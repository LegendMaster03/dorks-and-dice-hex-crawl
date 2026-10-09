using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16HarmonicMetricRealizationTests
{
    private static readonly (string Name, string Symbol)[] Examples =
    [
        ("square", "<1:1,1,1:4,4>"),
        ("triangle", "<1:1,1,1:3,6>"),
        ("hexagon", "<1:1,1,1:6,3>"),
        ("two-chamber", "<2:1 2,1 2,2:4 4,4>"),
        ("checkerboard", "<16:2 7 6 10 12 11 15 16,3 5 9 12 13 14 15 16,4 6 8 11 12 10 16 15:4 4,4 4>"),
        ("mixed-square-triangle", "<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>"),
        ("mixed-quotient", "<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>"),
        ("nonedge-mixed-quotient", "<14:2 5 7 9 11 13 14,1 4 6 8 10 12 14 13,3 5 4 6 7 8 9 14 11 13:6 4,3 4 4 3>")
    ];

    [Fact]
    public void ReconstructsVerifiedPolygonCoordinatesFromGeneralEuclideanSymbolWithoutAnImage()
    {
        foreach (var (name, source) in Examples)
        {
            var result = DelaneyDressHarmonicMetricRealization.Construct(source, 1, "abstract");
            Assert.True(result.Status == "realized", $"{name}: {result.Status}: {result.Reason}");
            var topology = Assert.IsType<PeriodicTopologyWitness>(result.Topology);
            var metric = Assert.IsType<PeriodicMetricRealization>(result.Realization);
            Assert.True(PeriodicMetricWitnessValidator.Validate(topology, metric));
            Assert.Equal(DelaneyDressTopology.Inspect(source).Symbol!.Canonical, topology.QuotientDsSymbol);
            Assert.Equal(DelaneyDressCoverStatus.Constructed,
                DelaneyDressTorusCover.Construct(topology.TranslationDsSymbol).Status);
            Assert.Equal(topology.MotifCells.Count, metric.Polygons.Count);
            Assert.Equal(topology.MotifCells.Sum(c => c.Boundary.Count),
                metric.Polygons.Sum(p => p.Value.Count));
            var again = DelaneyDressHarmonicMetricRealization.Construct(source, 1, "abstract");
            Assert.Equal("realized", again.Status);
            Assert.Equal(JsonSerializer.Serialize(topology), JsonSerializer.Serialize(again.Topology));
            Assert.Equal(JsonSerializer.Serialize(metric), JsonSerializer.Serialize(again.Realization));
        }
    }

    [Fact]
    public void GlobalScaleRotationAndExplicitConstraintsAreProvenNotAssumed()
    {
        const string source = "<2:1 2,1 2,2:4 4,4>";
        var baseline = DelaneyDressHarmonicMetricRealization.Construct(source, 1, "world");
        var transformed = DelaneyDressHarmonicMetricRealization.Construct(
            source, 7, "miles", rotationDegrees: 37);
        Assert.Equal("realized", baseline.Status);
        Assert.Equal("realized", transformed.Status);
        Assert.True(PeriodicMetricWitnessValidator.Validate(transformed.Topology!, transformed.Realization!));
        Assert.Equal(baseline.Topology!.TranslationDsSymbol, transformed.Topology!.TranslationDsSymbol);
        Assert.Equal(JsonSerializer.Serialize(baseline.Topology), JsonSerializer.Serialize(transformed.Topology));
        var u = transformed.Realization!.TranslationU;
        Assert.InRange(Math.Sqrt(u.X * u.X + u.Y * u.Y), 7 - 1e-8, 7 + 1e-8);
        Assert.Equal("miles", transformed.Realization.Units);

        var feasible = DelaneyDressHarmonicMetricRealization.Construct(
            source, 2, "km", constraints:
            [
                new TilingMetricConstraint("period-u-length", "km", 2, 2),
                new TilingMetricConstraint("period-v-length", "km", 2, 2),
                new TilingMetricConstraint("period-angle-degrees", "degrees", 90, 90)
            ]);
        Assert.Equal("realized", feasible.Status);
        Assert.True(PeriodicMetricWitnessValidator.Validate(feasible.Topology!, feasible.Realization!));

        var incompatible = DelaneyDressHarmonicMetricRealization.Construct(
            source, 2, "km", constraints:
            [
                new TilingMetricConstraint("period-u-length", "km", 8, 9)
            ]);
        Assert.Equal("unresolved-geometry", incompatible.Status);
        Assert.Null(incompatible.Realization);
        Assert.Null(incompatible.Topology);
    }

    [Fact]
    public void InvalidConstraintsAndOutOfCapacityDoNotFabricateWorldGeometry()
    {
        foreach (var invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, 1e12 })
        {
            var result = DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:4,4>", invalid, "world");
            Assert.NotEqual("realized", result.Status);
            Assert.Null(result.Realization);
        }
        Assert.NotEqual("realized",
            DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:4,4>", 1, "world", chamberLimit: 1).Status);
        Assert.NotEqual("realized",
            DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:3,3>", 1, "world").Status);
        Assert.NotEqual("realized",
            DelaneyDressHarmonicMetricRealization.Construct("<1:1,1,1:4,4>", 1, "world", constraints:
            [
                new TilingMetricConstraint("unimplemented-condition", "world", 0, 10)
            ]).Status);
    }
}
