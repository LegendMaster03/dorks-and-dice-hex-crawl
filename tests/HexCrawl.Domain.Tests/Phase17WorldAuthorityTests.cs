using System;
using System.Linq;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class Phase17WorldAuthorityTests
{
    [Theory]
    [InlineData(HexOrientation.PointyTop, 0)]
    [InlineData(HexOrientation.PointyTop, 37)]
    [InlineData(HexOrientation.FlatTop, -19)]
    [InlineData(HexOrientation.FlatTop, 145)]
    public void LegacyAxialMappingPreservesOriginalHexWorldGeometry(
        HexOrientation orientation, double rotation)
    {
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            Orientation = orientation,
            Origin = new WorldPoint(13.5, -72.25),
            RotationDegrees = rotation,
            HexRadiusWorldUnits = 2.7,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var tiling = LegacyHexTilingCompatibility.Create(grid);
        Assert.Equal(grid.Id, tiling.Id);
        HexCoordinate[] coordinates =
        [
            new(0, 0), new(1, 0), new(0, -1), new(-1, 1),
            new(123456, -77777), new(-123456, 33333), new(2000000, -2000000)
        ];
        foreach (var coordinate in coordinates)
        {
            var address = LegacyHexTilingCompatibility.ToAddress(coordinate);
            Assert.Equal(coordinate, LegacyHexTilingCompatibility.ToHex(address));
            Assert.Equal(new HexId(grid.Id, coordinate),
                LegacyHexTilingCompatibility.ToHexId(
                    LegacyHexTilingCompatibility.ToCellId(new HexId(grid.Id, coordinate))));
            var original = HexGeometry.Corners(grid, coordinate);
            var resolved = tiling.Resolve(address);
            Assert.Equal(6, resolved.Polygon.Count);
            for (int corner = 0; corner < 6; corner++)
            {
                Assert.InRange(original[corner].DistanceTo(resolved.Polygon[corner]), 0, 1e-6);
            }
            Assert.InRange(HexGeometry.HexToWorld(grid, coordinate).DistanceTo(resolved.Center), 0, 1e-5);
            var boundaries = tiling.Boundaries(address);
            Assert.Equal(6, boundaries.Count);
            foreach (var edge in boundaries)
            {
                Assert.Equal(edge.From, tiling.Reciprocal(edge).To);
                Assert.Contains(coordinate.Neighbors(),
                    neighbor => LegacyHexTilingCompatibility.ToHex(edge.To.Address) == neighbor);
            }
        }
    }

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
    public void GeneralizedWorldKeepsIndependentSemanticsAndRejectsHexRuntime()
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
        Assert.Throws<NotSupportedException>(() => _ = world.Grid);
    }
}
