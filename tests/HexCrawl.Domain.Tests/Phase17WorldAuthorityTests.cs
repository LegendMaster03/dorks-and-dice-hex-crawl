using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class Phase17WorldAuthorityTests
{
    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    [InlineData("<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>")]
    [InlineData("<14:2 5 7 9 11 13 14,1 4 6 8 10 12 14 13,3 5 4 6 7 8 9 14 11 13:6 4,3 4 4 3>")]
    public void GeneralizedQueriesUseSuppliedTopologyWithoutCatalogDispatch(string symbol)
    {
        var result = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1.5, "unit");
        Assert.Equal("realized", result.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), result.Topology!, result.Realization!, new WorldPoint(12, -7));
        tiling.Validate();
        var first = tiling.Topology.MotifCells[0];
        var address = new PeriodicCellAddress(first.Id, new LatticeDisplacement(-8, 11));
        var cell = tiling.Resolve(address);
        Assert.True(cell.Polygon.Count >= 3);
        Assert.Equal(new WorldCellId(tiling.Id, address), cell.Id);
        Assert.Equal(first.Boundary.Count, tiling.Boundaries(address).Count);
        foreach (var boundary in tiling.Boundaries(address))
        {
            var reciprocal = tiling.Reciprocal(boundary);
            Assert.Equal(boundary.From, reciprocal.To);
            Assert.Equal(boundary.To, reciprocal.From);
            Assert.InRange(boundary.Start.DistanceTo(reciprocal.End), 0, 1e-7);
            Assert.InRange(boundary.End.DistanceTo(reciprocal.Start), 0, 1e-7);
        }
        var minX = cell.Polygon.Min(p => p.X) - .01;
        var maxX = cell.Polygon.Max(p => p.X) + .01;
        var minY = cell.Polygon.Min(p => p.Y) - .01;
        var maxY = cell.Polygon.Max(p => p.Y) + .01;
        Assert.Contains(tiling.Intersecting(new WorldBounds(minX, minY, maxX, maxY)),
            result => result.Id == cell.Id);
        Assert.Contains(tiling.Nearby(cell.Center, 10), c => c.Id == cell.Id);
        Assert.Equal(cell.Polygon.Count, tiling.Resolve(address).Polygon.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tiling.Intersecting(new WorldBounds(-100000, -100000, 100000, 100000), 100));
    }

    [Theory]
    [InlineData("<1:1,1,1:3,6>", 2e-6, false)]
    [InlineData("<1:1,1,1:3,6>", 2e-6, true)]
    [InlineData("<1:1,1,1:3,6>", 1, false)]
    [InlineData("<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>", 2e-6, false)]
    public void SmallAndOrdinaryCellCentersAreUniqueButSharedEdgesAreAmbiguous(
        string symbol, double scale, bool rotate)
    {
        var generated = DelaneyDressHarmonicMetricRealization.Construct(symbol, scale, "world-unit");
        Assert.Equal("realized", generated.Status);
        var metric = generated.Realization!;
        if (rotate)
        {
            static TilingWorldPoint QuarterTurn(TilingWorldPoint p) => new(-p.Y, p.X);
            metric = metric with
            {
                TranslationU = QuarterTurn(metric.TranslationU),
                TranslationV = QuarterTurn(metric.TranslationV),
                Polygons = metric.Polygons.ToDictionary(x => x.Key,
                    x => (IReadOnlyList<TilingWorldPoint>)x.Value.Select(QuarterTurn).ToArray())
            };
        }
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(), generated.Topology!,
            metric, new WorldPoint(12, -7));
        tiling.Validate();
        foreach (var cell in tiling.Topology.MotifCells)
            foreach (var translation in new[] { new LatticeDisplacement(0, 0), new LatticeDisplacement(-7, 11) })
            {
                var address = new PeriodicCellAddress(cell.Id, translation);
                var geometry = tiling.Resolve(address);
                var lookup = tiling.Containing(geometry.Center);
                Assert.Equal(WorldCellLookupStatus.Unique, lookup.Status);
                Assert.Equal(geometry.Id, Assert.Single(lookup.Candidates));
                var edge = tiling.Boundaries(address)[0];
                var midpoint = new WorldPoint(
                    (edge.Start.X + edge.End.X) / 2, (edge.Start.Y + edge.End.Y) / 2);
                var edgeLookup = tiling.Containing(midpoint);
                Assert.Equal(WorldCellLookupStatus.Ambiguous, edgeLookup.Status);
                Assert.Contains(edge.From, edgeLookup.Candidates);
                Assert.Contains(edge.To, edgeLookup.Candidates);
            }
    }

    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    public void OptionalPlayableBoundsWorkForEveryTiling(string symbol)
    {
        var built = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1, "unit");
        Assert.Equal("realized", built.Status);
        var baseTiling = new PeriodicWorldTiling(Guid.NewGuid(),
            built.Topology!, built.Realization!, new WorldPoint(0, 0));
        var address = new PeriodicCellAddress(baseTiling.Topology.MotifCells[0].Id, new(0, 0));
        var inside = baseTiling.Resolve(address).Center;
        Assert.True(baseTiling.IsPlayable(address));

        var bounded = baseTiling with
        {
            PlayableBounds = new PeriodicMapBounds(5, 5)
        };
        bounded.Validate();
        Assert.True(bounded.IsPlayable(address));
        Assert.True(bounded.IsPlayable(new PeriodicCellAddress(address.MotifCellId,
            new LatticeDisplacement(4, 4))));
        Assert.False(bounded.IsPlayable(new PeriodicCellAddress(address.MotifCellId,
            new LatticeDisplacement(-1, 0))));
        Assert.False(bounded.IsPlayable(new PeriodicCellAddress(address.MotifCellId,
            new LatticeDisplacement(100, 100))));
        Assert.Throws<InvalidOperationException>(() =>
            (bounded with { PlayableBounds = new PeriodicMapBounds(0, 1) }).Validate());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(9)]
    public void SquareMapSizeUsesExactPeriodicCellCounts(int size)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:4,4>", 1, "unit");
        Assert.Equal("realized", realized.Status);
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(),
            realized.Topology!, realized.Realization!, new WorldPoint(0, 0))
        {
            PlayableBounds = new PeriodicMapBounds(size, size)
        };
        tiling.Validate();
        Assert.Single(tiling.Topology.MotifCells);
        var cellName = tiling.Topology.MotifCells[0].Id;
        var count = 0;
        for (var u = -1; u <= size; u++)
            for (var v = -1; v <= size; v++)
            {
                var address = new PeriodicCellAddress(cellName, new LatticeDisplacement(u, v));
                if (tiling.IsPlayable(address)) count++;
            }
        Assert.Equal(size * size, count);
        Assert.NotNull(tiling.PlayableEnvelope());
    }

    [Fact]
    public void OptionalBoundsIntersectAndNonoverlapFailsClosed()
    {
        var left = new PeriodicMapBounds(9, 9);
        var right = new PeriodicMapBounds(5, 5, 2, 3);
        Assert.Equal(new PeriodicMapBounds(5, 5, 2, 3),
            PeriodicMapBounds.Intersection(left, right));
        Assert.Equal(left, PeriodicMapBounds.Intersection(null, left));
        Assert.Null(PeriodicMapBounds.Intersection(null, null));
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMapBounds.Intersection(left, new PeriodicMapBounds(2, 2, 12, 12)));
    }

    [Theory]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    [InlineData("<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>")]
    public void GenericMovementNeighborEligibilityRespectsBoundsOnEveryTiling(string symbol)
    {
        var result = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1, "unit");
        Assert.Equal("realized", result.Status);
        var unbounded = new PeriodicWorldTiling(Guid.NewGuid(),
            result.Topology!, result.Realization!, new WorldPoint(0, 0));
        var address = new PeriodicCellAddress(
            unbounded.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0));
        var original = unbounded.MovementNeighbors(address, CellAdjacencyMode.EdgeAndVertex);
        Assert.NotEmpty(original);

        var bounded = unbounded with { PlayableBounds = new PeriodicMapBounds(1, 1) };
        bounded.Validate();
        foreach (var mode in Enum.GetValues<CellAdjacencyMode>())
            Assert.All(bounded.MovementNeighbors(address, mode),
                neighbor => Assert.True(bounded.IsPlayable(neighbor.Address)));

        var forbidden = original.First(neighbor => !bounded.IsPlayable(neighbor.Address));
        Assert.DoesNotContain(forbidden,
            bounded.MovementNeighbors(address, CellAdjacencyMode.EdgeAndVertex));
        Assert.Throws<InvalidOperationException>(() =>
            SpatialCourseSelection.ParallelToNeighborCenters(
                bounded, new WorldCellId(bounded.Id, address), forbidden,
                CellAdjacencyMode.EdgeAndVertex));
        var exit = bounded.Boundaries(address)
            .FirstOrDefault(boundary => !bounded.IsPlayable(boundary.To.Address));
        Assert.NotNull(exit);
        var cursor = new PeriodicCellTraversal
        {
            CurrentCell = new WorldCellId(bounded.Id, address),
            Position = bounded.Resolve(address).Center
        };
        Assert.Throws<InvalidOperationException>(() =>
            SpatialCourseSelection.TowardAtomicInterface(
                bounded, cursor, exit.InterfaceIndex));
    }

    [Fact]
    public void WorldAuthorityRejectsFabricatedTopologyAndMetric()
    {
        var result = DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:4,4>", 1, "unit");
        Assert.Equal("realized", result.Status);
        var topology = result.Topology!;
        var metric = result.Realization!;
        var first = topology.MotifCells[0];
        var mutated = topology with
        {
            MotifCells = topology.MotifCells.Select((cell, index) =>
                index == 0 ? cell with
                {
                    Boundary = cell.Boundary.Select((side, sideIndex) =>
                        sideIndex == 0 ? side with { ReciprocalInterfaceIndex = -1 } : side).ToArray()
                } : cell).ToArray()
        };
        Assert.ThrowsAny<Exception>(() =>
            new PeriodicWorldTiling(Guid.NewGuid(), mutated, metric, new WorldPoint(0, 0)).Validate());
        Assert.ThrowsAny<Exception>(() => new PeriodicWorldTiling(
            Guid.NewGuid(), topology, metric with
            {
                TranslationV = metric.TranslationU
            }, new WorldPoint(0, 0)).Validate());
    }

    [Fact]
    public void GeneralizedWorldKeepsIndependentSemantics()
    {
        var result = DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:4,4>", 1, "unit");
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(), result.Topology!,
            result.Realization!, new WorldPoint(4, 2));
        var address = new PeriodicCellAddress(tiling.Topology.MotifCells[0].Id, new(0, 0));
        var center = tiling.Resolve(address).Center;
        var feature = new PointFeature(Guid.NewGuid(), "Spring", "water", center);
        var fact = new EnvironmentFact
        {
            Id = Guid.NewGuid(), Dimension = "terrain", ValueKind = EnvironmentValueKind.Tag, Tag = "wet"
        };
        var annotation = new EnvironmentAnnotation
        {
            Id = Guid.NewGuid(),
            Scope = new EnvironmentAnnotationScope
            {
                Kind = EnvironmentAnnotationScopeKind.Cell,
                Cell = new WorldCellId(tiling.Id, address)
            },
            Facts = [fact]
        };
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Unfamiliar motif", Tiling = tiling,
            Features = [feature], EnvironmentAnnotations = [annotation]
        };
        world.ValidateEnvironmentAnnotations();
        Assert.Equal(feature.Id, Assert.Single(world.FeaturesIntersecting(address)).Id);
        Assert.Contains(world.AnnotationsForCell(address), a => a.Id == annotation.Id);
    }
}
