using HexCrawl.Domain.Spatial;
using Xunit;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16DelaneyDressTests
{
    private const string Square = "<1:1,1,1:4,4>";
    private const string Checkerboard = "<2:1 2,1 2,2:4 4,4>";
    private const string CheckerboardTranslation =
        "<16:2 7 6 10 12 11 15 16,3 5 9 12 13 14 15 16,4 6 8 11 12 10 16 15:4 4,4 4>";

    [Fact]
    public void DistinguishesStructureCurvatureAndLimits()
    {
        Assert.Equal(DelaneyDressStatus.Euclidean, DelaneyDressTopology.Inspect(Square).Status);
        Assert.Equal(DelaneyDressStatus.Euclidean, DelaneyDressTopology.Inspect(Checkerboard).Status);
        Assert.Equal(DelaneyDressStatus.Euclidean, DelaneyDressTopology.Inspect("<1:1,1,1:3,6>").Status);
        Assert.Equal(DelaneyDressStatus.Euclidean, DelaneyDressTopology.Inspect("<1:1,1,1:6,3>").Status);
        Assert.Equal(DelaneyDressStatus.NonEuclidean, DelaneyDressTopology.Inspect("<1:1,1,1:4,5>").Status);
        Assert.Equal(DelaneyDressStatus.StructureInvalid,
            DelaneyDressTopology.Inspect("<3:2 3,1 2 3,1 3:4 4,4 4>").Status);
        Assert.Equal(DelaneyDressStatus.SyntaxInvalid, DelaneyDressTopology.Inspect("broken").Status);
        Assert.Equal(DelaneyDressStatus.LimitExceeded, DelaneyDressTopology.Inspect(Checkerboard, 1).Status);
        Assert.Equal(DelaneyDressStatus.LimitExceeded, DelaneyDressTopology.Inspect(new string('x', 131073)).Status);
        Assert.Equal(Square, DelaneyDressTopology.Inspect("<1.1:1:1,1,1:4,4>").Symbol!.Canonical);
    }

    [Fact]
    public void ComparesLocallyWithoutConflatingUnknownAndFalse()
    {
        Assert.Equal(TilingComparisonStatus.ExactIdentity, DelaneyDressTopology.Compare(Square, Square).Status);
        Assert.Equal(TilingComparisonStatus.Inconclusive, DelaneyDressTopology.Compare(Square, Checkerboard).Status);
        Assert.Equal(TilingComparisonStatus.ProvenEquivalent,
            DelaneyDressTopology.Compare(Square, Checkerboard, CheckerboardTranslation).Status);
        Assert.Equal(TilingComparisonStatus.StructurallyIncompatible,
            DelaneyDressTopology.Compare(Square, "<1:1,1,1:3,6>").Status);
        Assert.Equal(TilingComparisonStatus.MetricallyIncompatible,
            DelaneyDressTopology.Compare(Square, Square,
                leftMetric: [new("edge", "mile", 1, 1)],
                rightMetric: [new("edge", "mile", 2, 2)]).Status);
        Assert.NotNull(DelaneyDressTopology.ProjectChambers(
            DelaneyDressTopology.Inspect(CheckerboardTranslation).Symbol!,
            DelaneyDressTopology.Inspect(Checkerboard).Symbol!));
    }

    [Fact]
    public void PeriodicAddressesAreStableAndOverflowIsRejected()
    {
        var pattern = new PeriodicTopologyWitness(1, Square,
            "<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>",
            [new PeriodicMotifCell("a", [
                new(0, 0, "a", new(1, 0), 2),
                new(1, 1, "a", new(0, 1), 3),
                new(2, 2, "a", new(-1, 0), 0),
                new(3, 3, "a", new(0, -1), 1)
            ])], "Synthetic conformance fixture");
        pattern.ValidateAdjacency();
        var cells = pattern.Enumerate(-2, 2, -2, 2);
        Assert.Equal(25, cells.Count);
        Assert.Equal(25, cells.Select(c => c.ToString()).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => pattern.Enumerate(-100, 100, -100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => pattern.Enumerate(0, 99999, 0, 99999, long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => pattern.Enumerate(long.MinValue, long.MaxValue, 0, 0));
        Assert.Throws<OverflowException>(() => pattern.Enumerate(
            -PeriodicTopologyContractVersion.MaxWireTranslation, PeriodicTopologyContractVersion.MaxWireTranslation,
            -PeriodicTopologyContractVersion.MaxWireTranslation, PeriodicTopologyContractVersion.MaxWireTranslation));
    }
}
