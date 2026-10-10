using System.Text.Json;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase18CellWatchRuntimeTests
{
    private static readonly ResolutionProvenance Manual = new(ResolutionSource.ManualRoll);

    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    [InlineData("<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>")]
    public void CellWatchAdvancesTimeAndEmitsQualifiedReciprocalEvents(string dsSymbol)
    {
        var (world, procedure, state, plan) = Setup(dsSymbol, calibrated: true);
        var duration = GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration;
        var result = new CrawlRuntimeEngine().AdvanceCellWatch(
            world, procedure, state, plan with { ContinueAcrossBoundaries = true },
            Inputs(1));

        Assert.Null(result.PauseReason);
        Assert.Equal(TimeSpan.Zero, result.RemainingWatchTime);
        Assert.Equal(duration, result.Expedition.ElapsedTravelTime);
        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Null(result.Expedition.ActiveWatch);
        Assert.NotNull(result.Expedition.DistanceTraveled);
        Assert.True(result.Expedition.DistanceTraveled!.Value.Value > 0);
        var exited = Assert.Single(result.Events, e => e.Kind == CrawlRuntimeEventKind.CellExited);
        var entered = Assert.Single(result.Events, e => e.Kind == CrawlRuntimeEventKind.CellEntered);
        Assert.Equal(state.Traversal.CurrentCell, exited.Cell);
        Assert.Equal(result.Expedition.Traversal.CurrentCell, entered.Cell);
        Assert.Equal(exited.ReciprocalInterfaceIndex, entered.BoundaryInterfaceIndex);
        Assert.Equal(exited.BoundaryInterfaceIndex, entered.ReciprocalInterfaceIndex);
        Assert.Null(exited.Hex);
        Assert.Null(entered.Hex);
        Assert.DoesNotContain(result.Events, e =>
            e.Kind is CrawlRuntimeEventKind.HexEntered or CrawlRuntimeEventKind.HexExited);
        Assert.Equal(result.Events.Count, result.Events.Select(e => e.Sequence).Distinct().Count());
        Assert.Equal(result.Events.Count, result.Expedition.History.Count);
    }

    [Fact]
    public void NavigationDmOverrideNoteIsRecordedWhenTravelUsesManualResolution()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true);
        var result = new CrawlRuntimeEngine().AdvanceCellWatch(
            world, procedure, state, plan,
            Inputs(0) with
            {
                Navigation = new ResolvedCellNavigation(
                    NavigationCheckOutcome.Succeeded, null,
                    new ResolutionProvenance(ResolutionSource.DmOverride,
                        "Navigator used a DM-resolved course."))
            });
        var audit = Assert.Single(result.Events,
            e => e.Kind == CrawlRuntimeEventKind.DmOverrideApplied);
        Assert.Equal("Navigator used a DM-resolved course.", audit.Message);
        Assert.Equal(state.Traversal.CurrentCell, audit.Cell);
        Assert.Null(audit.Hex);
    }

    [Fact]
    public void DeliberateReverseAtTerminalBoundaryCompletesWithoutStrandedPause()
    {
        var (world, procedure, initial, plan) = Setup(
            "<1:1,1,1:4,4>", calibrated: true);
        var engine = new CrawlRuntimeEngine();
        var paused = engine.AdvanceCellWatch(world, procedure, initial, plan, Inputs(2));
        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, paused.PauseReason);
        Assert.NotNull(paused.Expedition.Traversal.EntryInterfaceIndex);
        Assert.Equal(initial.Traversal.CurrentCell,
            paused.Expedition.Traversal.EnteredFrom);
        var reverse = world.Resolve(initial.Traversal.CurrentCell.Address).Center
            - paused.Expedition.Traversal.Position;
        var reversePlan = plan with
        {
            IntendedHeading = reverse,
            DeliberateDoubleBack = true
        };
        var back = engine.AdvanceCellWatch(
            world, procedure, paused.Expedition, reversePlan, Inputs(1));
        Assert.Null(back.PauseReason);
        Assert.Null(back.Expedition.ActiveWatch);
        Assert.Equal(1, back.Expedition.CompletedWatches);
        Assert.Equal(initial.Traversal.CurrentCell, back.Expedition.Traversal.CurrentCell);
        Assert.Equal(GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration,
            back.Expedition.ElapsedTravelTime);
        Assert.Single(back.Events, e => e.Kind == CrawlRuntimeEventKind.WatchCompleted);
        Assert.DoesNotContain(back.Events, e =>
            e.Kind == CrawlRuntimeEventKind.ConditionsReviewRequired);
        Assert.Equal(2, back.Expedition.History.Count(e =>
            e.Kind == CrawlRuntimeEventKind.CellEntered));
        Assert.Equal(back.Expedition.History.Count,
            back.Expedition.History.Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void BoundaryReviewPausePersistsAndResumesRemainingTime()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true);
        var engine = new CrawlRuntimeEngine();
        var paused = engine.AdvanceCellWatch(world, procedure, state, plan, Inputs(3));
        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, paused.PauseReason);
        Assert.Single(paused.Events, e => e.Kind == CrawlRuntimeEventKind.CellEntered);
        Assert.True(paused.RemainingWatchTime > TimeSpan.Zero);
        Assert.True(paused.Expedition.ElapsedTravelTime > TimeSpan.Zero);

        var reloaded = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(paused.Expedition))!;
        reloaded.Validate(world);
        var resumed = engine.AdvanceCellWatch(world, procedure, reloaded,
            plan with { ContinueAcrossBoundaries = true }, Inputs(2));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(1, resumed.Expedition.CompletedWatches);
        Assert.Equal(GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration,
            resumed.Expedition.ElapsedTravelTime);
        Assert.Equal(3, resumed.Expedition.History.Count(e => e.Kind == CrawlRuntimeEventKind.CellEntered));
        Assert.Equal(resumed.Expedition.History.Count,
            resumed.Expedition.History.Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void AmbiguousVertexRequiresAdjudicationWithoutFakingTimeOrNeighbor()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true);
        var vertexHeading = world.Resolve(state.Traversal.CurrentCell.Address).Polygon[0]
            - state.Traversal.Position;
        state = state with
        {
            Traversal = state.Traversal with { SelectedExitInterfaceIndex = null }
        };
        plan = plan with { IntendedHeading = vertexHeading };
        var result = new CrawlRuntimeEngine().AdvanceCellWatch(
            world, procedure, state, plan, Inputs(1));
        Assert.Equal(RuntimePauseReason.CellCourseAdjudicationRequired, result.PauseReason);
        Assert.Equal(state.Traversal.CurrentCell, result.Expedition.Traversal.CurrentCell);
        Assert.Equal(TimeSpan.Zero, result.Expedition.ElapsedTravelTime);
        Assert.NotNull(result.Expedition.ActiveWatch);
        Assert.Contains(result.Events, e => e.Kind == CrawlRuntimeEventKind.CellCourseAdjudicationRequired);
        Assert.DoesNotContain(result.Events, e => e.Kind == CrawlRuntimeEventKind.CellEntered);
    }

    [Fact]
    public void ExplicitAtomicExitDecisionResolvesVertexPauseWithoutInventingAdjacency()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true);
        var cell = world.Resolve(state.Traversal.CurrentCell.Address);
        var ambiguousHeading = cell.Polygon[0] - cell.Center;
        state = state with
        {
            Traversal = state.Traversal with { SelectedExitInterfaceIndex = null }
        };
        var engine = new CrawlRuntimeEngine();
        var paused = engine.AdvanceCellWatch(world, procedure, state,
            plan with { IntendedHeading = ambiguousHeading }, Inputs(1));
        Assert.Equal(RuntimePauseReason.CellCourseAdjudicationRequired, paused.PauseReason);
        Assert.Throws<InvalidOperationException>(() =>
            engine.AdvanceCellWatch(world, procedure, paused.Expedition,
                plan with { IntendedHeading = ambiguousHeading }, Inputs(1)));

        var boundary = world.Boundaries(cell.Id.Address)[0];
        var midpoint = new WorldPoint(
            (boundary.Start.X + boundary.End.X) / 2,
            (boundary.Start.Y + boundary.End.Y) / 2);
        var heading = midpoint - cell.Center;
        var invalid = new CellCourseDecision(
            heading, (boundary.InterfaceIndex + 1) % world.Boundaries(cell.Id.Address).Count, Manual);
        Assert.Throws<InvalidOperationException>(() =>
            engine.ResolveCellCourse(world, paused.Expedition, invalid));

        var decided = engine.ResolveCellCourse(world, paused.Expedition,
            new CellCourseDecision(heading, boundary.InterfaceIndex, Manual));
        Assert.Null(decided.PauseReason);
        Assert.Equal(boundary.InterfaceIndex, decided.Expedition.Traversal.SelectedExitInterfaceIndex);
        var audit = Assert.Single(decided.Events,
            e => e.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded);
        Assert.Equal(state.Traversal.CurrentCell, audit.Cell);
        Assert.Null(audit.Hex);
        Assert.Contains("ManualRoll", audit.Message);
        Assert.Contains(boundary.InterfaceIndex.ToString(), audit.Message);
        var resumed = engine.AdvanceCellWatch(world, procedure,
            decided.Expedition, decided.Expedition.ActiveWatch!.Plan, Inputs(1));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(boundary.To, resumed.Expedition.Traversal.CurrentCell);
        Assert.Equal(1, resumed.Expedition.CompletedWatches);
        Assert.Single(resumed.Expedition.History,
            e => e.Kind == CrawlRuntimeEventKind.CellEntered);
    }

    [Fact]
    public void LostCellWatchRejectsReorientationWithoutRecognizingNavigationError()
    {
        var (world, procedure, state, plan) = Setup(
            "<1:1,1,1:4,4>", calibrated: true, advanced: true);
        var frozen = state with
        {
            IsLost = true,
            ResolvedVeerDegrees = 15,
            IntendedHeading = plan.IntendedHeading,
            ActiveWatch = new CellActiveWatchState(
                1, TimeSpan.FromHours(4), TimeSpan.FromHours(1),
                plan, ResolvedEncounter.None, true,
                RuntimePauseReason.LostRecognitionRequired)
        };
        frozen.Validate(world);
        var inputs = Inputs(1) with
        {
            BoundaryDecision = new BoundaryNavigationDecision(false, true, Manual)
        };
        Assert.Throws<InvalidOperationException>(() =>
            new CrawlRuntimeEngine().AdvanceCellWatch(
                world, procedure, frozen, plan, inputs));
        Assert.Equal(RuntimePauseReason.LostRecognitionRequired,
            frozen.ActiveWatch!.PendingDecision);
    }

    [Fact]
    public void MidWatchCourseChangeIsAuditedWithQualifiedCellAndNoAxialSubstitution()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true);
        var engine = new CrawlRuntimeEngine();
        var paused = engine.AdvanceCellWatch(world, procedure, state, plan, Inputs(3));
        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, paused.PauseReason);
        var current = world.Resolve(paused.Expedition.Traversal.CurrentCell.Address);
        var otherExit = world.Boundaries(current.Id.Address)
            .First(edge => edge.To != paused.Expedition.Traversal.EnteredFrom);
        var midpoint = new WorldPoint(
            (otherExit.Start.X + otherExit.End.X) / 2,
            (otherExit.Start.Y + otherExit.End.Y) / 2);
        var newHeading = midpoint - paused.Expedition.Traversal.Position;
        var changed = engine.AdvanceCellWatch(
            world, procedure, paused.Expedition,
            plan with { IntendedHeading = newHeading, ContinueAcrossBoundaries = true },
            Inputs(1));
        var decision = Assert.Single(changed.Events,
            e => e.Kind == CrawlRuntimeEventKind.DirectionChanged);
        Assert.Equal(paused.Expedition.Traversal.CurrentCell, decision.Cell);
        Assert.Null(decision.Hex);
        Assert.True(decision.ExpeditionElapsedTime >= paused.Expedition.ElapsedTravelTime);
        Assert.DoesNotContain(changed.Events, e => e.Kind is
            CrawlRuntimeEventKind.HexEntered or CrawlRuntimeEventKind.HexExited);
        Assert.Equal(changed.Expedition.History.Count,
            changed.Expedition.History.Select(e => e.Sequence).Distinct().Count());
    }

    [Fact]
    public void CannotSubmitAtomicExitDecisionWithoutAdjudicationPause()
    {
        var (world, _, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true);
        var selected = state.Traversal.SelectedExitInterfaceIndex!.Value;
        var decision = new CellCourseDecision(plan.IntendedHeading, selected, Manual);
        Assert.Throws<InvalidOperationException>(() =>
            new CrawlRuntimeEngine().ResolveCellCourse(world, state, decision));
    }

    [Fact]
    public void CorruptPendingEncounterCellFailsValidation()
    {
        var (world, _, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true);
        var encounter = new PendingEncounterOccurrence(Guid.NewGuid(), 1, 1,
            EncounterOutcomeKind.WanderingEncounter,
            TimeSpan.Zero, null, null, null, Manual)
        { Cell = state.Traversal.CurrentCell };
        var current = state with
        {
            IntendedHeading = plan.IntendedHeading,
            ActiveWatch = new CellActiveWatchState(1, TimeSpan.FromHours(4), TimeSpan.Zero,
                plan, ResolvedEncounter.None, true, RuntimePauseReason.EncounterTriggered),
            PendingEncounter = encounter
        };
        current.Validate(world);
        Assert.Throws<InvalidOperationException>(() =>
            (current with { PendingEncounter = null }).Validate(world));
        Assert.Throws<InvalidOperationException>(() =>
            (current with { PendingEncounter = encounter with { Cell = null } }).Validate(world));
        Assert.Throws<InvalidOperationException>(() =>
            (current with { PendingEncounter = encounter with { Hex = new HexCoordinate(0, 0) } }).Validate(world));
        Assert.Throws<InvalidOperationException>(() =>
            (current with { ActiveWatch = current.ActiveWatch! with { PendingDecision = null } }).Validate(world));
    }

    [Fact]
    public void NonCalibratedCellStepWatchDoesNotInventDistance()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: false);
        var result = new CrawlRuntimeEngine().AdvanceCellWatch(
            world, procedure, state, plan, Inputs(1));
        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Null(result.Expedition.DistanceTraveled);
        Assert.DoesNotContain(result.Events, e => e.Kind == CrawlRuntimeEventKind.DistanceTraveled);
    }

    [Fact]
    public void CalibratedContinuousCellWatchKeepsExactWithinCellPositionAndElapsedTime()
    {
        var (world, procedure, state, plan) = Setup(
            "<1:1,1,1:4,4>", calibrated: true,
            mode: TravelResolutionMode.ContinuousDistance);
        var exit = PeriodicCellTraversalGeometry.NextCrossing(world, state.Traversal);
        Assert.Equal(CellCrossingStatus.Crosses, exit.Status);
        var distance = new DistanceMeasure(
            exit.DistanceWorldUnits!.Value * world.PhysicalDistancePerWorldUnit!.Value.Value / 3,
            DistanceUnit.Miles);
        var result = new CrawlRuntimeEngine().AdvanceCellWatch(
            world, procedure, state, plan,
            new CellWatchAdvanceInputs(
                ResolvedTravelAmount.Distance(distance, distance, Manual),
                Encounter: ResolvedEncounter.None));

        Assert.Null(result.PauseReason);
        Assert.DoesNotContain(result.Events, e => e.Kind == CrawlRuntimeEventKind.CellEntered);
        Assert.Equal(state.Traversal.CurrentCell, result.Expedition.Traversal.CurrentCell);
        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Equal(GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration,
            result.Expedition.ElapsedTravelTime);
        Assert.InRange(result.Expedition.DistanceTraveled!.Value.Value,
            distance.Value - 1e-8, distance.Value + 1e-8);
        Assert.True(state.Traversal.Position.DistanceTo(result.Expedition.Traversal.Position) > 0);
    }

    [Fact]
    public void UncalibratedContinuousWatchFailsWithoutAlteringCellState()
    {
        var (world, procedure, state, plan) = Setup(
            "<1:1,1,1:4,4>", calibrated: false,
            mode: TravelResolutionMode.ContinuousDistance);
        var amount = new DistanceMeasure(1, DistanceUnit.Miles);
        Assert.Throws<NotSupportedException>(() =>
            new CrawlRuntimeEngine().AdvanceCellWatch(
                world, procedure, state, plan,
                new CellWatchAdvanceInputs(
                    ResolvedTravelAmount.Distance(amount, amount, Manual),
                    Encounter: ResolvedEncounter.None)));
        Assert.Empty(state.History);
        Assert.Equal(TimeSpan.Zero, state.ElapsedTravelTime);
    }

    [Fact]
    public void NavigationUsesAngularVeerWithoutHexDirectionAndDoesNotAutomaticallyRecoverLost()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true, advanced: true);
        var engine = new CrawlRuntimeEngine();
        var failed = engine.AdvanceCellWatch(world, procedure, state, plan,
            Inputs(0) with
            {
                Navigation = new ResolvedCellNavigation(
                    NavigationCheckOutcome.Failed, 15, Manual)
            });
        Assert.True(failed.Expedition.IsLost);
        Assert.Equal(15, failed.Expedition.ResolvedVeerDegrees);
        Assert.Equal(1, failed.Expedition.CompletedWatches);
        Assert.NotEqual(plan.IntendedHeading, failed.Expedition.Traversal.TravelHeading);

        var success = engine.AdvanceCellWatch(world, procedure, failed.Expedition, plan,
            Inputs(0));
        Assert.True(success.Expedition.IsLost);
        Assert.Equal(15, success.Expedition.ResolvedVeerDegrees);
        Assert.Equal(2, success.Expedition.CompletedWatches);
    }

    [Fact]
    public void EncounterPausesAtScheduledTimeAndCanResolveExactlyOnce()
    {
        var (world, procedure, state, plan) = Setup("<1:1,1,1:4,4>", calibrated: true, advanced: true);
        var engine = new CrawlRuntimeEngine();
        var total = GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration;
        var encounter = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter,
            TimeSpan.FromTicks(total.Ticks / 2),
            null, "A wandering patrol", Manual);
        var result = engine.AdvanceCellWatch(
            world, procedure, state, plan, Inputs(1) with { Encounter = encounter });

        Assert.Equal(RuntimePauseReason.EncounterTriggered, result.PauseReason);
        var pending = Assert.IsType<PendingEncounterOccurrence>(result.Expedition.PendingEncounter);
        Assert.Equal(result.Expedition.Traversal.CurrentCell, pending.Cell);
        Assert.Null(pending.Hex);
        Assert.Equal(encounter.OccursAt, result.Expedition.ElapsedTravelTime);
        Assert.NotNull(result.Expedition.ActiveWatch);
        Assert.Throws<InvalidOperationException>(() => engine.AdvanceCellWatch(
            world, procedure, result.Expedition, plan, Inputs(1)));

        var loaded = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(result.Expedition))!;
        var resolved = engine.ResolveCellEncounter(loaded, pending.Id, "Resolved by DM");
        Assert.Null(resolved.Expedition.PendingEncounter);
        Assert.Contains(resolved.Events, e =>
            e.Kind == CrawlRuntimeEventKind.EncounterResolved && e.EncounterOccurrenceId == pending.Id);
        Assert.Throws<InvalidOperationException>(() =>
            engine.ResolveCellEncounter(resolved.Expedition, pending.Id));

        var resumed = engine.AdvanceCellWatch(
            world, procedure, resolved.Expedition, plan, Inputs(1));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(1, resumed.Expedition.CompletedWatches);
        Assert.Equal(total, resumed.Expedition.ElapsedTravelTime);
        Assert.Single(resumed.Expedition.History, e => e.Kind == CrawlRuntimeEventKind.EncounterTriggered);
    }

    [Theory]
    [InlineData(TravelResolutionMode.CellSteps, true)]
    [InlineData(TravelResolutionMode.CellSteps, false)]
    [InlineData(TravelResolutionMode.ContinuousDistance, true)]
    [InlineData(TravelResolutionMode.ContinuousDistance, false)]
    public void TerminalLostBoundaryStillTriggersScheduledEncounterOnceAfterRecognition(
        TravelResolutionMode movementMode, bool reorient)
    {
        var (world, procedure, initial, plan) = Setup(
            "<1:1,1,1:4,4>", calibrated: true, advanced: true, mode: movementMode);
        const double veerDegrees = 30d;
        var radians = veerDegrees * Math.PI / 180;
        var rotated = new WorldPoint(
            plan.IntendedHeading.X * Math.Cos(radians) - plan.IntendedHeading.Y * Math.Sin(radians),
            plan.IntendedHeading.X * Math.Sin(radians) + plan.IntendedHeading.Y * Math.Cos(radians));
        var crossing = PeriodicCellTraversalGeometry.NextCrossing(world,
            initial.Traversal with { TravelHeading = rotated });
        Assert.Equal(CellCrossingStatus.Crosses, crossing.Status);
        var manual = new ResolutionProvenance(ResolutionSource.ManualRoll);
        ResolvedTravelAmount travel = movementMode == TravelResolutionMode.CellSteps
            ? ResolvedTravelAmount.CellTransitions(1, manual)
            : ResolvedTravelAmount.Distance(
                world.MeasurePhysicalDistance(initial.Traversal.Position, crossing.Point!.Value),
                world.MeasurePhysicalDistance(initial.Traversal.Position, crossing.Point!.Value),
                manual);
        var duration = GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration;
        var scheduled = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter, duration, null,
            "Terminal encounter", manual);
        var engine = new CrawlRuntimeEngine();
        var interrupted = engine.AdvanceCellWatch(
            world, procedure, initial, plan,
            new CellWatchAdvanceInputs(
                travel, new ResolvedCellNavigation(
                    NavigationCheckOutcome.Failed, veerDegrees, manual), scheduled));
        Assert.Equal(RuntimePauseReason.LostRecognitionRequired, interrupted.PauseReason);
        Assert.Equal(TimeSpan.Zero, interrupted.RemainingWatchTime);
        Assert.Equal(duration, interrupted.Expedition.ElapsedTravelTime);
        Assert.NotNull(interrupted.Expedition.ActiveWatch);
        Assert.False(interrupted.Expedition.ActiveWatch!.EncounterHandled);
        Assert.Null(interrupted.Expedition.PendingEncounter);
        Assert.DoesNotContain(interrupted.Events, e =>
            e.Kind == CrawlRuntimeEventKind.EncounterTriggered);

        // Simulate restart from a persisted runtime snapshot at the exact
        // zero-remaining pause (no requested movement may be replayed).
        var restored = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(interrupted.Expedition))!;
        restored.Validate(world);
        var recognized = engine.AdvanceCellWatch(
            world, procedure, restored, plan,
            new CellWatchAdvanceInputs(
                movementMode == TravelResolutionMode.CellSteps
                    ? ResolvedTravelAmount.CellTransitions(0, manual)
                    : ResolvedTravelAmount.Distance(
                        new DistanceMeasure(0, DistanceUnit.Miles),
                        new DistanceMeasure(0, DistanceUnit.Miles), manual),
                BoundaryDecision: new BoundaryNavigationDecision(
                    reorient, reorient, manual)));
        Assert.Equal(RuntimePauseReason.EncounterTriggered, recognized.PauseReason);
        Assert.Equal(TimeSpan.Zero, recognized.RemainingWatchTime);
        Assert.Equal(duration, recognized.Expedition.ElapsedTravelTime);
        Assert.Equal(!reorient, recognized.Expedition.IsLost);
        var pending = Assert.IsType<PendingEncounterOccurrence>(
            recognized.Expedition.PendingEncounter);
        Assert.Equal(recognized.Expedition.Traversal.CurrentCell, pending.Cell);
        Assert.Null(pending.Hex);
        var triggered = Assert.Single(recognized.Events,
            e => e.Kind == CrawlRuntimeEventKind.EncounterTriggered);
        Assert.Equal(pending.Id, triggered.EncounterOccurrenceId);
        Assert.Equal(duration, triggered.ExpeditionElapsedTime);
        Assert.Equal(pending.Cell, triggered.Cell);
        Assert.Throws<InvalidOperationException>(() =>
            engine.AdvanceCellWatch(world, procedure, recognized.Expedition, plan,
                new CellWatchAdvanceInputs(travel)));
        var resolved = engine.ResolveCellEncounter(
            recognized.Expedition, pending.Id, "resolved");
        Assert.Equal(pending.Id, Assert.Single(resolved.Events,
            e => e.Kind == CrawlRuntimeEventKind.EncounterResolved).EncounterOccurrenceId);
        var completed = engine.AdvanceCellWatch(
            world, procedure, resolved.Expedition, plan,
            new CellWatchAdvanceInputs(
                movementMode == TravelResolutionMode.CellSteps
                    ? ResolvedTravelAmount.CellTransitions(0, manual)
                    : ResolvedTravelAmount.Distance(
                        new DistanceMeasure(0, DistanceUnit.Miles),
                        new DistanceMeasure(0, DistanceUnit.Miles), manual)));
        Assert.Null(completed.PauseReason);
        Assert.Equal(1, completed.Expedition.CompletedWatches);
        Assert.Null(completed.Expedition.ActiveWatch);
        Assert.Equal(duration, completed.Expedition.ElapsedTravelTime);
        Assert.Single(completed.Expedition.History,
            e => e.Kind == CrawlRuntimeEventKind.EncounterTriggered);
        Assert.Single(completed.Expedition.History,
            e => e.Kind == CrawlRuntimeEventKind.EncounterResolved);
        Assert.Single(completed.Expedition.History,
            e => e.Kind == CrawlRuntimeEventKind.WatchCompleted);
        Assert.Equal(completed.Expedition.History.Count,
            completed.Expedition.History.Select(e => e.Sequence).Distinct().Count());
    }

    private static CellWatchAdvanceInputs Inputs(int steps) => new(
        ResolvedTravelAmount.CellTransitions(steps, Manual),
        new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, Manual),
        ResolvedEncounter.None);

    private static (PeriodicWorldTiling World, CampaignProcedure Procedure, CellExpeditionState State,
        CellWatchTravelPlan Plan) Setup(string symbol, bool calibrated, bool advanced = false,
        TravelResolutionMode mode = TravelResolutionMode.CellSteps)
    {
        var generated = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1.5, "world-unit");
        Assert.Equal("realized", generated.Status);
        var world = new PeriodicWorldTiling(Guid.NewGuid(),
            generated.Topology!, generated.Realization!, new WorldPoint(0, 0),
            PhysicalDistancePerWorldUnit: calibrated
                ? new DistanceMeasure(2, DistanceUnit.Miles)
                : null);
        world.Validate();
        var original = (advanced ? TestProcedureProfiles.AdvancedContinuous() : TestProcedureProfiles.FixedDistance()).Materialize();
        var modules = original.Modules
            .Where(m => m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.HexProgressPolicy)
            .Select(m =>
            {
                if (m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return m;
                var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                parameters["travelResolution"] = mode.ToString();
                parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                parameters["tracksIntraHexProgress"] = "false";
                return m with { Mechanic = m.Mechanic with { Version = 2 }, Parameters = parameters };
            }).ToArray();
        var procedure = original with
        {
            TilingDsSymbol = world.Topology.QuotientDsSymbol,
            Modules = modules
        };
        procedure.Validate();

        var cell = world.Resolve(new PeriodicCellAddress(
            world.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var boundary = world.Boundaries(cell.Id.Address)[0];
        var midpoint = new WorldPoint((boundary.Start.X + boundary.End.X) / 2,
            (boundary.Start.Y + boundary.End.Y) / 2);
        var heading = midpoint - cell.Center;
        var state = new CellExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = new PeriodicCellTraversal
            {
                CurrentCell = cell.Id,
                Position = cell.Center,
                TravelHeading = heading,
                SelectedExitInterfaceIndex = boundary.InterfaceIndex
            },
            DistanceTraveled = calibrated ? new DistanceMeasure(0, DistanceUnit.Miles) : null
        };
        var plan = new CellWatchTravelPlan(heading, false, false,
            TravelModeSelection.Normal, NavigationAidSelection.None);
        return (world, procedure, state, plan);
    }
}
