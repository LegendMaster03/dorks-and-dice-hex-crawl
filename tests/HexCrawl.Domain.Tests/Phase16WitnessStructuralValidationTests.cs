using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16WitnessStructuralValidationTests
{
    private const string Square = "<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>";
    private const string Mixed = "<24:2 7 6 10 14 16 18 20 17 19 24 23,3 5 9 12 13 15 17 19 21 22 24 23,4 6 8 11 14 15 18 21 22 19 23 24:4 4 4,3 6 3>";

    [Theory]
    [InlineData(Square)]
    [InlineData(Mixed)]
    public void DerivedCoverMatchesItsDeclaredChamberIncidenceAndClosesVertexOrbits(string symbol)
    {
        var cover = DelaneyDressTorusCover.Construct(symbol);
        Assert.Equal(DelaneyDressCoverStatus.Constructed, cover.Status);
        Assert.NotNull(cover.Witness);
        cover.Witness!.ValidateAdjacency();
    }

    [Fact]
    public void RejectsReciprocalButDisconnectedSublatticeWithTheSameDSymbol()
    {
        var witness = DelaneyDressTorusCover.Construct(Square).Witness!;
        var cell = witness.MotifCells.Single();
        var doubled = witness with
        {
            MotifCells = [cell with
            {
                Boundary = cell.Boundary.Select(edge => edge with
                {
                    TargetTranslation = new LatticeDisplacement(
                        2 * edge.TargetTranslation.U, 2 * edge.TargetTranslation.V)
                }).ToArray()
            }]
        };
        Assert.Throws<InvalidOperationException>(() => doubled.ValidateAdjacency());
    }

    [Fact]
    public void RejectsForgedIdentityEvenIfPeriodicInterfacesAreReciprocal()
    {
        var original = DelaneyDressTorusCover.Construct(Square).Witness!;
        var claimed = original with { QuotientDsSymbol = Mixed, TranslationDsSymbol = Mixed };
        Assert.Throws<InvalidOperationException>(() => claimed.ValidateAdjacency());
    }

    [Fact]
    public void RejectsReciprocalEditsThatBreakVertexClosure()
    {
        var witness = DelaneyDressTorusCover.Construct(Square).Witness!;
        var cell = witness.MotifCells.Single();
        var altered = cell.Boundary.ToArray();
        int peer = altered[0].ReciprocalInterfaceIndex;
        altered[0] = altered[0] with { TargetTranslation = default };
        altered[peer] = altered[peer] with { TargetTranslation = default };
        var changed = witness with { MotifCells = [cell with { Boundary = altered }] };
        Assert.Throws<InvalidOperationException>(() => changed.ValidateAdjacency());
    }
}
