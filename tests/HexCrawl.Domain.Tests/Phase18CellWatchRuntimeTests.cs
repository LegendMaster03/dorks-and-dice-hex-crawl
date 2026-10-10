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
        var resumed = engine.AdvanceCellWatch(world, procedure,
            decided.Expedition, decided.Expedition.ActiveWatch!.Plan, Inputs(1));
        Assert.Null(resumed.PauseReason);
        Assert.Equal(boundary.To, resumed.Expedition.Traversal.CurrentCell);
        Assert.Equal(1, resumed.Expedition.CompletedWatches);
        Assert.Single(resumed.Expedition.History,
            e => e.Kind == CrawlRuntimeEventKind.CellEntered);
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

    private static CellWatchAdvanceInputs Inputs(int steps) => new(
        ResolvedTravelAmount.CellTransitions(steps, Manual),
        new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, Manual),
        ResolvedEncounter.None);

    private static (PeriodicWorldTiling World, CampaignProcedure Procedure, CellExpeditionState State,
        CellWatchTravelPlan Plan) Setup(string symbol, bool calibrated, bool advanced = false)
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
                parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
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
