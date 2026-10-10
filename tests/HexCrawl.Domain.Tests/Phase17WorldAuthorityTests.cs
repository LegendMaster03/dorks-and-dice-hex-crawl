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

    [Theory]
    [InlineData(HexOrientation.PointyTop, 0)]
    [InlineData(HexOrientation.FlatTop, 37)]
    public void LegacyPhysicalCalibrationRetainsWorldCoordinatesAndNeighborDistance(
        HexOrientation orientation, double rotation)
    {
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), Orientation = orientation,
            Origin = new WorldPoint(17, -23), RotationDegrees = rotation,
            HexRadiusWorldUnits = 2.7,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Custom("leagues", 4000))
        };
        var tiling = LegacyHexTilingCompatibility.Create(grid);
        Assert.Equal("world-unit", tiling.Realization.Units);
        Assert.Equal(grid.NeighborCenterDistance.Unit,
            tiling.PhysicalDistancePerWorldUnit!.Value.Unit);
        var start = tiling.Resolve(LegacyHexTilingCompatibility.ToAddress(new(0, 0))).Center;
        var next = tiling.Resolve(LegacyHexTilingCompatibility.ToAddress(new(1, 0))).Center;
        Assert.InRange(start.DistanceTo(next), 4.67653717, 4.67653719);
        var physical = tiling.MeasurePhysicalDistance(start, next);
        Assert.Equal(grid.NeighborCenterDistance.Unit, physical.Unit);
        Assert.InRange(Math.Abs(physical.Value - 12), 0, 1e-9);
        Assert.Equal(grid.Origin, tiling.Origin);
        Assert.True(LegacyHexTilingCompatibility.Matches(tiling, grid));
    }

    [Fact]
    public void LargeWorldOriginDoesNotHideAuthorityMismatch()
    {
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), Origin = new WorldPoint(1e9, 1e9),
            HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var original = LegacyHexTilingCompatibility.Create(grid);
        var shifted = original with { Origin = new WorldPoint(original.Origin.X + 0.5, original.Origin.Y) };
        shifted.Validate();
        Assert.False(LegacyHexTilingCompatibility.Matches(shifted, grid));
        Assert.Throws<InvalidOperationException>(() => new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Mismatched", Grid = grid, Tiling = shifted
        }.ValidateSpatialAuthority());
        var localShift = original with
        {
            Realization = original.Realization with
            {
                Polygons = original.Realization.Polygons.ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<TilingWorldPoint>)x.Value
                        .Select(p => new TilingWorldPoint(p.X + 0.5, p.Y)).ToArray())
            }
        };
        localShift.Validate();
        Assert.False(LegacyHexTilingCompatibility.Matches(localShift, grid));
        static TilingWorldPoint QuarterTurn(TilingWorldPoint p) => new(-p.Y, p.X);
        var rotatedBasis = original with
        {
            Realization = original.Realization with
            {
                TranslationU = QuarterTurn(original.Realization.TranslationU),
                TranslationV = QuarterTurn(original.Realization.TranslationV),
                Polygons = original.Realization.Polygons.ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<TilingWorldPoint>)x.Value.Select(QuarterTurn).ToArray())
            }
        };
        rotatedBasis.Validate();
        Assert.False(LegacyHexTilingCompatibility.Matches(rotatedBasis, grid));
        var scaledBasis = original with
        {
            Realization = original.Realization with
            {
                TranslationU = new TilingWorldPoint(original.Realization.TranslationU.X * 1.25,
                    original.Realization.TranslationU.Y * 1.25),
                TranslationV = new TilingWorldPoint(original.Realization.TranslationV.X * 1.25,
                    original.Realization.TranslationV.Y * 1.25),
                Polygons = original.Realization.Polygons.ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<TilingWorldPoint>)x.Value.Select(p =>
                        new TilingWorldPoint(p.X * 1.25, p.Y * 1.25)).ToArray())
            }
        };
        scaledBasis.Validate();
        Assert.False(LegacyHexTilingCompatibility.Matches(scaledBasis, grid));
        var alteredScale = original with
        {
            PhysicalDistancePerWorldUnit = new DistanceMeasure(7, DistanceUnit.Miles)
        };
        Assert.False(LegacyHexTilingCompatibility.Matches(alteredScale, grid));
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
    [InlineData(HexOrientation.PointyTop, 17)]
    [InlineData(HexOrientation.FlatTop, 37)]
    [InlineData(HexOrientation.PointyTop, -49)]
    public void LegacyLargeOriginRotatedMetricBuildsWithoutCancellation(
        HexOrientation orientation, double rotation)
    {
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), Orientation = orientation,
            Origin = new WorldPoint(1e9, 1e9), RotationDegrees = rotation,
            HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var tiling = LegacyHexTilingCompatibility.Create(grid);
        tiling.Validate();
        Assert.True(LegacyHexTilingCompatibility.Matches(tiling, grid));
        foreach (var hex in new[] {
            new HexCoordinate(0, 0), new HexCoordinate(1, -1),
            new HexCoordinate(375, -214), new HexCoordinate(100000, -77777)
        })
        {
            var actual = tiling.Resolve(LegacyHexTilingCompatibility.ToAddress(hex));
            var original = HexGeometry.Corners(grid, hex);
            for (int corner = 0; corner < original.Count; corner++)
                Assert.InRange(original[corner].DistanceTo(actual.Polygon[corner]), 0, 2e-6);
            Assert.InRange(HexGeometry.HexToWorld(grid, hex).DistanceTo(actual.Center), 0, 2e-6);
        }
        var start = tiling.Resolve(LegacyHexTilingCompatibility.ToAddress(new(0, 0))).Center;
        var adjacent = tiling.Resolve(LegacyHexTilingCompatibility.ToAddress(new(1, 0))).Center;
        Assert.InRange(Math.Abs(tiling.MeasurePhysicalDistance(start, adjacent).Value - 12), 0, 2e-6);
    }

    [Theory]
    [InlineData(1000d, -1000d, 2e-6, 17d)]
    [InlineData(1000000d, 1000000d, 1d, 37d)]
    [InlineData(0d, 0d, 2e-6, -23d)]
    public void CandidateEnumerationRetainsEveryDirectlyContainedSharedEdge(
        double originX, double originY, double scale, double degrees)
    {
        var generation = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:3,6>", scale, "unit");
        Assert.Equal("realized", generation.Status);
        var angle = degrees * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        TilingWorldPoint Rotate(TilingWorldPoint value) => new(
            cosine * value.X - sine * value.Y,
            sine * value.X + cosine * value.Y);
        var metric = generation.Realization!;
        var rotated = metric with
        {
            TranslationU = Rotate(metric.TranslationU),
            TranslationV = Rotate(metric.TranslationV),
            Polygons = metric.Polygons.ToDictionary(
                item => item.Key,
                item => (IReadOnlyList<TilingWorldPoint>)item.Value.Select(Rotate).ToArray())
        };
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), generation.Topology!, rotated, new WorldPoint(originX, originY));
        tiling.Validate();
        int provenSharedEdges = 0;
        foreach (var motif in tiling.Topology.MotifCells)
            foreach (var translation in new[] {
                new LatticeDisplacement(0, 0), new LatticeDisplacement(-3, 7),
                new LatticeDisplacement(5, -9)
            })
            {
                var address = new PeriodicCellAddress(motif.Id, translation);
                foreach (var boundary in tiling.Boundaries(address))
                {
                    var middle = new WorldPoint(
                        (boundary.Start.X + boundary.End.X) / 2,
                        (boundary.Start.Y + boundary.End.Y) / 2);
                    // Test the exact property R2 violated: the final polygon
                    // predicate agrees that both polygons contain this point,
                    // but the inverse-lattice prefilter excluded one of them.
                    var fromPolygon = tiling.Resolve(boundary.From.Address).Polygon;
                    var toPolygon = tiling.Resolve(boundary.To.Address).Polygon;
                    if (!FeatureIntersection.PointInPolygon(middle, fromPolygon)
                        || !FeatureIntersection.PointInPolygon(middle, toPolygon))
                        continue;
                    provenSharedEdges++;
                    var lookup = tiling.Containing(middle);
                    Assert.Equal(WorldCellLookupStatus.Ambiguous, lookup.Status);
                    Assert.Contains(boundary.From, lookup.Candidates);
                    Assert.Contains(boundary.To, lookup.Candidates);
                }
            }
        Assert.True(provenSharedEdges >= 6, "Fixture did not exercise enough common boundaries.");
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
        Assert.Throws<NotSupportedException>(() =>
            world.FeaturesIntersecting(new HexCoordinate(0, 0)));
    }
}
