using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16UniformQuotientUnfoldingTests
{
    private static readonly string[] ReflectionQuotients =
    [
        "<1:1,1,1:3,6>",
        "<1:1,1,1:4,4>",
        "<1:1,1,1:6,3>",
        "<2:1 2,1 2,2:4 4,4>"
    ];

    [Theory]
    [MemberData(nameof(ReflectionCases))]
    public void UnfoldsOneAndTwoChamberSymmetryQuotientsToPrimitiveTori(string source)
    {
        var result = DelaneyDressUniformQuotientUnfolding.Construct(source);
        Assert.True(result.Status == DelaneyDressCoverStatus.Constructed,
            $"{source}: {result.Status}: {result.Reason}");
        var witness = Assert.IsType<PeriodicTopologyWitness>(result.Witness);
        Assert.Equal(DelaneyDressTopology.Inspect(source).Symbol!.Canonical, witness.QuotientDsSymbol);
        witness.ValidateAdjacency();
        var cover = DelaneyDressTopology.Inspect(witness.TranslationDsSymbol, 1024);
        Assert.Equal(DelaneyDressStatus.Euclidean, cover.Status);
        Assert.True(cover.Symbol!.FixedPointFree);
        Assert.True(cover.Symbol.WeaklyOrientable);
        Assert.NotNull(DelaneyDressTopology.ProjectChambers(
            cover.Symbol, DelaneyDressTopology.Inspect(source).Symbol!));
        Assert.All(witness.MotifCells, cell => Assert.All(cell.Boundary, edge =>
        {
            var peer = Assert.Single(witness.MotifCells, c => c.Id == edge.TargetMotifCellId);
            var reciprocal = peer.Boundary[edge.ReciprocalInterfaceIndex];
            Assert.Equal(cell.Id, reciprocal.TargetMotifCellId);
            Assert.Equal(edge.Index, reciprocal.ReciprocalInterfaceIndex);
            Assert.Equal(edge.TargetTranslation.Opposite(), reciprocal.TargetTranslation);
        }));
        var again = DelaneyDressUniformQuotientUnfolding.Construct(source);
        Assert.Equal(JsonSerializer.Serialize(witness), JsonSerializer.Serialize(again.Witness));
    }

    public static TheoryData<string> ReflectionCases()
    {
        var cases = new TheoryData<string>();
        foreach (var source in ReflectionQuotients) cases.Add(source);
        return cases;
    }

    [Theory]
    [InlineData("<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>")]
    [InlineData("<16:2 7 6 10 12 11 15 16,3 5 9 12 13 14 15 16,4 6 8 11 12 10 16 15:4 4,4 4>")]
    public void AlreadyExpandedUniformQuotientsHaveVerifiedFiberProductCovers(string source)
    {
        var result = DelaneyDressUniformQuotientUnfolding.Construct(source);
        Assert.Equal(DelaneyDressCoverStatus.Constructed, result.Status);
        result.Witness!.ValidateAdjacency();
    }

    [Fact]
    public void SharedVersionedUniformQuotientCorpusMatchesSurveyorEnvelope()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "periodic-topology-v1.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var entry in json.RootElement.GetProperty("uniformQuotientCases").EnumerateArray())
        {
            string name = entry.GetProperty("name").GetString()!;
            string symbol = entry.GetProperty("dsSymbol").GetString()!;
            int limit = entry.TryGetProperty("chamberLimit", out var value) ? value.GetInt32() : 768;
            var expected = entry.GetProperty("status").GetString()! switch
            {
                "constructed" => DelaneyDressCoverStatus.Constructed,
                "unsupported" => DelaneyDressCoverStatus.Unsupported,
                "invalid" => DelaneyDressCoverStatus.Invalid,
                _ => throw new InvalidOperationException($"Unrecognized uniform quotient status: {name}")
            };
            var result = DelaneyDressUniformQuotientUnfolding.Construct(symbol, limit);
            Assert.True(result.Status == expected, $"{name}: expected {expected}, got {result.Status}. {result.Reason}");
            if (expected == DelaneyDressCoverStatus.Constructed)
                result.Witness!.ValidateAdjacency();
        }
    }

    [Fact]
    public void MixedDegreeEuclideanAndInvalidSymbolsFailClosed()
    {
        const string mixed = "<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>";
        Assert.Equal(DelaneyDressCoverStatus.Unsupported,
            DelaneyDressUniformQuotientUnfolding.Construct(mixed).Status);
        Assert.Equal(DelaneyDressCoverStatus.Invalid,
            DelaneyDressUniformQuotientUnfolding.Construct("garbage").Status);
        Assert.Equal(DelaneyDressCoverStatus.Invalid,
            DelaneyDressUniformQuotientUnfolding.Construct("<1:1,1,1:3,3>").Status);
        Assert.Equal(DelaneyDressCoverStatus.Unsupported,
            DelaneyDressUniformQuotientUnfolding.Construct(ReflectionQuotients[3], 8).Status);
        Assert.Equal(DelaneyDressCoverStatus.Unsupported,
            DelaneyDressUniformQuotientUnfolding.Construct(ReflectionQuotients[3], 4096).Status);
    }
}
