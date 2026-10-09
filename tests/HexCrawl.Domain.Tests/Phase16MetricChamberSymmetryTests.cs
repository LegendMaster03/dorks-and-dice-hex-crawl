using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16MetricChamberSymmetryTests
{
    private static (PeriodicTopologyWitness Topology, PeriodicMetricRealization Metric)
        SquareMetric(double u, double v, double angle)
    {
        var result = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1, "unit", constraints:
            [
                new TilingMetricConstraint("period-u-length", "unit", u, u),
                new TilingMetricConstraint("period-v-length", "unit", v, v),
                new TilingMetricConstraint("period-angle-degrees", "degrees", angle, angle)
            ]);
        Assert.True(result.Status == "realized", result.Reason);
        Assert.True(PeriodicMetricWitnessValidator.Validate(result.Topology!, result.Realization!));
        return (result.Topology!, result.Realization!);
    }

    [Fact]
    public void SquareMetricSupportsFullEightChamberSymmetry()
    {
        var (topology, metric) = SquareMetric(1, 1, 90);
        var result = DelaneyDressMetricChamberSymmetry.Verify(topology, metric);
        Assert.Equal("verified", result.Status);
        Assert.Equal(8, result.MetricAutomorphisms);
        Assert.Equal(8, result.CombinatorialAutomorphisms);
        Assert.Equal("<1:1,1,1:4,4>", result.MetricQuotientDsSymbol);
        Assert.Equal(1, result.QuotientChambers);
        Assert.Equal("complete-periodic-polygon-witness", result.Evidence);
    }

    [Fact]
    public void MetricChangeCanForbidValidCombinatorialSquareAutomorphisms()
    {
        var (rectTopology, rectangle) = SquareMetric(2, 1, 90);
        var (skewTopology, oblique) = SquareMetric(1, Math.Sqrt(1.16),
            Math.Atan2(1, 0.4) * 180 / Math.PI);
        var rect = DelaneyDressMetricChamberSymmetry.Verify(rectTopology, rectangle);
        var skew = DelaneyDressMetricChamberSymmetry.Verify(skewTopology, oblique);
        Assert.Equal("verified", rect.Status);
        Assert.Equal("verified", skew.Status);
        Assert.Equal(8, rect.CombinatorialAutomorphisms);
        Assert.Equal(4, rect.MetricAutomorphisms);
        Assert.Equal(2, rect.QuotientChambers);
        Assert.Equal(8, skew.CombinatorialAutomorphisms);
        Assert.Equal(2, skew.MetricAutomorphisms);
        Assert.Equal(4, skew.QuotientChambers);
        Assert.NotEqual("<1:1,1,1:4,4>", skew.MetricQuotientDsSymbol);
    }

    [Theory]
    [InlineData("<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>")]
    [InlineData("<14:2 5 7 9 11 13 14,1 4 6 8 10 12 14 13,3 5 4 6 7 8 9 14 11 13:6 4,3 4 4 3>")]
    public void MixedAndNonEdgeMotifsProduceOnlyProvenPolygonIsometryQuotients(string source)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(source, 1, "unit");
        Assert.Equal("realized", realized.Status);
        var result = DelaneyDressMetricChamberSymmetry.Verify(
            realized.Topology!, realized.Realization!);
        Assert.Equal("verified", result.Status);
        Assert.InRange(result.MetricAutomorphisms!.Value, 1, result.CombinatorialAutomorphisms!.Value);
        Assert.Equal(realized.Topology!.TranslationDsSymbol, result.TranslationDsSymbol);
        var original = DelaneyDressTopology.Inspect(result.TranslationDsSymbol, 2048);
        var quotient = DelaneyDressTopology.Inspect(result.MetricQuotientDsSymbol, 2048);
        Assert.Equal(DelaneyDressStatus.Euclidean, quotient.Status);
        Assert.NotNull(DelaneyDressTopology.ProjectChambers(original.Symbol!, quotient.Symbol!));
    }

    [Fact]
    public void InvalidWitnessOrLooseToleranceNeverBecomesGeometricSymmetryEvidence()
    {
        var (topology, metric) = SquareMetric(1, 1, 90);
        Assert.Equal("unsupported",
            DelaneyDressMetricChamberSymmetry.Verify(topology, metric, .2).Status);
        var falseTopology = topology with { TranslationDsSymbol = "<1:1,1,1:4,4>" };
        Assert.Equal("unsupported",
            DelaneyDressMetricChamberSymmetry.Verify(falseTopology, metric).Status);
        var overlapping = metric with
        {
            TranslationV = metric.TranslationU
        };
        Assert.Equal("unsupported",
            DelaneyDressMetricChamberSymmetry.Verify(topology, overlapping).Status);
    }
}
