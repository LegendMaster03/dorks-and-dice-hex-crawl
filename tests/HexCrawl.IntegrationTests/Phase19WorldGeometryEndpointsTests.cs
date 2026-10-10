using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.IntegrationTests;

public sealed class Phase19WorldGeometryEndpointsTests
{
    [Fact]
    public async Task PeriodicWorldWithoutImageCanBeCreatedEnumeratedAndSelected()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            JsonElement cell;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Generated square world",
                    tilingDsSymbol = "<1:1,1,1:4,4>",
                    worldUnitsPerAbstractPeriod = 3.0,
                    origin = new { x = 12.0, y = -4.0 }
                });
                created.EnsureSuccessStatusCode();
                var world = await created.Content.ReadFromJsonAsync<JsonElement>();
                worldId = world.GetProperty("id").GetGuid();
                Assert.Equal(JsonValueKind.Null, world.GetProperty("grid").ValueKind);
                Assert.NotEqual(JsonValueKind.Null, world.GetProperty("tiling").ValueKind);
                Assert.Equal(0, world.GetProperty("sourceMaps").GetArrayLength());
                using var viewport = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/viewport?minX=8&minY=-8&maxX=16&maxY=0");
                viewport.EnsureSuccessStatusCode();
                var cells = await viewport.Content.ReadFromJsonAsync<JsonElement>();
                Assert.NotEmpty(cells.EnumerateArray());
                cell = cells.EnumerateArray().First();
                var center = cell.GetProperty("center");
                using var containing = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/containing?x={center.GetProperty("x").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture)}&y={center.GetProperty("y").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                containing.EnsureSuccessStatusCode();
                var lookup = await containing.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Unique", lookup.GetProperty("status").GetString());
                Assert.Single(lookup.GetProperty("candidates").EnumerateArray());
                var address = cell.GetProperty("id").GetProperty("address");
                using var resolved = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/resolve?motifCellId={Uri.EscapeDataString(address.GetProperty("motifCellId").GetString()!)}&u={address.GetProperty("translation").GetProperty("u").GetInt64()}&v={address.GetProperty("translation").GetProperty("v").GetInt64()}");
                resolved.EnsureSuccessStatusCode();
                var result = await resolved.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(
                    cell.GetProperty("id").GetProperty("tilingId").GetGuid(),
                    result.GetProperty("id").GetProperty("tilingId").GetGuid());
                Assert.True(result.GetProperty("polygon").GetArrayLength() >= 3);
                // Complete the image-free workflow without a custom procedure
                // JSON editor or a reconfigured preset template.
                using var createdProcedure = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures", new { });
                createdProcedure.EnsureSuccessStatusCode();
                var pinned = await createdProcedure.Content.ReadFromJsonAsync<JsonElement>();
                using var start = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions", new
                    {
                        name = "First cell journey",
                        procedureId = pinned.GetProperty("procedureId").GetGuid(),
                        procedureRevision = pinned.GetProperty("revision").GetInt32(),
                        presentationKey = "dm-controlled",
                        startCell = address
                    });
                start.EnsureSuccessStatusCode();
                var started = await start.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(JsonValueKind.Null, started.GetProperty("expedition")
                    .GetProperty("currentHex").ValueKind);
                Assert.Equal("CellSteps", started.GetProperty("procedure")
                    .GetProperty("runtime").GetProperty("travelResolution").GetString());
                Assert.Equal(cell.GetProperty("id").GetProperty("tilingId").GetGuid(),
                    started.GetProperty("expedition").GetProperty("currentCell")
                        .GetProperty("tilingId").GetGuid());
                // Metadata-only rename must preserve stored traversal and
                // the underlying world geometry after an expedition exists.
                using var renamed = await client.PutAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/name", new
                    {
                        name = "Renamed periodic world",
                        expectedVersion = world.GetProperty("version").GetInt64()
                    });
                renamed.EnsureSuccessStatusCode();
                var renamedWorld = await renamed.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Renamed periodic world", renamedWorld.GetProperty("name").GetString());
                Assert.Equal(JsonValueKind.Null, renamedWorld.GetProperty("grid").ValueKind);
                Assert.Equal(cell.GetProperty("id").GetProperty("tilingId").GetGuid(),
                    renamedWorld.GetProperty("tiling").GetProperty("id").GetGuid());
            }

            using (var otherHost = TestWebHost.Create(database, "bob"))
            using (var other = otherHost.CreateClient())
            {
                Assert.Equal(HttpStatusCode.NotFound,
                    (await other.GetAsync($"/api/overworlds/{worldId:D}/cells/viewport?minX=0&minY=0&maxX=1&maxY=1")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound,
                    (await other.GetAsync($"/api/overworlds/{worldId:D}/cells/containing?x=0&y=0")).StatusCode);
                using var deniedProcedure = await other.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures", new { });
                Assert.Equal(HttpStatusCode.NotFound, deniedProcedure.StatusCode);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task InvalidPatternDoesNotPersistAWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "Invalid metric world",
                tilingDsSymbol = "<1:1,1,1:7,3>",
                worldUnitsPerAbstractPeriod = 0,
                origin = new { x = 0, y = 0 }
            });
            Assert.False(created.IsSuccessStatusCode);
            var worlds = await client.GetFromJsonAsync<JsonElement>("/api/overworlds");
            Assert.Empty(worlds.EnumerateArray());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
