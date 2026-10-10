using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>Resolved angular navigation, never an invented hex direction.</summary>
public sealed record ResolvedCellNavigation(
    NavigationCheckOutcome Outcome,
    double? VeerDegreesOnFailure,
    ResolutionProvenance Provenance);

public sealed record CellCourseDecision(
    WorldPoint IntendedHeading,
    int SelectedExitInterfaceIndex,
    ResolutionProvenance Provenance);

public sealed record CellWatchAdvanceInputs(
    ResolvedTravelAmount Travel,
    ResolvedCellNavigation? Navigation = null,
    ResolvedEncounter? Encounter = null,
    BoundaryNavigationDecision? BoundaryDecision = null,
    string? DmOverrideNote = null);

public sealed record CellWatchAdvanceResult(
    CellExpeditionState Expedition,
    RuntimePauseReason? PauseReason,
    TimeSpan RemainingWatchTime,
    IReadOnlyList<CrawlRuntimeEvent> Events);

/// <summary>
/// Generalized cells use the same deterministic engine, pinned procedure,
/// encounter cadence, event collector, and watch time accounting as hexes.
/// Only the geometry and angular navigation are cell-specific.
/// </summary>
public sealed partial class CrawlRuntimeEngine
{
    public CellWatchAdvanceResult AdvanceCellWatch(
        PeriodicWorldTiling world, CampaignProcedure procedure,
        CellExpeditionState expedition, CellWatchTravelPlan plan,
        CellWatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inputs);
        if (expedition.PendingEncounter is not null)
            throw new InvalidOperationException("Resolve the pending encounter before continuing travel.");

        var runtime = GenericProcedureRuntime.Bind(procedure);
        if (runtime.Movement.MechanicVersion != 2)
            throw new NotSupportedException("Cell watches require an explicit version-2 movement procedure.");
        expedition.Validate(world);
        ValidateCellPlan(plan);
        ValidateCellTravel(runtime.Movement, inputs.Travel);

        var events = new EventCollector(expedition.History);
        var state = expedition;
        var active = state.ActiveWatch;

        if (active is { PendingDecision: { } pause })
        {
            if (pause == RuntimePauseReason.EncounterTriggered)
                throw new InvalidOperationException("Resolve the pending encounter before resuming travel.");
            if (pause == RuntimePauseReason.LostRecognitionRequired)
            {
                var decision = inputs.BoundaryDecision
                    ?? throw new InvalidOperationException("Resolve lost recognition before continuing.");
                if (decision.Reorient && !decision.RecognizedLost)
                    throw new InvalidOperationException("Reorientation requires recognizing that the party is lost.");
                if (decision.RecognizedLost && decision.Reorient)
                {
                    state = state with { IsLost = false, ResolvedVeerDegrees = null };
                    AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.ExpeditionReoriented,
                        state, "The expedition recognized its error and reoriented.");
                }
            }
            active = active with { PendingDecision = null };
            state = state with { ActiveWatch = active };
        }

        if (active is null)
        {
            var checkDue = GenericProcedureRuntimeRequirements.IsEncounterCheckDue(runtime, state);
            var encounter = ResolveEncounterForNewWatch(
                runtime.Time.IntervalDuration, checkDue, inputs.Encounter);
            active = new CellActiveWatchState(
                state.CompletedWatches + 1, runtime.Time.IntervalDuration, TimeSpan.Zero,
                plan, encounter, encounter.Kind == EncounterOutcomeKind.None, null);
            state = state with { ActiveWatch = active, IntendedHeading = plan.IntendedHeading };
            AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.WatchStarted, state,
                $"Watch {active.WatchNumber} started ({runtime.Name}); pace {plan.Mode.PaceKey}.");
            state = ResolveCellNavigation(runtime.Navigation, state, plan, inputs.Navigation,
                active.WatchNumber, events);
            if (checkDue)
                AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.EncounterCheckPerformed,
                    state, $"Encounter check resolved as {encounter.Kind}.");
        }

        active = active with { Plan = plan };
        state = state with { ActiveWatch = active, IntendedHeading = plan.IntendedHeading };
        if (plan.DeliberateDoubleBack)
        {
            var entry = state.Traversal.EntryInterfaceIndex
                ?? throw new InvalidOperationException("Double-back requires a known entry interface.");
            state = state with
            {
                IsLost = false, ResolvedVeerDegrees = null,
                Traversal = state.Traversal with { SelectedExitInterfaceIndex = entry }
            };
        }

        var degrees = state.IsLost ? state.ResolvedVeerDegrees ?? 0d : 0d;
        var heading = RotateCellHeading(plan.IntendedHeading, degrees);
        state = state with { Traversal = state.Traversal with { TravelHeading = heading } };
        if (inputs.Travel.Provenance.Source == ResolutionSource.DmOverride
            || inputs.Navigation?.Provenance.Source == ResolutionSource.DmOverride
            || inputs.Encounter?.Provenance.Source == ResolutionSource.DmOverride
            || inputs.BoundaryDecision?.Provenance.Source == ResolutionSource.DmOverride
            || !string.IsNullOrWhiteSpace(inputs.DmOverrideNote))
            AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.DmOverrideApplied,
                state, inputs.DmOverrideNote ?? inputs.Travel.Provenance.Note ?? "DM resolved an override.");

        if (active.Remaining == TimeSpan.Zero)
            return CompleteCellWatch(state, active, events);

        AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.TravelResolved, state,
            runtime.Movement.TravelResolution == TravelResolutionMode.CellSteps
                ? $"Travel resolved at {inputs.Travel.CellSteps} cell step(s)."
                : $"Travel resolved at {Format(inputs.Travel.ActualDistance!.Value.Value)} {inputs.Travel.ActualDistance.Value.Unit.Symbol}.");

        var callDuration = active.Remaining;
        var segmentDuration = callDuration;
        var encounterAtEnd = false;
        if (!active.EncounterHandled && active.Encounter.Kind != EncounterOutcomeKind.None)
        {
            var due = active.Encounter.OccursAt!.Value;
            if (due <= active.Elapsed)
                return TriggerCellEncounter(state, active, events);
            if (due - active.Elapsed <= segmentDuration)
            {
                segmentDuration = due - active.Elapsed;
                encounterAtEnd = true;
            }
        }

        var fraction = Math.Clamp(segmentDuration.Ticks / (double)callDuration.Ticks, 0, 1);
        ResolvedTravelAmount segmentTravel;
        if (runtime.Movement.TravelResolution == TravelResolutionMode.CellSteps)
        {
            var steps = segmentDuration == callDuration ? inputs.Travel.CellSteps!.Value
                : (int)Math.Floor(inputs.Travel.CellSteps!.Value * fraction + Epsilon);
            segmentTravel = ResolvedTravelAmount.CellTransitions(steps, inputs.Travel.Provenance);
        }
        else
        {
            var expected = inputs.Travel.ExpectedDistance!.Value;
            var actual = inputs.Travel.ActualDistance!.Value;
            segmentTravel = ResolvedTravelAmount.Distance(
                new DistanceMeasure(expected.Value * fraction, expected.Unit),
                new DistanceMeasure(actual.Value * fraction, actual.Unit),
                inputs.Travel.Provenance);
        }

        var movement = ResolveCellTravel(
            world, procedure, state.Traversal, segmentTravel,
            plan.ContinueAcrossBoundaries && !state.IsLost && !plan.DeliberateDoubleBack);

        var previousTime = state.ElapsedTravelTime;
        for (var i = 0; i < movement.Transitions.Count; i++)
        {
            var transition = movement.Transitions[i];
            var share = runtime.Movement.TravelResolution == TravelResolutionMode.CellSteps
                ? (i + 1d) / Math.Max(1, inputs.Travel.CellSteps!.Value)
                : transition.CumulativeWorldDistance /
                    (inputs.Travel.ActualDistance!.Value.ConvertTo(
                        world.PhysicalDistancePerWorldUnit!.Value.Unit).Value /
                        world.PhysicalDistancePerWorldUnit.Value.Value);
            var at = previousTime + ScaleTime(callDuration, Math.Clamp(share, 0d, 1d));
            AddCellCrossingEvent(events, active.WatchNumber, CrawlRuntimeEventKind.CellExited,
                at, transition.From, transition.ExitInterfaceIndex, transition.EntryInterfaceIndex);
            AddCellCrossingEvent(events, active.WatchNumber, CrawlRuntimeEventKind.CellEntered,
                at, transition.To, transition.EntryInterfaceIndex, transition.ExitInterfaceIndex);
        }

        RuntimePauseReason? stop = null;
        if (movement.RequiresAdjudication)
            stop = RuntimePauseReason.CellCourseAdjudicationRequired;
        else if (movement.Transitions.Count > 0 && plan.DeliberateDoubleBack)
            stop = RuntimePauseReason.BacktrackBoundaryReached;
        else if (movement.Transitions.Count > 0 && state.IsLost)
            stop = RuntimePauseReason.LostRecognitionRequired;
        else if (movement.BoundaryReviewRequired)
            stop = RuntimePauseReason.ConditionsReviewRequired;

        // Defer the event until the new elapsed time is recorded; events
        // following a boundary crossing must not jump backwards in time.
        var resetVeerAtBoundary = movement.Transitions.Count > 0 && state.IsLost
            && plan.NavigationAid.ResetsVeerAtBoundary
            && state.ResolvedVeerDegrees is not null;

        var spent = stop is null ? segmentDuration
            : runtime.Movement.TravelResolution == TravelResolutionMode.CellSteps
                ? inputs.Travel.CellSteps == 0 ? TimeSpan.Zero
                    : ScaleTime(callDuration,
                        (double)movement.CompletedCellSteps / inputs.Travel.CellSteps!.Value)
                : inputs.Travel.ActualDistance!.Value.Value == 0 ? TimeSpan.Zero
                    : ScaleTime(callDuration,
                        movement.ConsumedPhysicalDistance!.Value.ConvertTo(
                            inputs.Travel.ActualDistance.Value.Unit).Value /
                        inputs.Travel.ActualDistance.Value.Value);
        spent = ClampTime(spent, TimeSpan.Zero, segmentDuration);
        state = state with
        {
            Traversal = movement.Traversal,
            ElapsedTravelTime = previousTime + spent,
            DistanceTraveled = movement.ConsumedPhysicalDistance is { } measured
                ? state.DistanceTraveled is { } previous ? Add(previous, measured) : measured
                : state.DistanceTraveled
        };
        if (resetVeerAtBoundary)
        {
            state = state with { ResolvedVeerDegrees = null };
            AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.VeerReset,
                state, $"{plan.NavigationAid.Key} reset the veer at the boundary.");
        }
        active = active with
        {
            Elapsed = ClampTime(active.Elapsed + spent, TimeSpan.Zero, active.TotalDuration),
            PendingDecision = stop
        };
        state = state with { ActiveWatch = active };
        if (movement.ConsumedPhysicalDistance is { Value: > 0 } traveled)
            AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.DistanceTraveled,
                state, $"Traveled {Format(traveled.Value)} {traveled.Unit.Symbol}.",
                traveled.Value, traveled.Unit.Symbol);

        if (stop is not null)
        {
            var kind = stop == RuntimePauseReason.LostRecognitionRequired
                ? CrawlRuntimeEventKind.NavigationDecisionRequired
                : stop == RuntimePauseReason.CellCourseAdjudicationRequired
                    ? CrawlRuntimeEventKind.CellCourseAdjudicationRequired
                    : CrawlRuntimeEventKind.ConditionsReviewRequired;
            AddCellEvent(events, active.WatchNumber, kind, state,
                movement.PendingAdjudication?.Reason ??
                (stop == RuntimePauseReason.LostRecognitionRequired
                    ? "Resolve lost recognition before continuing."
                    : "Review the boundary and course before continuing."));
            return FinishCell(state, stop, active.Remaining, events);
        }
        if (encounterAtEnd)
            return TriggerCellEncounter(state, active, events);
        return active.Remaining == TimeSpan.Zero
            ? CompleteCellWatch(state, active, events)
            : FinishCell(state, null, active.Remaining, events);
    }

    /// <summary>
    /// Accept an explicit atomic-interface decision when the course is
    /// ambiguous. Merely naming a boundary does not authorize a phantom
    /// vertex crossing: the actual ray must leave through that boundary.
    /// </summary>
    public CellWatchAdvanceResult ResolveCellCourse(
        PeriodicWorldTiling world, CellExpeditionState expedition,
        CellCourseDecision decision)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(decision);
        expedition.Validate(world);
        if (expedition.PendingEncounter is not null)
            throw new InvalidOperationException("Resolve the pending encounter before changing course.");
        if (expedition.ActiveWatch?.PendingDecision == RuntimePauseReason.LostRecognitionRequired)
            throw new InvalidOperationException("Resolve lost recognition before changing course.");
        if (!double.IsFinite(decision.IntendedHeading.X)
            || !double.IsFinite(decision.IntendedHeading.Y)
            || decision.IntendedHeading.X == 0 && decision.IntendedHeading.Y == 0)
            throw new InvalidOperationException("The resolved course requires a finite, nonzero heading.");

        var actual = RotateCellHeading(decision.IntendedHeading,
            expedition.IsLost ? expedition.ResolvedVeerDegrees ?? 0d : 0d);
        var traversal = expedition.Traversal with
        {
            TravelHeading = actual,
            SelectedExitInterfaceIndex = decision.SelectedExitInterfaceIndex
        };
        traversal.Validate(world);
        var crossing = PeriodicCellTraversalGeometry.NextCrossing(world, traversal);
        if (crossing.Status != CellCrossingStatus.Crosses)
            throw new InvalidOperationException(crossing.Reason
                ?? "This course has no uniquely adjudicated atomic exit interface.");

        var active = expedition.ActiveWatch;
        if (active is not null)
            active = active with
            {
                Plan = active.Plan with { IntendedHeading = decision.IntendedHeading },
                PendingDecision = null
            };
        var next = expedition with
        {
            Traversal = traversal,
            IntendedHeading = decision.IntendedHeading,
            ActiveWatch = active
        };
        next.Validate(world);
        var events = new EventCollector(expedition.History);
        AddCellEvent(events, active?.WatchNumber ?? Math.Max(1, next.CompletedWatches + 1),
            CrawlRuntimeEventKind.DirectionChanged, next,
            $"DM confirmed atomic exit interface {decision.SelectedExitInterfaceIndex} for the current cell.");
        return FinishCell(next, null, active?.Remaining ?? TimeSpan.Zero, events);
    }

    public CellWatchAdvanceResult ResolveCellEncounter(
        CellExpeditionState expedition, Guid occurrenceId, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        var pending = expedition.PendingEncounter
            ?? throw new InvalidOperationException("No encounter is pending.");
        if (pending.Id != occurrenceId || pending.Cell != expedition.Traversal.CurrentCell)
            throw new InvalidOperationException("Encounter occurrence or cell identity mismatch.");
        var active = expedition.ActiveWatch
            ?? throw new InvalidOperationException("A pending encounter requires its active watch.");
        if (active.PendingDecision != RuntimePauseReason.EncounterTriggered)
            throw new InvalidOperationException("The watch is not paused for this encounter.");
        var events = new EventCollector(expedition.History);
        AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.EncounterResolved,
            expedition, note ?? "Encounter resolved.", encounterId: occurrenceId);
        return FinishCell(expedition with
        {
            PendingEncounter = null,
            ActiveWatch = active with { PendingDecision = null }
        }, null, active.Remaining, events);
    }

    private static CellExpeditionState ResolveCellNavigation(
        ProcedureNavigationRuntime policy, CellExpeditionState state,
        CellWatchTravelPlan plan, ResolvedCellNavigation? resolved,
        int watch, EventCollector events)
    {
        if (!policy.UsesNavigationChecks || plan.NavigationAid.SuppressesNavigationCheck
            || plan.DeliberateDoubleBack)
        {
            AddCellEvent(events, watch, CrawlRuntimeEventKind.NavigationCheckResolved,
                state, "Navigation check not required.");
            return state with { IsLost = false, ResolvedVeerDegrees = null };
        }
        var outcome = resolved ?? throw new InvalidOperationException("A resolved navigation check is required.");
        if (outcome.Outcome == NavigationCheckOutcome.NotRequired)
            throw new InvalidOperationException("A required navigation check can not be NotRequired.");
        var wasLost = state.IsLost;
        var priorVeer = state.ResolvedVeerDegrees;
        if (outcome.Outcome == NavigationCheckOutcome.Failed)
        {
            var veer = outcome.VeerDegreesOnFailure
                ?? throw new InvalidOperationException("A failed navigation check requires a resolved angular veer.");
            if (!double.IsFinite(veer) || veer == 0 || Math.Abs(veer) > 180)
                throw new InvalidOperationException("Veer must be finite, nonzero, and at most 180 degrees.");
            if (!wasLost || !policy.UsesPersistentVeer
                || Math.Abs(veer) > Math.Abs(priorVeer ?? 0))
                state = state with { IsLost = true, ResolvedVeerDegrees = veer };
        }
        AddCellEvent(events, watch, CrawlRuntimeEventKind.NavigationCheckResolved, state,
            $"Navigation check {outcome.Outcome.ToString().ToLowerInvariant()}.");
        if (!wasLost && state.IsLost)
            AddCellEvent(events, watch, CrawlRuntimeEventKind.ExpeditionBecameLost,
                state, "The expedition became lost.");
        if (priorVeer != state.ResolvedVeerDegrees)
            AddCellEvent(events, watch, CrawlRuntimeEventKind.VeerChanged,
                state, $"Veer changed to {Format(state.ResolvedVeerDegrees ?? 0)} degrees.");
        return state;
    }

    private static void ValidateCellPlan(CellWatchTravelPlan plan)
    {
        if (!double.IsFinite(plan.IntendedHeading.X) || !double.IsFinite(plan.IntendedHeading.Y)
            || (plan.IntendedHeading.X == 0d && plan.IntendedHeading.Y == 0d))
            throw new InvalidOperationException("Cell travel requires a finite nonzero intended heading.");
        ArgumentNullException.ThrowIfNull(plan.Mode);
        ArgumentNullException.ThrowIfNull(plan.NavigationAid);
        if (string.IsNullOrWhiteSpace(plan.Mode.PaceKey))
            throw new InvalidOperationException("A travel pace is required.");
    }

    private static void ValidateCellTravel(ProcedureMovementRuntime policy, ResolvedTravelAmount travel)
    {
        if (policy.TravelResolution == TravelResolutionMode.CellSteps)
        {
            if (travel.CellSteps is null or < 0 || travel.HexSteps is not null
                || travel.ExpectedDistance is not null || travel.ActualDistance is not null)
                throw new InvalidOperationException("Cell steps require one nonnegative count, without legacy hex steps or distances.");
        }
        else
        {
            ValidateNativeTravelAmountPolicy(policy, travel);
            if (travel.ExpectedDistance!.Value.Value < 0 || travel.ActualDistance!.Value.Value < 0)
                throw new InvalidOperationException("Travel distances must not be negative.");
        }
    }

    private static WorldPoint RotateCellHeading(WorldPoint vector, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new WorldPoint(vector.X * cosine - vector.Y * sine,
            vector.X * sine + vector.Y * cosine);
    }

    private static void AddCellEvent(
        EventCollector events, int watch, CrawlRuntimeEventKind kind,
        CellExpeditionState state, string message, double? distance = null,
        string? unit = null, Guid? encounterId = null)
    {
        var recorded = events.Add(watch, kind, state.ElapsedTravelTime, null, message,
            distance, unit, encounterOccurrenceId: encounterId);
        events.NewEvents[^1] = recorded with { Cell = state.Traversal.CurrentCell };
    }

    private static void AddCellCrossingEvent(
        EventCollector events, int watch, CrawlRuntimeEventKind kind,
        TimeSpan elapsed, WorldCellId cell, int interfaceIndex, int reciprocalIndex)
    {
        var recorded = events.Add(watch, kind, elapsed, null, $"Cell {cell} boundary crossed.");
        events.NewEvents[^1] = recorded with
        {
            Cell = cell,
            BoundaryInterfaceIndex = interfaceIndex,
            ReciprocalInterfaceIndex = reciprocalIndex
        };
    }

    private static CellWatchAdvanceResult TriggerCellEncounter(
        CellExpeditionState state, CellActiveWatchState active, EventCollector events)
    {
        var encounter = active.Encounter;
        var occurrenceId = EncounterOccurrenceIdentity.Create(state.Id, events.NextSequence);
        var emitted = events.Add(active.WatchNumber, CrawlRuntimeEventKind.EncounterTriggered,
            state.ElapsedTravelTime, null, encounter.Note ?? $"{encounter.Kind} triggered.",
            subjectId: encounter.LocationId, encounterOutcome: encounter.Kind,
            encounterNote: encounter.Note, encounterProvenance: encounter.Provenance,
            encounterOccurrenceId: occurrenceId);
        events.NewEvents[^1] = emitted with { Cell = state.Traversal.CurrentCell };
        active = active with { EncounterHandled = true, PendingDecision = RuntimePauseReason.EncounterTriggered };
        state = state with
        {
            ActiveWatch = active,
            PendingEncounter = new PendingEncounterOccurrence(
                occurrenceId, emitted.Sequence, active.WatchNumber, encounter.Kind,
                state.ElapsedTravelTime, null, encounter.LocationId, encounter.Note, encounter.Provenance)
            { Cell = state.Traversal.CurrentCell }
        };
        return FinishCell(state, RuntimePauseReason.EncounterTriggered, active.Remaining, events);
    }

    private static CellWatchAdvanceResult CompleteCellWatch(
        CellExpeditionState state, CellActiveWatchState active, EventCollector events)
    {
        state = state with
        {
            ActiveWatch = null,
            CompletedWatches = Math.Max(state.CompletedWatches, active.WatchNumber)
        };
        AddCellEvent(events, active.WatchNumber, CrawlRuntimeEventKind.WatchCompleted,
            state, $"Watch {active.WatchNumber} completed.");
        return FinishCell(state, null, TimeSpan.Zero, events);
    }

    private static CellWatchAdvanceResult FinishCell(
        CellExpeditionState state, RuntimePauseReason? pause,
        TimeSpan remaining, EventCollector events)
    {
        if (events.NewEvents.Count != 0)
            state = state with { History = state.History.Concat(events.NewEvents).ToArray() };
        return new CellWatchAdvanceResult(state, pause, remaining, events.NewEvents.ToArray());
    }
}
