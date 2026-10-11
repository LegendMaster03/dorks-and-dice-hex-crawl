using System.Text.Json;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

/// <summary>
/// Phase 20.1 movement equivalence is geometry authority, not exact replay
/// of the old V1 exit-factor thresholds. Application/persistence cutover stays
/// outside these tests and remains a Phase 20.2 concern.
/// </summary>
public sealed class Phase20GeometricTurningRegressionTests
{
    private static readonly ResolutionProvenance Manual =
        new(ResolutionSource.ManualRoll);

    private static AbstractCellSessionStart Session() =>
        AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(),
            new AbstractHexCrawlSessionContext(
                "Mapless geometric test", HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(6, DistanceUnit.Miles))),
            new HexCoordinate(0, 0));

    private static CellWatchAdvanceInputs Inputs(double miles) => new(
        ResolvedTravelAmount.Distance(
            new DistanceMeasure(miles, DistanceUnit.Miles),
            new DistanceMeasure(miles, DistanceUnit.Miles), Manual),
        new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, Manual),
        ResolvedEncounter.None);

    private static CellWatchTravelPlan Plan(WorldPoint direction, bool continueAcross = true) =>
        new(direction, false, continueAcross,
            TravelModeSelection.Normal, NavigationAidSelection.None);

    [Fact]
    public void TurningBudgetDebtSurvivesWatchCompletionSerializationAndResume()
    {
        var session = Session();
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var engine = new CrawlRuntimeEngine();
        var east = SpatialCourseSelection.FromLegacyHexDirection(
            session.World, new HexCoordinate(0, 0), new HexDirection(0));
        var northEast = SpatialCourseSelection.FromLegacyHexDirection(
            session.World, new HexCoordinate(0, 0), new HexDirection(1));

        var first = engine.AdvanceCellWatch(
            session.World, procedure, session.Expedition, Plan(east), Inputs(1));
        Assert.Null(first.PauseReason);
        Assert.Equal(1, first.Expedition.CompletedWatches);
        Assert.Equal(1d, first.Expedition.DistanceTraveled!.Value.Value, 8);

        // The one-mile Alexandrian turn costs the entire 0.4-mile watch
        // budget and leaves 0.6 miles of nongeometric debt.
        var turning = engine.AdvanceCellWatch(
            session.World, procedure, first.Expedition, Plan(northEast), Inputs(0.4));
        Assert.Null(turning.PauseReason);
        Assert.Equal(2, turning.Expedition.CompletedWatches);
        Assert.Equal(first.Expedition.Traversal.Position, turning.Expedition.Traversal.Position);
        Assert.Equal(1d, turning.Expedition.DistanceTraveled!.Value.Value, 8);
        Assert.InRange(turning.Expedition.PendingTravelBudgetCharge!.Value.Value, 0.5999999, 0.6000001);
        Assert.Null(turning.Expedition.ActiveWatch);

        var restored = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(turning.Expedition))!;
        restored.Validate(session.World);
        Assert.Equal(turning.Expedition.Traversal.CurrentCell, restored.Traversal.CurrentCell);
        Assert.Equal(turning.Expedition.PendingTravelBudgetCharge, restored.PendingTravelBudgetCharge);

        // Same accepted course must pay only the outstanding 0.6 miles,
        // then use the remaining 0.15 miles for real displacement.
        var third = engine.AdvanceCellWatch(
            session.World, procedure, restored, Plan(northEast), Inputs(0.75));
        Assert.Null(third.PauseReason);
        Assert.Equal(3, third.Expedition.CompletedWatches);
        Assert.Null(third.Expedition.PendingTravelBudgetCharge);
        Assert.InRange(third.Expedition.DistanceTraveled!.Value.Value, 1.1499999, 1.1500001);
        Assert.True(third.Expedition.Traversal.Position.DistanceTo(restored.Traversal.Position) > 0.01);

        var fourth = engine.AdvanceCellWatch(
            session.World, procedure, third.Expedition, Plan(northEast), Inputs(1));
        Assert.Null(fourth.PauseReason);
        Assert.Equal(4, fourth.Expedition.CompletedWatches);
        Assert.InRange(fourth.Expedition.DistanceTraveled!.Value.Value, 2.1499999, 2.1500001);
        var debits = fourth.Expedition.History
            .Where(e => e.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
                && e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(2, debits.Length);
        Assert.InRange(debits.Sum(e => e.DistanceValue ?? 0d), 0.9999999, 1.0000001);
        Assert.Equal(TimeSpan.FromHours(16), fourth.Expedition.ElapsedTravelTime);
    }

    [Fact]
    public void RejectedCourseDoesNotChargeAndAcceptedCourseChargesOnceAfterReload()
    {
        var session = Session();
        var world = session.World;
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var center = world.Resolve(session.Expedition.Traversal.CurrentCell.Address);
        var edge = world.Boundaries(center.Id.Address)[0];
        var midpoint = new WorldPoint(
            (edge.Start.X + edge.End.X) / 2, (edge.Start.Y + edge.End.Y) / 2);
        var outward = midpoint - center.Center;
        var incoming = center.Center - midpoint;
        var atBoundary = session.Expedition with
        {
            Traversal = session.Expedition.Traversal with
            {
                Position = midpoint,
                TravelHeading = incoming,
                SelectedExitInterfaceIndex = null
            },
            IntendedHeading = incoming
        };
        atBoundary.Validate(world);

        var engine = new CrawlRuntimeEngine();
        var paused = engine.AdvanceCellWatch(
            world, procedure, atBoundary, Plan(outward), Inputs(2));
        Assert.Equal(RuntimePauseReason.CellCourseAdjudicationRequired, paused.PauseReason);
        Assert.Equal(TimeSpan.Zero, paused.Expedition.ElapsedTravelTime);
        Assert.Equal(midpoint, paused.Expedition.Traversal.Position);
        Assert.Null(paused.Expedition.PendingTravelBudgetCharge);
        Assert.True(paused.Expedition.ActiveWatch!.TurnPreflightWasDeferred);
        Assert.DoesNotContain(paused.Events, e => e.Kind ==
            CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase));

        // The pending adjudication and previous intent survive a snapshot.
        var restored = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(paused.Expedition))!;
        restored.Validate(world);
        Assert.True(restored.ActiveWatch!.TurnPreflightWasDeferred);
        Assert.Equal(incoming, restored.ActiveWatch.PendingTurnFromHeading);
        Assert.Throws<InvalidOperationException>(() => engine.ResolveCellCourse(
            world, restored,
            new CellCourseDecision(outward,
                (edge.InterfaceIndex + 1) % world.Boundaries(center.Id.Address).Count,
                Manual)));

        var accepted = engine.ResolveCellCourse(
            world, restored, new CellCourseDecision(outward, edge.InterfaceIndex, Manual));
        Assert.Null(accepted.PauseReason);
        Assert.False(accepted.Expedition.ActiveWatch!.TurnPreflightWasDeferred);
        Assert.Equal(incoming, accepted.Expedition.ActiveWatch.PendingTurnFromHeading);
        Assert.Equal(midpoint, accepted.Expedition.Traversal.Position);

        var resumed = engine.AdvanceCellWatch(
            world, procedure, accepted.Expedition,
            accepted.Expedition.ActiveWatch.Plan, Inputs(2));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(1, resumed.Expedition.CompletedWatches);
        Assert.Equal(edge.To, resumed.Expedition.Traversal.CurrentCell);
        Assert.InRange(resumed.Expedition.DistanceTraveled!.Value.Value, 0.9999999, 1.0000001);
        var charge = Assert.Single(resumed.Expedition.History,
            e => e.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
                && e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(charge.DistanceValue!.Value, 0.9999999, 1.0000001);
        Assert.Null(resumed.Expedition.PendingTravelBudgetCharge);
        Assert.Equal(TimeSpan.FromHours(4), resumed.Expedition.ElapsedTravelTime);
    }

    [Fact]
    public void EncounterInterruptsPartialTurnChargeAndResumePaysOnlyOutstandingDebt()
    {
        var session = Session();
        var world = session.World;
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var engine = new CrawlRuntimeEngine();
        var east = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(0));
        var northEast = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(1));
        var first = engine.AdvanceCellWatch(
            world, procedure, session.Expedition, Plan(east), Inputs(1));
        Assert.Null(first.PauseReason);

        // Only half of a 0.8-mile budget is available before the encounter,
        // so 0.4 miles pays the one-mile turn with 0.6 left outstanding.
        var encounter = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter, TimeSpan.FromHours(2),
            null, "An interrupted turn", Manual);
        var paused = engine.AdvanceCellWatch(
            world, procedure, first.Expedition, Plan(northEast),
            Inputs(0.8) with { Encounter = encounter });
        Assert.Equal(RuntimePauseReason.EncounterTriggered, paused.PauseReason);
        Assert.Equal(TimeSpan.FromHours(2), paused.RemainingWatchTime);
        Assert.Equal(TimeSpan.FromHours(6), paused.Expedition.ElapsedTravelTime);
        Assert.Equal(first.Expedition.Traversal.Position, paused.Expedition.Traversal.Position);
        Assert.InRange(paused.Expedition.PendingTravelBudgetCharge!.Value.Value,
            0.5999999, 0.6000001);
        Assert.Equal(1d, paused.Expedition.DistanceTraveled!.Value.Value, 8);
        var pending = Assert.IsType<PendingEncounterOccurrence>(
            paused.Expedition.PendingEncounter);

        // Save/reload while the encounter is pending, then resolve it and
        // continue with a new budget for the remaining half of the watch.
        var saved = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(paused.Expedition))!;
        saved.Validate(world);
        Assert.Equal(pending.Id, saved.PendingEncounter!.Id);
        var resolved = engine.ResolveCellEncounter(saved, pending.Id, "Resolved");
        Assert.Null(resolved.PauseReason);
        Assert.Equal(TimeSpan.FromHours(2), resolved.RemainingWatchTime);
        Assert.Null(resolved.Expedition.PendingEncounter);
        var resumed = engine.AdvanceCellWatch(
            world, procedure, resolved.Expedition, Plan(northEast),
            Inputs(0.75));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(2, resumed.Expedition.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(8), resumed.Expedition.ElapsedTravelTime);
        Assert.Null(resumed.Expedition.ActiveWatch);
        Assert.Null(resumed.Expedition.PendingTravelBudgetCharge);
        Assert.InRange(resumed.Expedition.DistanceTraveled!.Value.Value,
            1.1499999, 1.1500001);
        var debits = resumed.Expedition.History
            .Where(e => e.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
                && e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(2, debits.Length);
        Assert.InRange(debits.Sum(e => e.DistanceValue ?? 0), 0.9999999, 1.0000001);
        Assert.Single(resumed.Expedition.History, e =>
            e.Kind == CrawlRuntimeEventKind.EncounterTriggered);
        Assert.Single(resumed.Expedition.History, e =>
            e.Kind == CrawlRuntimeEventKind.EncounterResolved);
    }

    [Fact]
    public void FirstCourseAdjudicationDoesNotInventATurningCharge()
    {
        var session = Session();
        var world = session.World;
        var center = world.Resolve(session.Expedition.Traversal.CurrentCell.Address);
        var edge = world.Boundaries(center.Id.Address)[0];
        var midpoint = new WorldPoint(
            (edge.Start.X + edge.End.X) / 2, (edge.Start.Y + edge.End.Y) / 2);
        var outward = midpoint - center.Center;
        var positionWithoutPriorCourse = session.Expedition with
        {
            Traversal = session.Expedition.Traversal with
            {
                Position = midpoint,
                TravelHeading = null,
                SelectedExitInterfaceIndex = null
            },
            IntendedHeading = null
        };
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var engine = new CrawlRuntimeEngine();
        var paused = engine.AdvanceCellWatch(
            world, procedure, positionWithoutPriorCourse, Plan(outward), Inputs(2));
        Assert.Equal(RuntimePauseReason.CellCourseAdjudicationRequired, paused.PauseReason);
        Assert.Null(paused.Expedition.ActiveWatch!.PendingTurnFromHeading);
        Assert.True(paused.Expedition.ActiveWatch.TurnPreflightWasDeferred);

        var accepted = engine.ResolveCellCourse(
            world, paused.Expedition,
            new CellCourseDecision(outward, edge.InterfaceIndex, Manual));
        var resumed = engine.AdvanceCellWatch(
            world, procedure, accepted.Expedition, accepted.Expedition.ActiveWatch!.Plan, Inputs(2));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(1, resumed.Expedition.CompletedWatches);
        Assert.Equal(edge.To, resumed.Expedition.Traversal.CurrentCell);
        Assert.InRange(resumed.Expedition.DistanceTraveled!.Value.Value, 1.9999999, 2.0000001);
        Assert.DoesNotContain(resumed.Expedition.History, e =>
            e.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InsufficientBudgetDoesNotChargeAnUnadjudicatedBoundaryCourse()
    {
        var session = Session();
        var world = session.World;
        var cell = world.Resolve(session.Expedition.Traversal.CurrentCell.Address);
        var edge = world.Boundaries(cell.Id.Address)[0];
        var midpoint = new WorldPoint(
            (edge.Start.X + edge.End.X) / 2, (edge.Start.Y + edge.End.Y) / 2);
        var incoming = cell.Center - midpoint;
        var outward = midpoint - cell.Center;
        var state = session.Expedition with
        {
            Traversal = session.Expedition.Traversal with
            {
                Position = midpoint,
                TravelHeading = incoming,
                SelectedExitInterfaceIndex = null
            },
            IntendedHeading = incoming
        };
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var engine = new CrawlRuntimeEngine();

        // Only 0.4 miles are available; the full turning cost is 1 mile.
        // The course must be adjudicated BEFORE debiting the 0.4 miles.
        var paused = engine.AdvanceCellWatch(
            world, procedure, state, Plan(outward), Inputs(0.4));
        Assert.Equal(RuntimePauseReason.CellCourseAdjudicationRequired, paused.PauseReason);
        Assert.Equal(TimeSpan.Zero, paused.Expedition.ElapsedTravelTime);
        Assert.Equal(midpoint, paused.Expedition.Traversal.Position);
        Assert.Null(paused.Expedition.PendingTravelBudgetCharge);
        Assert.DoesNotContain(paused.Events, e =>
            e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase));

        Assert.Throws<InvalidOperationException>(() => engine.ResolveCellCourse(
            world, paused.Expedition, new CellCourseDecision(outward,
                (edge.InterfaceIndex + 1) % world.Boundaries(cell.Id.Address).Count, Manual)));
        var accepted = engine.ResolveCellCourse(
            world, paused.Expedition, new CellCourseDecision(outward, edge.InterfaceIndex, Manual));
        var result = engine.AdvanceCellWatch(
            world, procedure, accepted.Expedition,
            accepted.Expedition.ActiveWatch!.Plan, Inputs(0.4));
        Assert.Null(result.PauseReason);
        Assert.Equal(midpoint, result.Expedition.Traversal.Position);
        Assert.Equal(cell.Id, result.Expedition.Traversal.CurrentCell);
        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.InRange(result.Expedition.PendingTravelBudgetCharge!.Value.Value,
            0.5999999, 0.6000001);
        Assert.Single(result.Expedition.History, e =>
            e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ZeroTimeEncounterRetainsUnpaidTurnAfterResumption()
    {
        var session = Session();
        var world = session.World;
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var east = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(0));
        var northEast = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(1));
        var engine = new CrawlRuntimeEngine();
        var first = engine.AdvanceCellWatch(
            world, procedure, session.Expedition, Plan(east), Inputs(1));
        Assert.Null(first.PauseReason);
        var before = first.Expedition.Traversal.Position;

        var zeroTimeEncounter = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter, TimeSpan.Zero,
            null, "Encounter at watch start", Manual);
        var paused = engine.AdvanceCellWatch(
            world, procedure, first.Expedition, Plan(northEast),
            Inputs(0.4) with { Encounter = zeroTimeEncounter });
        Assert.Equal(RuntimePauseReason.EncounterTriggered, paused.PauseReason);
        Assert.Equal(TimeSpan.FromHours(4), paused.RemainingWatchTime);
        Assert.Equal(before, paused.Expedition.Traversal.Position);
        Assert.Null(paused.Expedition.PendingTravelBudgetCharge);
        Assert.Equal(east, paused.Expedition.ActiveWatch!.PendingTurnFromHeading);

        var serialized = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(paused.Expedition))!;
        serialized.Validate(world);
        var pending = Assert.IsType<PendingEncounterOccurrence>(serialized.PendingEncounter);
        var resolved = engine.ResolveCellEncounter(serialized, pending.Id, "Encounter resolved");
        var resumed = engine.AdvanceCellWatch(
            world, procedure, resolved.Expedition, Plan(northEast), Inputs(0.4));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(2, resumed.Expedition.CompletedWatches);
        Assert.Equal(before, resumed.Expedition.Traversal.Position);
        Assert.InRange(resumed.Expedition.PendingTravelBudgetCharge!.Value.Value,
            0.5999999, 0.6000001);
        Assert.Equal(TimeSpan.FromHours(8), resumed.Expedition.ElapsedTravelTime);
        Assert.Single(resumed.Expedition.History, e =>
            e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ZeroTimeEncounterBeforeAnyTravelDoesNotCreateAFictitiousCourseTurn()
    {
        var session = Session();
        var world = session.World;
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var east = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(0));
        var northEast = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(1));
        var engine = new CrawlRuntimeEngine();

        // A heading requested at an encounter occurring at the first instant
        // is not a traveled, accepted course.
        var encounter = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter, TimeSpan.Zero,
            null, "Encounter before first movement", Manual);
        var paused = engine.AdvanceCellWatch(
            world, procedure, session.Expedition, Plan(east),
            Inputs(2) with { Encounter = encounter });
        Assert.Equal(RuntimePauseReason.EncounterTriggered, paused.PauseReason);
        Assert.Null(paused.Expedition.Traversal.TravelHeading);
        Assert.Null(paused.Expedition.ActiveWatch!.PendingTurnFromHeading);
        Assert.Null(paused.Expedition.PendingTravelBudgetCharge);
        Assert.Equal(session.Expedition.Traversal.Position, paused.Expedition.Traversal.Position);

        var restored = JsonSerializer.Deserialize<CellExpeditionState>(
            JsonSerializer.Serialize(paused.Expedition))!;
        restored.Validate(world);
        var pending = Assert.IsType<PendingEncounterOccurrence>(restored.PendingEncounter);
        var resolved = engine.ResolveCellEncounter(restored, pending.Id, "No time elapsed");
        var resumed = engine.AdvanceCellWatch(
            world, procedure, resolved.Expedition, Plan(northEast), Inputs(2));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(1, resumed.Expedition.CompletedWatches);
        Assert.Null(resumed.Expedition.PendingTravelBudgetCharge);
        Assert.InRange(resumed.Expedition.DistanceTraveled!.Value.Value, 1.9999999, 2.0000001);
        Assert.DoesNotContain(resumed.Expedition.History, e =>
            e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidDoubleBackIsRejectedBeforeImmediateEncounterCanPause()
    {
        var session = Session();
        var world = session.World;
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
        var heading = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(0));
        var backtrack = Plan(heading) with { DeliberateDoubleBack = true };
        var immediate = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter, TimeSpan.Zero,
            null, "Should never enter the watch", Manual);

        // An unentered first cell has no reciprocal entry interface to
        // backtrack through; the request must fail before encounter mutation.
        var error = Assert.Throws<InvalidOperationException>(() =>
            new CrawlRuntimeEngine().AdvanceCellWatch(
                world, procedure, session.Expedition, backtrack,
                Inputs(1) with { Encounter = immediate }));
        Assert.Contains("Double-back requires a known entry interface", error.Message);
        Assert.Null(session.Expedition.ActiveWatch);
        Assert.Null(session.Expedition.PendingEncounter);
    }

    [Fact]
    public void GeometricExitIsNotGovernedByTheHistoricalFarExitFactor()
    {
        var session = Session();
        var world = session.World;
        var legacy = GenericProcedureRuntime.Bind(
            CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure);
        var geometric = GenericProcedureRuntime.Bind(
            GeometricNativeProcedureMaterializer.Materialize("alexandrian-advanced").Procedure);
        Assert.Equal(1, legacy.Movement.MechanicVersion);
        Assert.Equal(2, geometric.Movement.MechanicVersion);
        Assert.Equal(1d, legacy.HexProgress.FarExitProgressFactor);
        Assert.Equal(1d / 6d, geometric.TurnPolicy!.NeighborCenterDistanceFactor, 12);

        var east = SpatialCourseSelection.FromLegacyHexDirection(
            world, new HexCoordinate(0, 0), new HexDirection(0));
        var fromCenter = session.Expedition.Traversal with { TravelHeading = east };
        var first = PeriodicCellTraversalGeometry.NextCrossing(world, fromCenter);
        Assert.Equal(CellCrossingStatus.Crosses, first.Status);
        var entered = PeriodicCellTraversalGeometry.Advance(
            world, fromCenter, first.DistanceWorldUnits!.Value).Traversal;
        Assert.NotEqual(fromCenter.CurrentCell, entered.CurrentCell);

        // A 60-degree course change from the actual entry point can reach
        // a polygon boundary sooner than the historical 6-mile far factor.
        var changed = entered with
        {
            TravelHeading = SpatialCourseSelection.FromLegacyHexDirection(
                world, new HexCoordinate(1, 0), new HexDirection(1)),
            SelectedExitInterfaceIndex = null
        };
        var exit = PeriodicCellTraversalGeometry.NextCrossing(world, changed);
        Assert.Equal(CellCrossingStatus.Crosses, exit.Status);
        var distance = new DistanceMeasure(
            exit.DistanceWorldUnits!.Value * world.PhysicalDistancePerWorldUnit!.Value.Value,
            world.PhysicalDistancePerWorldUnit.Value.Unit);
        Assert.InRange(distance.Value, 0.001, 5.999999);
        Assert.True(distance.Value < 6 * legacy.HexProgress.FarExitProgressFactor);
        Assert.NotEqual(changed.CurrentCell,
            PeriodicCellTraversalGeometry.Advance(
                world, changed, exit.DistanceWorldUnits.Value).Traversal.CurrentCell);
    }
}
