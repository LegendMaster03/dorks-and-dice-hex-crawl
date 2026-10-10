using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16GeneralQuotientUnfoldingTests
{
    [Fact]
    public void GeneralEuclideanSymbolsProduceProvedPrimitiveTranslationTori()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "periodic-topology-v1.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in json.RootElement.GetProperty("generalQuotientCases").EnumerateArray())
        {
            string name = item.GetProperty("name").GetString()!;
            string symbol = item.GetProperty("dsSymbol").GetString()!;
            int bound = item.TryGetProperty("chamberLimit", out var limit) ? limit.GetInt32() : 1024;
            var expected = item.GetProperty("status").GetString()! switch
            {
                "constructed" => DelaneyDressCoverStatus.Constructed,
                "unsupported" => DelaneyDressCoverStatus.Unsupported,
                "invalid" => DelaneyDressCoverStatus.Invalid,
                _ => throw new InvalidOperationException($"Unexpected status for {name}")
            };
            var result = DelaneyDressGeneralQuotientUnfolding.Construct(symbol, bound);
            Assert.True(result.Status == expected, $"{name}: {result.Status}: {result.Reason}");
            if (expected != DelaneyDressCoverStatus.Constructed) continue;
            var witness = Assert.IsType<PeriodicTopologyWitness>(result.Witness);
            var original = DelaneyDressTopology.Inspect(symbol, 2048);
            var expanded = DelaneyDressTopology.Inspect(witness.TranslationDsSymbol, 2048);
            Assert.Equal(DelaneyDressStatus.Euclidean, original.Status);
            Assert.Equal(DelaneyDressStatus.Euclidean, expanded.Status);
            Assert.True(expanded.Symbol!.FixedPointFree);
            Assert.True(expanded.Symbol.WeaklyOrientable);
            Assert.NotNull(DelaneyDressTopology.ProjectChambers(expanded.Symbol, original.Symbol!));
            Assert.Equal(expanded.Symbol.ChamberCount, result.SourceProjection!.Count - 1);
            Assert.Equal(expanded.Symbol.ChamberCount,
                witness.MotifCells.Sum(cell => 2 * cell.Boundary.Count));
            witness.ValidateAdjacency();
            Assert.True(result.CyclicSheetCount >= 1);
            if (item.TryGetProperty("cyclicSheets", out var sheets))
                Assert.Equal(sheets.GetInt32(), result.CyclicSheetCount);
            var repeated = DelaneyDressGeneralQuotientUnfolding.Construct(symbol, bound);
            Assert.Equal(DelaneyDressCoverStatus.Constructed, repeated.Status);
            Assert.Equal(JsonSerializer.Serialize(witness), JsonSerializer.Serialize(repeated.Witness));
        }
    }

    [Fact]
    public void MixedFaceDegreeQuotientsAreNotRestrictedToRegularReflectionSeeds()
    {
        var mixed = "<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>";
        var result = DelaneyDressGeneralQuotientUnfolding.Construct(mixed);
        Assert.Equal(DelaneyDressCoverStatus.Constructed, result.Status);
        Assert.True(result.Witness!.MotifCells.Select(c => c.Boundary.Count).Distinct().Count() > 1);
        result.Witness.ValidateAdjacency();
    }
}
