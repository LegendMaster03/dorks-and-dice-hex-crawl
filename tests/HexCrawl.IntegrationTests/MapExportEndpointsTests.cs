using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class MapExportEndpointsTests
{
    [Theory]
    [InlineData("<1:1,1,1:6,3>")]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>")]
    public async Task FiniteMapExportPaginatesAuthoritativeCellsWithoutChangingIdentity(string symbol)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "Full map export",
                tilingDsSymbol = symbol,
                worldUnitsPerAbstractPeriod = 1.0,
                origin = new { x = 10.0, y = -20.0 },
                playableBounds = new { width = 5, height = 5, minU = -2, minV = 3 }
            });
            created.EnsureSuccessStatusCode();
            var world = await created.Content.ReadFromJsonAsync<JsonElement>();
            var id = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();
            var expectedTotal = 25 * world.GetProperty("tiling").GetProperty("topology")
                .GetProperty("motifCells").GetArrayLength();
            var keys = new HashSet<string>();
            JsonElement envelope = default;
            for (var offset = 0; offset < expectedTotal; offset += 7)
            {
                using var response = await client.GetAsync(
                    $"/api/overworlds/{id:D}/cells/export?expectedVersion={version}&offset={offset}&limit=7");
                response.EnsureSuccessStatusCode();
                var page = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(expectedTotal, page.GetProperty("totalCells").GetInt32());
                Assert.Equal(offset, page.GetProperty("offset").GetInt32());
                Assert.Equal(version, page.GetProperty("worldVersion").GetInt64());
                Assert.Equal(world.GetProperty("tiling").GetProperty("id").GetGuid(),
                    page.GetProperty("tilingId").GetGuid());
                envelope = page.GetProperty("bounds");
                foreach (var cell in page.GetProperty("cells").EnumerateArray())
                {
                    Assert.True(keys.Add(cell.GetProperty("id").GetRawText()));
                    Assert.True(cell.GetProperty("polygon").GetArrayLength() >= 3);
                }
            }
            Assert.Equal(expectedTotal, keys.Count);
            using var viewport = await client.GetAsync(
                $"/api/overworlds/{id:D}/cells/viewport?minX={Number(envelope.GetProperty("minX").GetDouble() - 0.01)}&minY={Number(envelope.GetProperty("minY").GetDouble() - 0.01)}&maxX={Number(envelope.GetProperty("maxX").GetDouble() + 0.01)}&maxY={Number(envelope.GetProperty("maxY").GetDouble() + 0.01)}");
            viewport.EnsureSuccessStatusCode();
            var visible = await viewport.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(keys.SetEquals(visible.EnumerateArray()
                .Select(cell => cell.GetProperty("id").GetRawText())));

            using var renamed = await client.PutAsJsonAsync($"/api/overworlds/{id:D}/name",
                new { name = "Updated world", expectedVersion = version });
            renamed.EnsureSuccessStatusCode();
            using var stale = await client.GetAsync(
                $"/api/overworlds/{id:D}/cells/export?expectedVersion={version}&offset=0&limit=7");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            using var otherHost = TestWebHost.Create(database, "bob");
            using var other = otherHost.CreateClient();
            using var forbidden = await other.GetAsync(
                $"/api/overworlds/{id:D}/cells/export?expectedVersion={version}&offset=0&limit=7");
            Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task FullExportRejectsUnboundedAndOversizedWorlds()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            async Task<(Guid Id, long Version)> Create(object? bounds)
            {
                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Map export limit",
                    tilingDsSymbol = "<1:1,1,1:4,4>",
                    worldUnitsPerAbstractPeriod = 1.0,
                    origin = new { x = 0.0, y = 0.0 },
                    playableBounds = bounds
                });
                created.EnsureSuccessStatusCode();
                var world = await created.Content.ReadFromJsonAsync<JsonElement>();
                return (world.GetProperty("id").GetGuid(), world.GetProperty("version").GetInt64());
            }
            var unbounded = await Create(null);
            var oversized = await Create(new { width = 150, height = 150 });
            foreach (var item in new[] { unbounded, oversized })
            {
                using var response = await client.GetAsync(
                    $"/api/overworlds/{item.Id:D}/cells/export?expectedVersion={item.Version}&offset=0&limit=10");
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    private static string Number(double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
