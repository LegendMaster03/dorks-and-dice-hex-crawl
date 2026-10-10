using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
    private static (ExpeditionState State, ActiveWatchState Active) StartWatch(
        GenericProcedureRuntime procedure,
        ExpeditionState state,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        var encounterCheckDue = GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);
        var encounter = ResolveEncounterForNewWatch(
            procedure.Time.IntervalDuration,
            encounterCheckDue,
            inputs.Encounter);
        var active = new ActiveWatchState(
            state.CompletedWatches + 1,
            procedure.Time.IntervalDuration,
            TimeSpan.Zero,
            plan,
            encounter,
            encounter.Kind == EncounterOutcomeKind.None,
            null);
        var assignments = plan.Mode.ActivityAssignments.Count == 0
            ? "none"
            : string.Join(", ", plan.Mode.ActivityAssignments.Select(DescribeAssignment));

        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.WatchStarted,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Watch {active.WatchNumber} started ({procedure.Name}); pace {plan.Mode.PaceKey}; participant assignments {assignments}.");

        state = state with { ActiveWatch = active };
        state = ResolveNavigationAtWatchStart(
            procedure.Navigation,
            state,
            plan,
            inputs.Navigation,
            events,
            active.WatchNumber);

        if (encounterCheckDue)
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

    private static string DescribeAssignment(ParticipantActivityAssignment assignment)
    {
        var target = assignment.ParticipantId?.ToString() ?? "party";
        var activity = string.IsNullOrWhiteSpace(assignment.ActivityKey) ? null : $"activity={assignment.ActivityKey}";
        var role = string.IsNullOrWhiteSpace(assignment.RoleKey) ? null : $"role={assignment.RoleKey}";
        var details = string.Join("/", new[] { activity, role }.Where(value => value is not null));
        return string.IsNullOrWhiteSpace(details) ? target : $"{target}:{details}";
    }

    private static (ExpeditionState State, ActiveWatchState Active) ResumePendingDecision(
        ExpeditionState state,
        ActiveWatchState active,
        BoundaryNavigationDecision? boundaryDecision,
        EventCollector events)
    {
        if (active.PendingDecision == RuntimePauseReason.LostRecognitionRequired)
        {
            var decision = boundaryDecision
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

    /// <summary>
    /// One authoritative encounter-time slicing rule for both legacy hex
    /// and generalized cell watches. The event itself is owned by each
    /// session's position adapter; the elapsed-time cutoff is shared.
    /// </summary>
    private readonly record struct WatchEncounterWindow(
        TimeSpan Duration, bool EncounterAtEnd, bool EncounterImmediatelyDue);

    private static WatchEncounterWindow ResolveEncounterWindow(
        TimeSpan callRemaining,
        TimeSpan elapsed,
        TimeSpan totalDuration,
        ResolvedEncounter encounter,
        bool alreadyHandled)
    {
        if (alreadyHandled || encounter.Kind == EncounterOutcomeKind.None)
            return new WatchEncounterWindow(callRemaining, false, false);

        var due = encounter.OccursAt
            ?? throw new InvalidOperationException("A triggered encounter needs its scheduled watch time.");
        if (due < TimeSpan.Zero || due > totalDuration)
            throw new InvalidOperationException("The encounter falls outside the active watch.");
        if (due <= elapsed)
            return new WatchEncounterWindow(TimeSpan.Zero, false, true);

        var untilEncounter = due - elapsed;
        if (untilEncounter <= callRemaining)
            return new WatchEncounterWindow(untilEncounter, true, false);
        return new WatchEncounterWindow(callRemaining, false, false);
    }

    private static ResolvedEncounter ResolveEncounterForNewWatch(
        TimeSpan intervalDuration,
        bool encounterCheckDue,
        ResolvedEncounter? supplied)
    {
        if (!encounterCheckDue)
        {
            return ResolvedEncounter.None;
        }

        var encounter = supplied
            ?? throw new InvalidOperationException("This procedure requires an explicit resolved encounter-check outcome.");
        if (encounter.Kind == EncounterOutcomeKind.None)
        {
            return encounter;
        }

        if (encounter.OccursAt is null || encounter.OccursAt < TimeSpan.Zero || encounter.OccursAt > intervalDuration)
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
        var occurrenceId = EncounterOccurrenceIdentity.Create(state.Id, events.NextSequence);
        var triggered = events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.EncounterTriggered,
            state.ElapsedTravelTime,
            state.CurrentHex,
            string.IsNullOrWhiteSpace(encounter.Note)
                ? $"{encounter.Kind} triggered."
                : $"{encounter.Kind}: {encounter.Note}",
            subjectId: encounter.LocationId,
            encounterOutcome: encounter.Kind,
            encounterNote: encounter.Note,
            encounterProvenance: encounter.Provenance,
            encounterOccurrenceId: occurrenceId);

        active = active with
        {
            EncounterHandled = true,
            PendingDecision = RuntimePauseReason.EncounterTriggered
        };
        state = state with
        {
            ActiveWatch = active,
            PendingEncounter = new PendingEncounterOccurrence(
                occurrenceId, triggered.Sequence, active.WatchNumber, encounter.Kind,
                state.ElapsedTravelTime, state.CurrentHex, encounter.LocationId,
                encounter.Note, encounter.Provenance)
        };
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
