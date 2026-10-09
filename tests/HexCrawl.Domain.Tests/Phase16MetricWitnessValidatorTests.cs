using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16MetricWitnessValidatorTests
{
    private static PeriodicTopologyWitness SquareTopology()
    {
        const string squareCover = "<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>";
        var canonical = DelaneyDressTopology.Inspect(squareCover).Symbol!.Canonical;
        var edges = new[]
        {
            new PeriodicEdgeInterface(0, 0, "square", new LatticeDisplacement(0, -1), 2),
            new PeriodicEdgeInterface(1, 1, "square", new LatticeDisplacement(1, 0), 3),
            new PeriodicEdgeInterface(2, 2, "square", new LatticeDisplacement(0, 1), 0),
            new PeriodicEdgeInterface(3, 3, "square", new LatticeDisplacement(-1, 0), 1)
        };
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current,
            "<1:1,1,1:4,4>",
            canonical,
            [new PeriodicMotifCell("square", edges)],
            "test:explicit-square-incidence");
        topology.ValidateAdjacency();
        return topology;
    }

    private static PeriodicMetricRealization Rectangle(
        IReadOnlyList<TilingWorldPoint>? corners = null,
        IReadOnlyList<TilingMetricConstraint>? constraints = null)
    {
        var points = corners ?? [
            new TilingWorldPoint(0, 0),
            new TilingWorldPoint(2, 0),
            new TilingWorldPoint(2, 1),
            new TilingWorldPoint(0, 1)
        ];
        return new PeriodicMetricRealization(
            "km",
            new TilingWorldPoint(2, 0), new TilingWorldPoint(0, 1),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>> { ["square"] = points },
            constraints ?? []);
    }

    [Fact]
    public void IndependentlyAcceptsTranslationalRectangleWithExactReciprocalEdgesAndConstraints()
    {
        var topology = SquareTopology();
        var constraints = new[]
        {
            new TilingMetricConstraint("period-u-length", "km", 2, 2),
            new TilingMetricConstraint("period-v-length", "km", 1, 1),
            new TilingMetricConstraint("period-angle-degrees", "degrees", 90, 90),
            new TilingMetricConstraint("edge-length", "km", 1, 2),
            new TilingMetricConstraint("tile-area", "km", 2, 2),
            new TilingMetricConstraint("cell-area:square", "km", 2, 2)
        };
        Assert.True(PeriodicMetricWitnessValidator.Validate(topology, Rectangle(constraints: constraints)));
        Assert.True(PeriodicMetricWitnessValidator.Validate(topology, Rectangle()));
    }

    [Fact]
    public void RejectsForgedCornerGeometryEvenWhenCombinatorialGraphIsValid()
    {
        var topology = SquareTopology();
        var broken = new[]
        {
            new TilingWorldPoint(0, 0),
            new TilingWorldPoint(2, 0),
            new TilingWorldPoint(2, 1.1),
            new TilingWorldPoint(0, 1)
        };
        var failure = Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, Rectangle(broken)));
        Assert.Contains("area", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsSelfIntersectingAndClockwisePolygonWitnesses()
    {
        var topology = SquareTopology();
        var crossed = new[]
        {
            new TilingWorldPoint(0, 0),
            new TilingWorldPoint(2, 1),
            new TilingWorldPoint(0, 1),
            new TilingWorldPoint(2, 0)
        };
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, Rectangle(crossed)));
        var clockwise = new[]
        {
            new TilingWorldPoint(0, 0),
            new TilingWorldPoint(0, 1),
            new TilingWorldPoint(2, 1),
            new TilingWorldPoint(2, 0)
        };
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, Rectangle(clockwise)));
    }

    [Fact]
    public void UnknownOrUnsatisfiedMetricConstraintsNeverClaimCompatibility()
    {
        var topology = SquareTopology();
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, Rectangle(constraints:
            [
                new TilingMetricConstraint("edge-length", "km", 1.5, 2.5)
            ])));
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, Rectangle(constraints:
            [
                new TilingMetricConstraint("unknown-area-condition", "km", 0, 3)
            ])));
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, Rectangle(constraints:
            [
                new TilingMetricConstraint("period-angle-degrees", "km", 89, 91)
            ])));
    }

    [Fact]
    public void MissingPolygonsAndDegenerateTranslationBasisAreRejected()
    {
        var topology = SquareTopology();
        var missing = Rectangle() with
        {
            Polygons = new Dictionary<string, IReadOnlyList<TilingWorldPoint>>()
        };
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, missing));
        var degenerate = Rectangle() with
        {
            TranslationV = new TilingWorldPoint(4, 0)
        };
        Assert.Throws<InvalidOperationException>(() =>
            PeriodicMetricWitnessValidator.Validate(topology, degenerate));
    }
}
