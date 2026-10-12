using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class PeriodicMapRegistrationEndpointsTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task PeriodicRegistrationPreservesGeometryAndSourceAssetAcrossRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            Guid mapId;
            string tilingIdentity;
            long savedVersion;

            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Periodic registration",
                    tilingDsSymbol = "<1:1,1,1:6,3>",
                    worldUnitsPerAbstractPeriod = 1,
                    origin = new { x = 0, y = 0 }
                });
                created.EnsureSuccessStatusCode();
                var world = await created.Content.ReadFromJsonAsync<JsonElement>();
                worldId = world.GetProperty("id").GetGuid();
                tilingIdentity = world.GetProperty("tiling").GetProperty("id").GetString()!;
                var uploaded = await Upload(client, worldId, world.GetProperty("version").GetInt64());
                mapId = Assert.Single(uploaded.GetProperty("sourceMaps").EnumerateArray())
                    .GetProperty("id").GetGuid();

                var request = new
                {
                    expectedVersion = uploaded.GetProperty("version").GetInt64(),
                    controlPoints = new[]
                    {
                        new { sourcePixel = new { x = 0, y = 0 }, worldPoint = new { x = 10, y = 20 } },
                        new { sourcePixel = new { x = 1, y = 0 }, worldPoint = new { x = 14, y = 20 } },
                        new { sourcePixel = new { x = 0, y = 1 }, worldPoint = new { x = 10, y = 26 } }
                    }
                };
                var url = $"/api/overworlds/{worldId:D}/source-maps/{mapId:D}/registration";
                using var appliedResponse = await client.PutAsJsonAsync(url, request);
                appliedResponse.EnsureSuccessStatusCode();
                var applied = await appliedResponse.Content.ReadFromJsonAsync<JsonElement>();
                savedVersion = applied.GetProperty("version").GetInt64();

                Assert.Equal(request.expectedVersion + 1, savedVersion);
                Assert.Equal(tilingIdentity, applied.GetProperty("tiling").GetProperty("id").GetString());
                var map = Assert.Single(applied.GetProperty("sourceMaps").EnumerateArray());
                Assert.Equal("Affine", map.GetProperty("alignment").GetProperty("kind").GetString());
                Assert.Equal(4, map.GetProperty("alignment").GetProperty("m11").GetDouble(), 8);
                Assert.Equal(10, map.GetProperty("alignment").GetProperty("m13").GetDouble(), 8);
                Assert.Equal(6, map.GetProperty("alignment").GetProperty("m22").GetDouble(), 8);
                Assert.Equal(4, map.GetProperty("worldCoverageBoundary").GetArrayLength());
                Assert.Equal(TinyPng, await client.GetByteArrayAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{mapId:D}/asset"));

                using var stale = await client.PutAsJsonAsync(url, request);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
                Assert.Equal(savedVersion, reloaded.GetProperty("version").GetInt64());
                Assert.Equal(tilingIdentity, reloaded.GetProperty("tiling").GetProperty("id").GetString());
                var map = Assert.Single(reloaded.GetProperty("sourceMaps").EnumerateArray());
                Assert.Equal(mapId, map.GetProperty("id").GetGuid());
                Assert.Equal(6, map.GetProperty("alignment").GetProperty("m22").GetDouble(), 8);
                Assert.Equal(4, map.GetProperty("worldCoverageBoundary").GetArrayLength());
                Assert.Equal(TinyPng, await client.GetByteArrayAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{mapId:D}/asset"));
            }

            using (var factory = TestWebHost.Create(database, "bob"))
            using (var client = factory.CreateClient())
            {
                using var forbidden = await client.PutAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{mapId:D}/registration",
                    new { expectedVersion = savedVersion, controlPoints = Array.Empty<object>() });
                Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> Upload(HttpClient client, Guid worldId, long expectedVersion)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TinyPng);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(file, "file", "baked-grid.png");
        content.Add(new StringContent("Baked grid"), "name");
        content.Add(new StringContent("http-fixture"), "geographyKey");
        content.Add(new StringContent("GM"), "role");
        content.Add(new StringContent("True"), "containsBakedGrid");
        content.Add(new StringContent(expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");

        using var response = await client.PostAsync($"/api/overworlds/{worldId:D}/source-maps", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
