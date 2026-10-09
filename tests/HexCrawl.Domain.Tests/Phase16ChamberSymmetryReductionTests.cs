using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16ChamberSymmetryReductionTests
{
    [Fact]
    public void OrdinarySquareTorusReducesToOneChamberWithoutNamedPatternSelection()
    {
        const string translation = "<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>";
        var result = DelaneyDressChamberSymmetryReduction.Construct(translation);
        Assert.Equal("reduced", result.Status);
        Assert.Equal("<1:1,1,1:4,4>", result.QuotientDsSymbol);
        Assert.Equal(8, result.SourceChambers);
        Assert.Equal(1, result.QuotientChambers);
        Assert.Equal(8, result.AutomorphismCount);
        Assert.Equal("combinatorial-only", result.Evidence);
        Assert.Equal(result.QuotientDsSymbol,
            DelaneyDressChamberSymmetryReduction.Construct(result.QuotientDsSymbol!).QuotientDsSymbol);
    }

    [Theory]
    [InlineData("<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>")]
    [InlineData("<14:2 5 7 9 11 13 14,1 4 6 8 10 12 14 13,3 5 4 6 7 8 9 14 11 13:6 4,3 4 4 3>")]
    public void IndependentlyRealizedMixedAndJunctionSymmetryRetainsCoverProof(string source)
    {
        var built = DelaneyDressHarmonicMetricRealization.Construct(source, 1, "unit");
        Assert.True(built.Status == "realized", built.Reason);
        var translation = built.Topology!.TranslationDsSymbol;
        var result = DelaneyDressChamberSymmetryReduction.Construct(translation);
        Assert.Equal("reduced", result.Status);
        Assert.Equal(translation, result.SourceDsSymbol);
        Assert.InRange(result.QuotientChambers!.Value, 1, result.SourceChambers!.Value);
        var from = DelaneyDressTopology.Inspect(translation, 2048);
        var quotient = DelaneyDressTopology.Inspect(result.QuotientDsSymbol, 2048);
        Assert.Equal(DelaneyDressStatus.Euclidean, quotient.Status);
        Assert.NotNull(DelaneyDressTopology.ProjectChambers(from.Symbol!, quotient.Symbol!));
        Assert.Equal(result.QuotientDsSymbol,
            DelaneyDressChamberSymmetryReduction.Construct(result.QuotientDsSymbol!).QuotientDsSymbol);
    }

    [Fact]
    public void BoundedInvalidAndNonEuclideanSymbolsCannotClaimDerivedSymmetry()
    {
        Assert.Equal("invalid",
            DelaneyDressChamberSymmetryReduction.Construct("<1:1,1,1:4,5>").Status);
        Assert.Equal("invalid",
            DelaneyDressChamberSymmetryReduction.Construct("not a D-symbol").Status);
        Assert.Equal("unsupported-limit",
            DelaneyDressChamberSymmetryReduction.Construct("<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>", 1).Status);
        Assert.Equal("unsupported-limit",
            DelaneyDressChamberSymmetryReduction.Construct("<1:1,1,1:4,4>", 99999).Status);
    }
}
