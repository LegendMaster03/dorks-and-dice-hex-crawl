using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16TorusCoverTests
{
    [Theory]
    [InlineData("<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>", 1)]
    [InlineData("<24:2 7 6 10 14 16 18 20 17 19 24 23,3 5 9 12 13 15 17 19 21 22 24 23,4 6 8 11 14 15 18 21 22 19 23 24:4 4 4,3 6 3>", 3)]
    public void ReconstructsStableImageIndependentIntegerAddresses(string symbol, int expectedFaces)
    {
        var result = DelaneyDressTorusCover.Construct(symbol);
        Assert.Equal(DelaneyDressCoverStatus.Constructed, result.Status);
        Assert.NotNull(result.Witness);
        Assert.Equal(expectedFaces, result.Witness!.MotifCells.Count);
        result.Witness.ValidateAdjacency();
        Assert.Equal(JsonSerializer.Serialize(result.Witness),
            JsonSerializer.Serialize(DelaneyDressTorusCover.Construct(symbol).Witness));
        var addresses = result.Witness.Enumerate(-3, 3, -3, 3);
        Assert.Equal(49 * expectedFaces, addresses.Count);
        Assert.Equal(addresses.Count, addresses.Distinct().Count());
    }

    [Fact]
    public void UsesTheExactSharedTranslationCoverConformanceCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "periodic-topology-v1.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var entry in json.RootElement.GetProperty("symbolCoverCases").EnumerateArray())
        {
            var label = entry.GetProperty("name").GetString();
            var symbol = entry.GetProperty("dsSymbol").GetString()!;
            var expected = entry.GetProperty("status").GetString()! switch
            {
                "constructed" => DelaneyDressCoverStatus.Constructed,
                "unsupported" => DelaneyDressCoverStatus.Unsupported,
                "invalid" => DelaneyDressCoverStatus.Invalid,
                _ => throw new InvalidOperationException($"Unexpected corpus status: {label}")
            };
            var result = DelaneyDressTorusCover.Construct(symbol);
            Assert.True(result.Status == expected, $"{label}: {result.Status}: {result.Reason}");
            if (expected == DelaneyDressCoverStatus.Constructed)
            {
                Assert.NotNull(result.Witness);
                result.Witness!.ValidateAdjacency();
                var expectedSides = entry.GetProperty("sideCounts").EnumerateArray()
                    .Select(value => value.GetInt32()).OrderBy(value => value).ToArray();
                var actualSides = result.Witness.MotifCells.Select(cell => cell.Boundary.Count)
                    .OrderBy(value => value).ToArray();
                Assert.Equal(expectedSides, actualSides);
            }
        }
    }

    [Fact]
    public void RejectsSymmetryQuotientsAsUnsupportedNotInvalidMathematics()
    {
        foreach (var symbol in new[] { "<1:1,1,1:4,4>", "<1:1,1,1:3,6>", "<1:1,1,1:6,3>" })
            Assert.Equal(DelaneyDressCoverStatus.Unsupported, DelaneyDressTorusCover.Construct(symbol).Status);
        Assert.Equal(DelaneyDressCoverStatus.Invalid, DelaneyDressTorusCover.Construct("<1:1,1,1:3,3>").Status);
    }
}
