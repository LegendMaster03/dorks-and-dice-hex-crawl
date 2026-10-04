using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public static class JourneyRuntimeIntegration
{
    public static void EnsureRelevantTravelAllowed(StoredExpedition expedition)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        var blockingProcess = expedition.Journey.ActiveProcesses.FirstOrDefault(value =>
            value.Execution.BlocksRelevantTravelWhileResolutionRequired
            && value.Status == JourneyProcessStatus.ResolutionRequired);
        if (blockingProcess is not null)
        {
            throw new InvalidOperationException(
                $"Journey process '{blockingProcess.ProcessKey}' requires resolution before further applicable travel.");
        }

        var eventPolicy = JourneyProcedurePolicyResolver.ResolveEvents(expedition.CampaignProcedure);
        if (eventPolicy.Support == JourneyPolicySupport.Supported
            && eventPolicy.BlocksRelevantTravelWhileResolutionRequired
            && expedition.Journey.EventOccurrences.Any(value => value.Status == JourneyEventStatus.ResolutionRequired))
        {
            throw new InvalidOperationException("The exact pinned journey-event policy requires pending event resolution before further applicable travel.");
        }
    }

    public static ExpeditionJourneyState ObserveCompletedWatches(
        StoredExpedition expedition,
        CrawlSessionRuntimeState before,
        CrawlSessionRuntimeState after,
        ExpeditionJourneyState state)
    {
        var beforeCount = CompletedWatches(before);
        var afterCount = CompletedWatches(after);
        if (afterCount <= beforeCount) return state;

        var eventPolicy = JourneyProcedurePolicyResolver.ResolveEvents(expedition.CampaignProcedure);
        var watchEventsApply = eventPolicy.Support == JourneyPolicySupport.Supported
            && eventPolicy.Supports(JourneyEventTriggerKind.WatchCompleted)
            && eventPolicy.LinkMode is JourneyEventLinkMode.Standalone or JourneyEventLinkMode.Both;
        var working = state;
        for (var watch = beforeCount + 1; watch <= afterCount; watch++)
        {
            var occurrenceKey = $"watch:{watch}";
            if (working.ObservedRuntimeOccurrenceIds.Contains(occurrenceKey, StringComparer.Ordinal)) continue;

            var processWatchApplies = working.ActiveProcesses.Any(value =>
                value.Execution.IntervalIntegrationModel == JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity);
            if (!processWatchApplies && !watchEventsApply)
            {
                continue;
            }

            var clock = JourneyClockReference.From(after);
            var provenance = new ExpeditionConsequenceProvenance(
                ExpeditionConsequenceSourceKind.Procedure,
                "completed-watch-journey-integration",
                SourceReference: occurrenceKey);

            var active = working.ActiveProcesses.ToList();
            var history = working.History.ToList();
            for (var index = 0; index < active.Count; index++)
            {
                var process = active[index];
                if (process.Execution.IntervalIntegrationModel != JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity)
                {
                    continue;
                }
                var pendingId = JourneyProcessEngine.DeterministicId(process.Id, occurrenceKey);
                if (process.PendingActions.Any(value => value.Id == pendingId)) continue;
                var pending = new JourneyPendingAction
                {
                    Id = pendingId,
                    Kind = JourneyPendingActionKind.WatchResolution,
                    StageKey = process.CurrentStageKey,
                    SourceReference = occurrenceKey,
                    Detail = "A completed authoritative watch created one process resolution opportunity. No progress amount was inferred."
                };
                active[index] = process with
                {
                    Status = JourneyProcessStatus.ResolutionRequired,
                    PendingActions = process.PendingActions.Append(pending).ToArray()
                };
                history.Add(new JourneyHistoryRecord
                {
                    Id = Guid.NewGuid(),
                    Kind = JourneyHistoryKind.WatchOpportunityCreated,
                    ProcessId = process.Id,
                    StageKey = process.CurrentStageKey,
                    ExpeditionTime = clock.ExpeditionTime,
                    CompletedWatches = clock.CompletedWatches,
                    Detail = $"Completed watch {watch} created one journey-process resolution opportunity.",
                    Provenance = provenance
                });
            }
            working = working with { ActiveProcesses = active, History = history };

            if (watchEventsApply)
            {
                var eventId = JourneyProcessEngine.DeterministicId(expedition.Id, $"journey-event:{occurrenceKey}");
                var eventResult = JourneyEventEngine.CreateOpportunity(
                    working,
                    eventPolicy,
                    new JourneyEventOpportunityInput
                    {
                        OccurrenceId = eventId,
                        Trigger = JourneyEventTriggerKind.WatchCompleted,
                        TriggerReference = occurrenceKey,
                        TargetKind = JourneyEventTargetKind.Unresolved,
                        Provenance = provenance,
                        Note = "Watch completion created an event evaluation opportunity; probability, event content, target selection, and consequences remain unresolved."
                    },
                    clock,
                    expedition.Party);
                working = eventResult.State;
            }

            working = working with
            {
                ObservedRuntimeOccurrenceIds = working.ObservedRuntimeOccurrenceIds.Append(occurrenceKey).ToArray()
            };
            working.Validate(expedition.Party);
        }
        return working;
    }

    private static int CompletedWatches(CrawlSessionRuntimeState runtime) => runtime switch
    {
        ExpeditionState spatial => spatial.CompletedWatches,
        NonSpatialSessionState nonSpatial => nonSpatial.CompletedWatches,
        _ => throw new InvalidOperationException("Unsupported expedition runtime state for journey watch integration.")
    };
}
