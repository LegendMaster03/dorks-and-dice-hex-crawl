using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class Phase20GeometricNativeProcedureTests
{
    [Theory]
    [InlineData("alexandrian-advanced", TravelResolutionMode.ContinuousDistance, true)]
    [InlineData("simple-fixed-distance", TravelResolutionMode.ContinuousDistance, false)]
    [InlineData("simple-hex-step", TravelResolutionMode.CellSteps, false)]
    public void GeometricNativeMaterializationKeepsExistingPresetSemantics(
        string key, TravelResolutionMode mode, bool hasTurnCost)
    {
        var published = CrawlProcedureCatalog.Resolve(key).MaterializeGeneric();
        var geometric = CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve(key));
        var publishedRuntime = GenericProcedureRuntime.Bind(published.Procedure);
        var runtime = GenericProcedureRuntime.Bind(geometric.Procedure);

        Assert.Equal(mode, runtime.Movement.TravelResolution);
        Assert.Equal(hasTurnCost, runtime.TurnPolicy!.NeighborCenterDistanceFactor > 0);
        Assert.Equal(publishedRuntime.Time.IntervalDuration, runtime.Time.IntervalDuration);
        Assert.Equal(publishedRuntime.Encounters.Cadence, runtime.Encounters.Cadence);
        Assert.Equal(publishedRuntime.Navigation, runtime.Navigation);
        Assert.Equal(published.Procedure.TilingDsSymbol, geometric.Procedure.TilingDsSymbol);
        Assert.Equal(published.Procedure.Name, geometric.Procedure.Name);
        Assert.Equal(published.Procedure.Key, geometric.Procedure.Key);
        Assert.Equal(published.Origin!.PresetKey, geometric.Origin!.PresetKey);
        Assert.Null(geometric.Origin.PresetRevision);
        Assert.NotNull(published.Origin.PresetRevision);
    }

    [Theory]
    [InlineData("bx")]
    [InlineData("ose-classic-fantasy")]
    [InlineData("pathfinder-2e-hexploration")]
    [InlineData("the-one-ring-2e")]
    public void DeclarativePresetsDoNotAcquireInventedGeometricWatches(string key)
    {
        Assert.Throws<NotSupportedException>(() =>
            CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve(key)));
    }

    [Fact]
    public void DiscreteCellStepsRejectDistanceDenominatedTurningCosts()
    {
        var original = CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve("simple-hex-step")).Procedure;
        var modules = original.Modules.Select(module =>
        {
            if (module.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.SpatialTurnCostPolicy)
                return module;
            var parameters = module.Parameters.ToDictionary(x => x.Key, x => x.Value);
            parameters["neighborCenterDistanceFactor"] = "0.16666666666666666";
            return module with { Parameters = parameters };
        }).ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            GenericProcedureRuntime.Bind(original with { Modules = modules }));
    }

    [Fact]
    public void OptInGeometricProcedureUsesRequestedFreshPinnedId()
    {
        var id = Guid.NewGuid();
        var materialized = CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve("simple-hex-step"), id);
        Assert.Equal(id, materialized.Procedure.ProcedureId);
        Assert.Equal(1, materialized.Procedure.Revision);
    }

    [Fact]
    public void AlexandrianTurnChargesEffortWithoutFabricatingPhysicalDistance()
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:6,3>", 1d, "world-unit");
        var id = Guid.NewGuid();
        var tiling = new PeriodicWorldTiling(id, realized.Topology!,
            realized.Realization!, new WorldPoint(0, 0),
            PhysicalDistancePerWorldUnit: new DistanceMeasure(6, DistanceUnit.Miles));
        var session = MaplessCellSessionGeometry.Create(id, tiling);
        var procedure = CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve("alexandrian-advanced")).Procedure;
        var current = session.Expedition.Traversal.CurrentCell;
        var east = HeadingToBoundary(session.World, current, 0);
        var turn = HeadingToBoundary(session.World, current, 5);
        var engine = new CrawlRuntimeEngine();
        var inputs1 = new CellWatchAdvanceInputs(
            ResolvedTravelAmount.Distance(
                new DistanceMeasure(1, DistanceUnit.Miles),
                new DistanceMeasure(1, DistanceUnit.Miles), new ResolutionProvenance(ResolutionSource.ManualRoll)),
            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null,
                new ResolutionProvenance(ResolutionSource.ManualRoll)),
            ResolvedEncounter.None);
        var first = engine.AdvanceCellWatch(session.World, procedure, session.Expedition,
            new CellWatchTravelPlan(east, false, false, TravelModeSelection.Normal, NavigationAidSelection.None),
            inputs1);
        Assert.Equal(1, first.Expedition.CompletedWatches);
        Assert.Equal(1d, first.Expedition.DistanceTraveled!.Value.Value, 7);
        var inputs2 = inputs1 with
        {
            Travel = ResolvedTravelAmount.Distance(
                new DistanceMeasure(2, DistanceUnit.Miles),
                new DistanceMeasure(2, DistanceUnit.Miles),
                new ResolutionProvenance(ResolutionSource.ManualRoll))
        };
        var second = engine.AdvanceCellWatch(session.World, procedure, first.Expedition,
            new CellWatchTravelPlan(turn, false, false, TravelModeSelection.Normal, NavigationAidSelection.None),
            inputs2);
        Assert.Equal(2, second.Expedition.CompletedWatches);
        Assert.Equal(2d, second.Expedition.DistanceTraveled!.Value.Value, 7);
        Assert.Contains(second.Events, e => e.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && e.Message.Contains("turn debited", StringComparison.OrdinalIgnoreCase)
            && e.DistanceValue == 1d);
    }
    private static WorldPoint HeadingToBoundary(PeriodicWorldTiling world, WorldCellId current, int interfaceIndex)
    {
        var boundary = world.Boundaries(current.Address)[interfaceIndex];
        return SpatialCourseSelection.ParallelToNeighborCenters(world, current, boundary.To);
    }
}
