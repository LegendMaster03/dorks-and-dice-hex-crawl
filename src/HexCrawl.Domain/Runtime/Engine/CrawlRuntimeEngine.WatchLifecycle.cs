using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
    private static (ExpeditionState State, ActiveWatchState Active) StartWatch(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        var encounter = ResolveEncounterForNewWatch(profile, inputs.Encounter);
        var active = new ActiveWatchState(
            state.CompletedWatches + 1,
            profile.WatchLength,
            TimeSpan.Zero,
            plan,
            encounter,
            encounter.Kind == EncounterOutcomeKind.None,
            null);
        var activities = plan.Mode.Activities.Count == 0
            ? "none"
            : string.Join(", ", plan.Mode.Activities);

        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.WatchStarted,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Watch {active.WatchNumber} started ({profile.Name}); pace {plan.Mode.PaceKey}; activities {activities}.");

        state = state with { ActiveWatch = active };
        state = ResolveNavigationAtWatchStart(
            profile,
            state,
            plan,
            inputs.Navigation,
            events,
            active.WatchNumber);

        if (profile.EncounterCadence != EncounterCheckCadence.None)
        {
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.EncounterCheckPerformed,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Encounter check resolved as {encounter.Kind}.");
        }

        return (state, active);
    }

    private static (ExpeditionState State, ActiveWatchState Active) ResumePendingDecision(
        ExpeditionState state,
        ActiveWatchState active,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        if (active.PendingDecision == RuntimePauseReason.LostRecognitionRequired)
        {
            var decision = inputs.BoundaryDecision
                ?? throw new InvalidOperationException("A lost-recognition boundary decision is required before travel can continue.");
            if (decision.RecognizedLost && decision.Reorient)
            {
                state = state with { Navigation = new NavigationRuntimeState(false, 0) };
                events.Add(
                    active.WatchNumber,
                    CrawlRuntimeEventKind.ExpeditionReoriented,
                    state.ElapsedTravelTime,
                    state.CurrentHex,
                    "The expedition recognized that it was lost and reoriented.");
            }
        }

        active = active with { PendingDecision = null };
        state = state with { ActiveWatch = active };
        return (state, active);
    }

    private static ResolvedEncounter ResolveEncounterForNewWatch(
        CrawlProcedureProfile profile,
        ResolvedEncounter? supplied)
    {
        if (profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return ResolvedEncounter.None;
        }

        var encounter = supplied
            ?? throw new InvalidOperationException("This procedure requires an explicit resolved encounter-check outcome.");
        if (encounter.Kind == EncounterOutcomeKind.None)
        {
            return encounter;
        }

        if (encounter.OccursAt is null || encounter.OccursAt < TimeSpan.Zero || encounter.OccursAt > profile.WatchLength)
        {
            throw new InvalidOperationException("Triggered encounter time must fall within the watch.");
        }

        if (encounter.Kind == EncounterOutcomeKind.KeyedLocationDiscovery && encounter.LocationId is null)
        {
            throw new InvalidOperationException("A keyed-location encounter requires a location id.");
        }

        return encounter;
    }

    private static WatchAdvanceResult TriggerEncounter(
        ExpeditionState state,
        ActiveWatchState active,
        EventCollector events)
    {
        var encounter = active.Encounter;
        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.EncounterTriggered,
            state.ElapsedTravelTime,
            state.CurrentHex,
            string.IsNullOrWhiteSpace(encounter.Note)
                ? $"{encounter.Kind} triggered."
                : $"{encounter.Kind}: {encounter.Note}",
            subjectId: encounter.LocationId);

        active = active with
        {
            EncounterHandled = true,
            PendingDecision = RuntimePauseReason.EncounterTriggered
        };
        state = state with { ActiveWatch = active };
        return Finish(state, RuntimePauseReason.EncounterTriggered, active.Remaining, events);
    }

    private static WatchAdvanceResult CompleteWatch(
        ExpeditionState state,
        ActiveWatchState active,
        EventCollector events)
    {
        state = state with
        {
            CompletedWatches = Math.Max(state.CompletedWatches, active.WatchNumber),
            ActiveWatch = null
        };
        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.WatchCompleted,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Watch {active.WatchNumber} completed.");
        return Finish(state, null, TimeSpan.Zero, events);
    }

    private static void EmitDmOverrideIfNeeded(
        ExpeditionState state,
        int watchNumber,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        var overrideApplied = inputs.Travel.Provenance.Source == ResolutionSource.DmOverride
            || inputs.Navigation?.Provenance.Source == ResolutionSource.DmOverride
            || inputs.Encounter?.Provenance.Source == ResolutionSource.DmOverride
            || inputs.BoundaryDecision?.Provenance.Source == ResolutionSource.DmOverride
            || !string.IsNullOrWhiteSpace(inputs.DmOverrideNote);
        if (!overrideApplied)
        {
            return;
        }

        var note = inputs.DmOverrideNote
            ?? inputs.Travel.Provenance.Note
            ?? inputs.Navigation?.Provenance.Note
            ?? inputs.Encounter?.Provenance.Note
            ?? inputs.BoundaryDecision?.Provenance.Note
            ?? "DM supplied an authoritative override.";
        events.Add(
            watchNumber,
            CrawlRuntimeEventKind.DmOverrideApplied,
            state.ElapsedTravelTime,
            state.CurrentHex,
            note);
    }

    private static WatchAdvanceResult Finish(
        ExpeditionState state,
        RuntimePauseReason? pause,
        TimeSpan remaining,
        EventCollector events)
    {
        var newEvents = events.NewEvents.ToArray();
        if (newEvents.Length > 0)
        {
            state = state with { History = state.History.Concat(newEvents).ToArray() };
        }
        return new WatchAdvanceResult(state, pause, remaining, newEvents);
    }


}
