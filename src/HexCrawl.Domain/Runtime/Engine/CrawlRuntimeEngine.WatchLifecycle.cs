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
        var start = PrepareWatchStart(procedure, state, state.CompletedWatches, inputs.Encounter);
        var active = new ActiveWatchState(
            start.WatchNumber,
            start.Duration,
            TimeSpan.Zero,
            plan,
            start.Encounter,
            start.Encounter.Kind == EncounterOutcomeKind.None,
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

        if (start.EncounterCheckDue)
        {
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.EncounterCheckPerformed,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Encounter check resolved as {start.Encounter.Kind}.");
        }

        return (state, active);
    }

    // The procedure's watch duration, encounter cadence and outcome decision
    // are one policy for both legacy hex and generalized cell expeditions.
    // Geometry-specific plans and events remain adapters around this shared
    // start boundary.
    private readonly record struct WatchStartPolicy(
        int WatchNumber,
        TimeSpan Duration,
        ResolvedEncounter Encounter,
        bool EncounterCheckDue);

    private static WatchStartPolicy PrepareWatchStart(
        GenericProcedureRuntime procedure,
        CrawlSessionRuntimeState state,
        int completedWatches,
        ResolvedEncounter? suppliedEncounter)
    {
        if (completedWatches < 0)
            throw new InvalidOperationException("Completed watch count must be nonnegative.");
        var checkDue = GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);
        var encounter = ResolveEncounterForNewWatch(
            procedure.Time.IntervalDuration, checkDue, suppliedEncounter);
        return new WatchStartPolicy(
            checked(completedWatches + 1), procedure.Time.IntervalDuration,
            encounter, checkDue);
    }

    private static string DescribeAssignment(ParticipantActivityAssignment assignment)
    {
        var target = assignment.ParticipantId?.ToString() ?? "party";
        var activity = string.IsNullOrWhiteSpace(assignment.ActivityKey) ? null : $"activity={assignment.ActivityKey}";
        var role = string.IsNullOrWhiteSpace(assignment.RoleKey) ? null : $"role={assignment.RoleKey}";
        var details = string.Join("/", new[] { activity, role }.Where(value => value is not null));
        return string.IsNullOrWhiteSpace(details) ? target : $"{target}:{details}";
    }

    /// <summary>
    /// A shared recognition rule for legacy hex and qualified cell watches.
    /// Refusing reorientation is valid; reorienting without first recognizing
    /// the navigation error is not.
    /// </summary>
    private static bool ValidateLostRecognitionDecision(
        BoundaryNavigationDecision? decision)
    {
        if (decision is null)
            throw new InvalidOperationException(
                "A lost-recognition boundary decision is required before travel can continue.");
        if (decision.Reorient && !decision.RecognizedLost)
            throw new InvalidOperationException(
                "Reorientation requires recognizing that the expedition is lost.");
        return decision.RecognizedLost && decision.Reorient;
    }

    private static (ExpeditionState State, ActiveWatchState Active) ResumePendingDecision(
        ExpeditionState state,
        ActiveWatchState active,
        BoundaryNavigationDecision? boundaryDecision,
        EventCollector events)
    {
        if (active.PendingDecision == RuntimePauseReason.LostRecognitionRequired)
        {
            if (ValidateLostRecognitionDecision(boundaryDecision))
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

    /// <summary>
    /// Both spatial runtimes use the same occurrence identity and typed
    /// encounter-trigger metadata; only their authoritative coordinate differs.
    /// </summary>
    private static CrawlRuntimeEvent RecordEncounterTrigger(
        Guid expeditionId,
        int watchNumber,
        TimeSpan elapsed,
        HexCoordinate? hex,
        WorldCellId? cell,
        ResolvedEncounter encounter,
        EventCollector events,
        string message)
    {
        if (hex.HasValue == cell.HasValue)
            throw new InvalidOperationException(
                "An encounter trigger requires exactly one authoritative spatial identity.");
        var occurrenceId = EncounterOccurrenceIdentity.Create(
            expeditionId, events.NextSequence);
        var recorded = events.Add(
            watchNumber, CrawlRuntimeEventKind.EncounterTriggered,
            elapsed, hex, message,
            subjectId: encounter.LocationId,
            encounterOutcome: encounter.Kind,
            encounterNote: encounter.Note,
            encounterProvenance: encounter.Provenance,
            encounterOccurrenceId: occurrenceId,
            cell: cell);
        return recorded;
    }

    private static WatchAdvanceResult TriggerEncounter(
        ExpeditionState state,
        ActiveWatchState active,
        EventCollector events)
    {
        var encounter = active.Encounter;
        var triggered = RecordEncounterTrigger(
            state.Id, active.WatchNumber, state.ElapsedTravelTime,
            state.CurrentHex, null, encounter, events,
            string.IsNullOrWhiteSpace(encounter.Note)
                ? $"{encounter.Kind} triggered."
                : $"{encounter.Kind}: {encounter.Note}");
        var occurrenceId = triggered.EncounterOccurrenceId!.Value;

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

    /// <summary>
    /// Shared DM-override detection and note precedence. A cell/hex adapter
    /// supplies only its authoritative position to the event collector.
    /// </summary>
    private static string? ResolveDmOverrideNote(
        ResolutionProvenance travel,
        ResolutionProvenance? navigation,
        ResolutionProvenance? encounter,
        ResolutionProvenance? boundary,
        string? overrideNote)
    {
        if (travel.Source != ResolutionSource.DmOverride
            && navigation?.Source != ResolutionSource.DmOverride
            && encounter?.Source != ResolutionSource.DmOverride
            && boundary?.Source != ResolutionSource.DmOverride
            && string.IsNullOrWhiteSpace(overrideNote))
            return null;

        return overrideNote
            ?? travel.Note
            ?? navigation?.Note
            ?? encounter?.Note
            ?? boundary?.Note
            ?? "DM supplied an authoritative override.";
    }

    private static void EmitDmOverrideIfNeeded(
        ExpeditionState state,
        int watchNumber,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        var note = ResolveDmOverrideNote(
            inputs.Travel.Provenance,
            inputs.Navigation?.Provenance,
            inputs.Encounter?.Provenance,
            inputs.BoundaryDecision?.Provenance,
            inputs.DmOverrideNote);
        if (note is null)
            return;
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
