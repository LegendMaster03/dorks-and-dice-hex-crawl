using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record AggregateConsequenceTransitionResult(
    StoredExpedition Expedition,
    ExpeditionConsequenceStatus Status,
    string Detail,
    bool StateChanged);

/// <summary>
/// Pure aggregate transition shared by journey orchestration when a resolved operation produces a
/// Phase 10 consequence. It reuses Phase 10 effect processing and the Phase 11 resource consumer,
/// and it advances only the existing expedition runtime clock for resolved time delay. It performs
/// no persistence by itself, allowing the caller to commit journey/effect/resource/runtime changes
/// in one optimistic-concurrency save.
/// </summary>
public static class ExpeditionConsequenceAggregateTransition
{
    public static AggregateConsequenceTransitionResult Apply(
        StoredExpedition expedition,
        ExpeditionConsequence consequence,
        ExpeditionConsequenceProvenance resolutionProvenance)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(consequence);
        consequence.ValidateAgainst(expedition.Party);
        resolutionProvenance.Validate();
        expedition.Effects.Validate(expedition.Party);
        expedition.Resources.Validate(expedition.Party);

        var prior = expedition.Effects.AppliedConsequences.SingleOrDefault(value => value.ConsequenceId == consequence.Id);
        if (prior is not null)
        {
            return new(expedition, ExpeditionConsequenceStatus.AlreadyApplied,
                $"Consequence '{consequence.Id:D}' was already consumed as {prior.Status}.", false);
        }

        if (consequence.Category == ExpeditionConsequenceCategory.TimeDelay)
        {
            return ApplyTimeDelay(expedition, consequence);
        }

        var policy = PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure);
        var processed = ExpeditionConsequenceEngine.Process(
            expedition.Effects,
            policy,
            consequence,
            expedition.Party);
        var working = expedition with { Effects = processed.State };
        var changed = processed.StateChanged;
        var status = processed.Status;
        var detail = processed.Detail;

        var pending = working.Effects.PendingConsequences.SingleOrDefault(value => value.Consequence.Id == consequence.Id);
        if (pending?.UnresolvedComponents.Any(value => value is ResourceChangeConsequenceComponent) == true)
        {
            var resource = ResourceConsequenceConsumer.Consume(
                working.Resources,
                working.Effects,
                working.Party,
                consequence.Id,
                resolutionProvenance);
            if (resource.StateChanged)
            {
                working = working with { Resources = resource.Resources, Effects = resource.Effects };
                changed = true;
            }
            status = resource.Status;
            detail = resource.Detail;
        }

        working.Effects.Validate(working.Party);
        working.Resources.Validate(working.Party);
        return new(working, status, detail, changed);
    }

    private static AggregateConsequenceTransitionResult ApplyTimeDelay(
        StoredExpedition expedition,
        ExpeditionConsequence consequence)
    {
        var components = consequence.Components.OfType<TimeDelayConsequenceComponent>().ToArray();
        if (components.Length != consequence.Components.Count)
        {
            throw new InvalidOperationException("A time-delay consequence can contain only time-delay components.");
        }

        TimeSpan delay;
        try
        {
            delay = components.Aggregate(TimeSpan.Zero, (current, component) => current + component.ToTimeSpan());
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("Resolved time delay exceeds the supported runtime duration.", exception);
        }
        if (delay <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Resolved time delay must be positive.");
        }

        var hasActiveInterval = expedition.Runtime switch
        {
            ExpeditionState spatial => spatial.ActiveWatch is not null,
            CellExpeditionState cells => cells.ActiveWatch is not null,
            NonSpatialSessionState nonSpatial => nonSpatial.ActiveWatch is not null,
            _ => true
        };
        if (hasActiveInterval)
        {
            var detail = "Time delay can not mutate the expedition clock while an active interval is in progress without corrupting its remaining duration.";
            var record = new AppliedConsequenceRecord(
                consequence.Id,
                consequence.ConsequenceKey,
                ExpeditionConsequenceStatus.Deferred,
                [],
                consequence.Provenance,
                detail);
            var pending = new PendingExpeditionConsequence(
                consequence,
                ExpeditionConsequenceStatus.Deferred,
                detail,
                "Complete or explicitly resolve the active interval, then adjudicate the structured delay.");
            var effects = expedition.Effects with
            {
                AppliedConsequences = expedition.Effects.AppliedConsequences.Append(record).ToArray(),
                PendingConsequences = expedition.Effects.PendingConsequences.Append(pending).ToArray()
            };
            effects.Validate(expedition.Party);
            return new(expedition with { Effects = effects }, ExpeditionConsequenceStatus.Deferred, detail, true);
        }

        var recorded = ExpeditionConsequenceEngine.RecordAppliedExternalMutation(
            expedition.Effects,
            consequence,
            expedition.Party,
            $"Advanced the existing expedition clock by {delay} without creating a second time authority.");
        if (recorded.Status == ExpeditionConsequenceStatus.AlreadyApplied)
        {
            return new(expedition, recorded.Status, recorded.Detail, false);
        }

        CrawlSessionRuntimeState runtime;
        try
        {
            runtime = expedition.Runtime switch
            {
                ExpeditionState spatial => spatial with { ElapsedTravelTime = spatial.ElapsedTravelTime + delay },
                CellExpeditionState cells => cells with { ElapsedTravelTime = cells.ElapsedTravelTime + delay },
                NonSpatialSessionState nonSpatial => nonSpatial with { ElapsedTime = nonSpatial.ElapsedTime + delay },
                _ => throw new InvalidOperationException("Unsupported crawl runtime state for time-delay application.")
            };
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("Time delay would exceed the supported expedition clock duration.", exception);
        }

        return new(
            expedition with { Runtime = runtime, Effects = recorded.State },
            recorded.Status,
            recorded.Detail,
            true);
    }
}
