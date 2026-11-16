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
    [Fact]
    public async Task ConvexType1CatalogGeometryCreatesAndReloadsAnOrdinaryPeriodicWorld()
    {
        var entry = Assert.Single(CrawlTilingNames.Catalog.ListEntries(),
            choice => choice.PresetId == "convex-pentagon-type-1");
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
                Assert.Equal(2, option.GetProperty("realization")
                    .GetProperty("polygons").EnumerateObject().Count());
                Assert.Equal("world-unit", option.GetProperty("realization")
                    .GetProperty("units").GetString());
                Assert.Equal(2, option.GetProperty("topologyWitness")
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
                    $"/api/overworlds/{worldId:D}/cells/viewport?minX=-5&minY=-5&maxX=5&maxY=5");
                viewport.EnsureSuccessStatusCode();
                var cells = (await viewport.Content.ReadFromJsonAsync<JsonElement>())
                    .EnumerateArray().ToArray();
                Assert.Equal(18, cells.Length);
                Assert.All(cells, cell => Assert.Equal(5,
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
                                .GetString() == "pentagon-a").GetProperty("id").GetProperty("address")
                    });
                Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
                var expedition = await started.Content.ReadFromJsonAsync<JsonElement>();
                using var navigation = await client.GetAsync(
                    $"/api/expeditions/{expedition.GetProperty("id").GetGuid():D}/cell-topology");
                navigation.EnsureSuccessStatusCode();
                var topology = await navigation.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(5, topology.GetProperty("interfaces").GetArrayLength());
            }

            var reloaded = await new PostgresHexCrawlStore(database)
                .GetOverworldAsync(worldId, "alice");
            Assert.NotNull(reloaded);
            reloaded!.World.SpatialTiling.Validate();
            Assert.Equal(2, reloaded.World.SpatialTiling.Topology.MotifCells.Count);
            Assert.Equal(3, reloaded.World.SpatialTiling.PlayableBounds!.Value.Width);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
