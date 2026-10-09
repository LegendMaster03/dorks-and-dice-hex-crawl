using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class Phase16OrientationCoverTests
{
    [Fact]
    public void SharesTheSameVersionedOrientationCoverEnvelopeWithSurveyor()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "periodic-topology-v1.json");
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var entry in json.RootElement.GetProperty("orientationCoverCases").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var symbol = entry.GetProperty("dsSymbol").GetString()!;
            int limit = entry.TryGetProperty("chamberLimit", out var bound) ? bound.GetInt32() : 1024;
            var expected = entry.GetProperty("status").GetString()! switch
            {
                "constructed" => DelaneyDressCoverStatus.Constructed,
                "invalid" => DelaneyDressCoverStatus.Invalid,
                "unsupported" => DelaneyDressCoverStatus.Unsupported,
                _ => throw new InvalidOperationException($"Unknown orientation result: {name}")
            };
            var result = DelaneyDressOrientationCover.Construct(symbol, limit);
            Assert.True(result.Status == expected, $"{name}: {result.Status}: {result.Reason}");
            if (expected != DelaneyDressCoverStatus.Constructed) continue;
            bool trivial = entry.GetProperty("isTrivial").GetBoolean();
            Assert.Equal(trivial, result.IsTrivial);
            var original = DelaneyDressTopology.Inspect(symbol, 2048).Symbol!;
            var cover = DelaneyDressTopology.Inspect(result.CoverSymbol, 2048);
            Assert.Equal(DelaneyDressStatus.Euclidean, cover.Status);
            Assert.True(cover.Symbol!.FixedPointFree);
            Assert.True(cover.Symbol.WeaklyOrientable);
            Assert.NotNull(DelaneyDressTopology.ProjectChambers(cover.Symbol, original));
            Assert.Equal(cover.Symbol.ChamberCount, result.ChamberCount);
            Assert.Equal(result.ChamberCount + 1, result.SourceProjection!.Count);
            if (trivial)
            {
                Assert.Equal(0, result.RemainingBranchedOrbits);
                Assert.Equal(DelaneyDressCoverStatus.Constructed,
                    DelaneyDressTorusCover.Construct(result.CoverSymbol!).Status);
            }
            else
            {
                Assert.True(result.RemainingBranchedOrbits > 0);
                Assert.Equal(DelaneyDressCoverStatus.Unsupported,
                    DelaneyDressTorusCover.Construct(result.CoverSymbol!).Status);
            }
            var second = DelaneyDressOrientationCover.Construct(result.CoverSymbol!);
            Assert.Equal(DelaneyDressCoverStatus.Constructed, second.Status);
            Assert.Equal(result.CoverSymbol, second.CoverSymbol);
            Assert.True(second.IsTrivial);
        }
    }
}
