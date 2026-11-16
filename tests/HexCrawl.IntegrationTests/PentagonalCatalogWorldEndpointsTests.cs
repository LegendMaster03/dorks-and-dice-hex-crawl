using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Domain.Presentation;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

/// <summary>
/// A non-hexagonal monohedral fixture exercises existing endpoints end-to-end.
/// No pentagon-specific creation, rendering or traversal endpoint is permitted.
/// </summary>
public sealed class PentagonalCatalogWorldEndpointsTests
{
    [Theory]
    [InlineData("convex-pentagon-type-1", 2, "pentagon-a", 5)]
    [InlineData("convex-pentagon-type-2", 8, "H00", 5)]
    [InlineData("convex-pentagon-type-3", 3, "A", 6)]
    [InlineData("convex-pentagon-type-4", 8, "H00", 5)]
    [InlineData("convex-pentagon-type-15", 12, "T15-01", 5)]
    public async Task ConvexCatalogGeometryCreatesAndReloadsAnOrdinaryPeriodicWorld(
        string presetId, int motifCellCount, string startMotifCellId, int atomicInterfaceCount)
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            choice => choice.PresetId == presetId);
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var catalog = await client.GetAsync("/api/procedures/tilings");
                catalog.EnsureSuccessStatusCode();
                var options = (await catalog.Content.ReadFromJsonAsync<JsonElement>())
                    .EnumerateArray().ToArray();
                var option = Assert.Single(options, item => item
                    .GetProperty("presetId").GetString() == entry.PresetId);
                Assert.Equal("Pentagons", option.GetProperty("categoryPath")[0].GetString());
                Assert.Equal("Convex", option.GetProperty("categoryPath")[1].GetString());
                Assert.Equal(motifCellCount, option.GetProperty("realization")
                    .GetProperty("polygons").EnumerateObject().Count());
                Assert.Equal("world-unit", option.GetProperty("realization")
                    .GetProperty("units").GetString());
                Assert.Equal(motifCellCount, option.GetProperty("topologyWitness")
                    .GetProperty("motifCells").GetArrayLength());

                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Verified pentagonal grid",
                    tilingDsSymbol = entry.CanonicalDsSymbol,
                    worldUnitsPerAbstractPeriod = 1.0,
                    origin = new { x = 0.0, y = 0.0 },
                    playableBounds = new { width = 3, height = 3, minU = -1, minV = -1 },
                    topologyWitness = entry.RepresentativeTopology,
                    metricRealization = entry.RepresentativeRealization
                });
                Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
                worldId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("id").GetGuid();

                using var viewport = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/viewport?minX=-30&minY=-30&maxX=30&maxY=30");
                viewport.EnsureSuccessStatusCode();
                var cells = (await viewport.Content.ReadFromJsonAsync<JsonElement>())
                    .EnumerateArray().ToArray();
                Assert.Equal(motifCellCount * 9, cells.Length);
                Assert.All(cells, cell => Assert.Equal(atomicInterfaceCount,
                    cell.GetProperty("polygon").GetArrayLength()));

                using var generated = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures", new { });
                Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync());
                var procedure = await generated.Content.ReadFromJsonAsync<JsonElement>();

                using var started = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions", new
                    {
                        name = "Pentagon movement",
                        procedureId = procedure.GetProperty("procedureId").GetGuid(),
                        procedureRevision = procedure.GetProperty("revision").GetInt32(),
                        presentationKey = "dm-controlled",
                        // Interior cells retain all five exits; finite boundaries intentionally hide invalid exits.
                        startCell = cells.Single(cell =>
                            cell.GetProperty("id").GetProperty("address").GetProperty("translation")
                                .GetProperty("u").GetInt64() == 0 &&
                            cell.GetProperty("id").GetProperty("address").GetProperty("translation")
                                .GetProperty("v").GetInt64() == 0 &&
                            cell.GetProperty("id").GetProperty("address").GetProperty("motifCellId")
                                .GetString() == startMotifCellId).GetProperty("id").GetProperty("address")
                    });
                Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
                var expedition = await started.Content.ReadFromJsonAsync<JsonElement>();
                using var navigation = await client.GetAsync(
                    $"/api/expeditions/{expedition.GetProperty("id").GetGuid():D}/cell-topology");
                navigation.EnsureSuccessStatusCode();
                var topology = await navigation.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(atomicInterfaceCount, topology.GetProperty("interfaces").GetArrayLength());
            }

            var reloaded = await new PostgresHexCrawlStore(database)
                .GetOverworldAsync(worldId, "alice");
            Assert.NotNull(reloaded);
            reloaded!.World.SpatialTiling.Validate();
            Assert.Equal(motifCellCount, reloaded.World.SpatialTiling.Topology.MotifCells.Count);
            Assert.Equal(3, reloaded.World.SpatialTiling.PlayableBounds!.Value.Width);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
