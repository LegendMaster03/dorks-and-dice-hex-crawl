using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record NonSpatialWatchAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public double ElapsedHours { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ProcedureDefault;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
}

public sealed record EncounterCadenceAssistantCommand
{
    public long ExpectedVersion { get; init; }
    public EncounterOutcomeKind Outcome { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public string? ResolutionNote { get; init; }
    public string? Note { get; init; }
}

public sealed record ResolveEncounterCommand(
    long ExpectedVersion,
    Guid OccurrenceId,
    ResolutionSource ResolutionSource = ResolutionSource.DmOverride,
    string? ResolutionNote = null,
    string? ResultNote = null);

public sealed class ExpeditionAssistantService(
    IHexCrawlStore store,
    HexCrawlService coreService)
{
    public async Task<StoredExpedition> RecordNonSpatialWatchAsync(
        Guid expeditionId,
        string ownerUserId,
        NonSpatialWatchAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        EnsureNoPendingEncounter(expedition);
        JourneyRuntimeIntegration.EnsureRelevantTravelAllowed(expedition);

        if (expedition.Context is not NonSpatialCrawlSessionContext)
        {
            throw new InvalidOperationException("Non-spatial watch bookkeeping requires a non-spatial crawl session.");
        }

        var stateBefore = expedition.Runtime as NonSpatialSessionState
            ?? throw new InvalidOperationException("Non-spatial watch bookkeeping requires non-spatial session state.");
        var intervalDuration = ExpeditionProcedureRequirements.ResolveIntervalDuration(expedition.CampaignProcedure);

        var state = CrawlAssistantActions.RecordWatch(
            intervalDuration,
            stateBefore,
            new NonSpatialWatchAssistantInput(
                TimeSpan.FromHours(command.ElapsedHours),
                ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote),
                command.Note),
            ParticipantActivityPolicyResolver.SnapshotAssignments(expedition.Party));

        var forcedTravel = ForcedTravelAccounting.AccountTravelMutation(
            expedition,
            stateBefore,
            state,
            command.ExpectedVersion,
            "focused-nonspatial-travel",
            new ExpeditionConsequenceProvenance(
                ExpeditionConsequenceSourceKind.Procedure,
                "focused-nonspatial-travel",
                Note: command.Note));
        var journey = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition,
            stateBefore,
            state,
            expedition.Journey);

        return await SaveAsync(
            expedition with
            {
                Runtime = state,
                RemainingWatchTime = state.ActiveWatch?.Remaining ?? TimeSpan.Zero,
                Survival = forcedTravel.Survival,
                Journey = journey
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> RecordEncounterCadenceAsync(
        Guid expeditionId,
        string ownerUserId,
        EncounterCadenceAssistantCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        EnsureNoPendingEncounter(expedition);

        var input = new EncounterCadenceAssistantInput(
            command.Outcome,
            ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote),
            command.Note);

        if (expedition.Runtime is not NonSpatialSessionState nonSpatial)
            throw new InvalidOperationException(
                "Independent encounter cadence is available only for nonspatial journeys.");
        var runtime = CrawlAssistantActions.RecordEncounterCadence(nonSpatial, input);

        var pending = runtime.PendingEncounter;
        return await SaveAsync(
            expedition with
            {
                Runtime = runtime,
                PauseReason = pending is null ? expedition.PauseReason : RuntimePauseReason.EncounterTriggered,
                RemainingWatchTime = pending is null ? expedition.RemainingWatchTime
                    : runtime.ActiveWatch?.Remaining ?? TimeSpan.Zero
            },
            command.ExpectedVersion,
            cancellationToken);
    }

    public async Task<StoredExpedition> ResolveEncounterAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveEncounterCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var pending = expedition.Runtime.PendingEncounter
            ?? throw new InvalidOperationException("No encounter is pending for this expedition.");
        if (pending.Id != command.OccurrenceId)
        {
            throw new InvalidOperationException("The requested encounter occurrence is not the encounter currently pending for this expedition.");
        }
        if (expedition.PauseReason != RuntimePauseReason.EncounterTriggered)
        {
            throw new InvalidOperationException("Pending encounter state is inconsistent with the expedition interruption.");
        }

        var provenance = ClientSuppliedProvenance(command.ResolutionSource, command.ResolutionNote);
        var history = expedition.Runtime.History.ToList();
        var sequence = history.Count == 0 ? 1 : history[^1].Sequence + 1;
        if (expedition.Runtime is not NonSpatialSessionState nonSpatial)
            throw new InvalidOperationException(
                "Independent encounter resolution is available only for nonspatial journeys.");
        var elapsed = nonSpatial.ElapsedTime;
        history.Add(new CrawlRuntimeEvent(
            sequence,
            pending.WatchNumber,
            CrawlRuntimeEventKind.EncounterResolved,
            elapsed,
            string.IsNullOrWhiteSpace(command.ResultNote)
                ? $"{pending.Outcome} resolved."
                : $"{pending.Outcome} resolved: {command.ResultNote.Trim()}",
            EncounterOutcome: pending.Outcome,
            EncounterNote: command.ResultNote,
            EncounterProvenance: provenance,
            EncounterOccurrenceId: pending.Id));

        var runtime = nonSpatial with { PendingEncounter = null, History = history };
        var remaining = runtime.ActiveWatch?.Remaining ?? TimeSpan.Zero;
        return await SaveAsync(
            expedition with { Runtime = runtime, PauseReason = null, RemainingWatchTime = remaining },
            command.ExpectedVersion,
            cancellationToken);
    }

    private static void EnsureNoPendingEncounter(StoredExpedition expedition)
    {
        if (expedition.Runtime.PendingEncounter is not null)
        {
            throw new InvalidOperationException("Resolve the pending encounter before continuing this expedition procedure.");
        }
    }

    private static ResolutionProvenance ClientSuppliedProvenance(
        ResolutionSource source,
        string? note)
    {
        if (source == ResolutionSource.AutomaticRoll)
        {
            throw new InvalidOperationException(
                "AutomaticRoll is reserved for server-verified procedure-helper results and can not be supplied to a focused manual assistant.");
        }
        return new ResolutionProvenance(source, note);
    }

    private async Task<StoredExpedition> SaveAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The expedition was changed by another request. Reload it before saving focused-assistant bookkeeping."),
            _ => throw new HexCrawlNotFoundException("Expedition was not found.")
        };
    }

    private static void RequireVersion(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new HexCrawlConcurrencyException("The resource version is stale. Reload it before saving again.");
        }
    }
}