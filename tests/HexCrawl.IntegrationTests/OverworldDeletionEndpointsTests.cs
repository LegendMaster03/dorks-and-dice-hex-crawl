using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class OverworldDeletionEndpointsTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task OwnerCanDeleteOverworldAndOwnedRasterAssets()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Disposable world");
            var worldId = world.GetProperty("id").GetGuid();
            var uploaded = await Upload(client, worldId, world.GetProperty("version").GetInt64());
            var version = uploaded.GetProperty("version").GetInt64();
            var mapsDirectory = Path.Combine(TestWebHost.AssetRoot(database), "maps");
            Assert.Single(Directory.GetFiles(mapsDirectory));

            using var response = await client.DeleteAsync($"/api/overworlds/{worldId:D}?expectedVersion={version}");
            response.EnsureSuccessStatusCode();

            using var reopened = await client.GetAsync($"/api/overworlds/{worldId:D}");
            Assert.Equal(HttpStatusCode.NotFound, reopened.StatusCode);
            Assert.Empty(Directory.GetFiles(mapsDirectory));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task DeleteOverworldWithSavedExpeditionReturnsConflictAndKeepsWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Active world");
            var worldId = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();

            using (var start = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
            {
                name = "Active expedition",
                procedureKey = "simple-fixed-distance",
                presentationKey = "exploration-map",
                startHex = new { q = 0, r = 0 }
            }))
            {
                start.EnsureSuccessStatusCode();
            }

            using var response = await client.DeleteAsync($"/api/overworlds/{worldId:D}?expectedVersion={version}");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("saved expeditions", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

            using var reopened = await client.GetAsync($"/api/overworlds/{worldId:D}");
            reopened.EnsureSuccessStatusCode();
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task OtherOwnerCanNotDeleteOverworld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            long version;
            using (var ownerFactory = TestWebHost.Create(database, "alice"))
            using (var owner = ownerFactory.CreateClient())
            {
                var world = await CreateWorld(owner, "Alice world");
                worldId = world.GetProperty("id").GetGuid();
                version = world.GetProperty("version").GetInt64();
            }

            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            using (var response = await other.DeleteAsync($"/api/overworlds/{worldId:D}?expectedVersion={version}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            using var reopenedFactory = TestWebHost.Create(database, "alice");
            using var reopenedClient = reopenedFactory.CreateClient();
            using var reopened = await reopenedClient.GetAsync($"/api/overworlds/{worldId:D}");
            reopened.EnsureSuccessStatusCode();
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> CreateWorld(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/overworlds", new
        {
            name,
            orientation = "PointyTop",
            origin = new { x = 0, y = 0 },
            rotationDegrees = 0,
            hexRadiusWorldUnits = 1,
            neighborCenterDistance = 12,
            distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Upload(HttpClient client, Guid worldId, long expectedVersion)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TinyPng);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(file, "file", "delete-me.png");
        content.Add(new StringContent("Delete me"), "name");
        content.Add(new StringContent("delete-fixture"), "geographyKey");
        content.Add(new StringContent("GM"), "role");
        content.Add(new StringContent("False"), "containsBakedGrid");
        content.Add(new StringContent(expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");

        using var response = await client.PostAsync($"/api/overworlds/{worldId:D}/source-maps", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
