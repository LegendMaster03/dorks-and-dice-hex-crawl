using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class Phase18EnvironmentContextTests
{
    [Fact]
    public void CellScopedEnvironmentAndFeaturesFollowGeneralizedPartyLocation()
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            "<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>",
            1.5, "world-unit");
        Assert.Equal("realized", realized.Status);
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(),
            realized.Topology!, realized.Realization!, new WorldPoint(0, 0));
        tiling.Validate();

        var cellA = tiling.Resolve(new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var cellB = tiling.Resolve(tiling.Boundaries(cellA.Id.Address)[0].To.Address);
        var source = new PointFeature(Guid.NewGuid(), "Spring", "spring", cellA.Center);

        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Mixed motif environment", Tiling = tiling,
            Features = [source],
            EnvironmentAnnotations =
            [
                Annotation(
                    new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.World },
                    "weather", "clear"),
                Annotation(
                    new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.Cell,
                        Cell = cellA.Id }, "terrain", "marsh"),
                Annotation(
                    new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.Cell,
                        Cell = cellB.Id }, "terrain", "rocky"),
                Annotation(
                    new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.SpatialFeature,
                        FeatureId = source.Id }, "route", "fresh-water")
            ]
        };
        world.ValidateEnvironmentAnnotations();

        var first = State(cellA);
        var expedition = Stored(world, first);
        var initial = EnvironmentContextResolver.Resolve(expedition, world);
        Assert.Contains(initial.Facts, f => f.Effective
            && f.Fact.Tag == "marsh" && f.Source.Kind == EnvironmentFactSourceKind.Cell
            && f.Source.Cell == cellA.Id);
        Assert.Contains(initial.Facts, f => f.Effective && f.Fact.Tag == "fresh-water");
        Assert.DoesNotContain(initial.Facts, f => f.Fact.Tag == "rocky");

        var second = EnvironmentContextResolver.Resolve(
            expedition with { Runtime = State(cellB) }, world);
        Assert.Contains(second.Facts, f => f.Effective
            && f.Fact.Tag == "rocky" && f.Source.Cell == cellB.Id);
        Assert.DoesNotContain(second.Facts, f => f.Fact.Tag == "marsh");
        Assert.DoesNotContain(second.Facts, f => f.Fact.Tag == "fresh-water");
    }

    [Fact]
    public void CellEnvironmentAlsoResolvesForLegacyHexWithoutReplacingHexScope()
    {
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(6, DistanceUnit.Miles)
        };
        var cell = LegacyHexTilingCompatibility.ToCellId(new HexId(grid.Id, new HexCoordinate(0, 0)));
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Legacy cell annotations", Grid = grid,
            EnvironmentAnnotations =
            [
                Annotation(
                    new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.Cell,
                        Cell = cell }, "terrain", "coastal"),
                Annotation(
                    new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.Hex,
                        Hex = new HexCoordinate(0, 0) }, "weather", "windy")
            ]
        };
        var state = new ExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
            DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
        };
        var values = EnvironmentContextResolver.Resolve(Stored(world, state), world);
        Assert.Contains(values.Facts, f => f.Effective && f.Fact.Tag == "coastal");
        Assert.Contains(values.Facts, f => f.Effective && f.Fact.Tag == "windy");
    }

    private static CellExpeditionState State(WorldCellGeometry cell) => new()
    {
        Id = Guid.NewGuid(),
        Traversal = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id,
            Position = cell.Center,
            TravelHeading = new WorldPoint(1, 0)
        },
        DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
    };

    private static StoredExpedition Stored(OverworldDefinition world, CrawlSessionRuntimeState state)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredExpedition(
            "Environment test", state, new WorldBoundCrawlSessionContext(world.Id),
            null, SyntheticProcedureFixtures.MixedProcedure(), null,
            TimeSpan.Zero, "owner", 1, now, now);
    }

    private static EnvironmentAnnotation Annotation(
        EnvironmentAnnotationScope scope, string dimension, string tag) => new()
    {
        Id = Guid.NewGuid(),
        Scope = scope,
        Facts = [new EnvironmentFact
        {
            Id = Guid.NewGuid(),
            Dimension = dimension, ValueKind = EnvironmentValueKind.Tag, Tag = tag
        }]
    };
}
