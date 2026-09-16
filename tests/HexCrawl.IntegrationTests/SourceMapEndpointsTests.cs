using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class SourceMapEndpointsTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task OwnerCanUploadListAndStreamAssetWhileOtherUserCanNotReadOrMutateIt()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            Guid sourceMapId;
            using (var ownerFactory = TestWebHost.Create(database, "alice"))
            using (var owner = ownerFactory.CreateClient())
            {
                var world = await CreateWorld(owner, "Owner world");
                worldId = world.GetProperty("id").GetGuid();
                var uploaded = await Upload(owner, worldId, world.GetProperty("version").GetInt64(), "Bellowing Wilds", "GM grid", "GM", true);
                sourceMapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();

                var list = await owner.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}/source-maps");
                Assert.Equal(1, list.GetProperty("sourceMaps")[0].GetProperty("pixelWidth").GetInt32());
                Assert.Equal("image/png", list.GetProperty("sourceMaps")[0].GetProperty("mediaType").GetString());
                var bytes = await owner.GetByteArrayAsync($"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/asset");
                Assert.Equal(TinyPng, bytes);
            }

            using var otherFactory = TestWebHost.Create(database, "bob");
            using var other = otherFactory.CreateClient();
            using var read = await other.GetAsync($"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/asset");
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            using var upload = await UploadResponse(other, worldId, 1, "Stolen", "Nope", "Neutral", false, TinyPng);
            Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task UnsupportedMalformedAndOversizeRasterInputsAreRejected()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, maxMapFileBytes: 32);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Validation");
            var worldId = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();

            using var unsupported = await UploadResponse(client, worldId, version, "Other", "GIF", "Other", false, "GIF89a-unsupported-raster"u8.ToArray());
            Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
            using var malformed = await UploadResponse(client, worldId, version, "Other", "Broken PNG", "Other", false, new byte[] { 137,80,78,71,13,10,26,10,0,0,0,13 });
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            using var oversize = await UploadResponse(client, worldId, version, "Other", "Large", "Other", false, TinyPng);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversize.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task MetadataAndBinarySurviveApplicationRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            Guid sourceMapId;
            using (var firstFactory = TestWebHost.Create(database))
            using (var first = firstFactory.CreateClient())
            {
                var world = await CreateWorld(first, "Restart map");
                worldId = world.GetProperty("id").GetGuid();
                var uploaded = await Upload(first, worldId, world.GetProperty("version").GetInt64(), "Kylandria", "Kylandria neutral", "Neutral", false);
                sourceMapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();
                Assert.Equal(TinyPng, await first.GetByteArrayAsync($"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/asset"));
            }

            using var secondFactory = TestWebHost.Create(database);
            using var second = secondFactory.CreateClient();
            var list = await second.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}/source-maps");
            Assert.Equal("Kylandria", list.GetProperty("sourceMaps")[0].GetProperty("geographyKey").GetString());
            Assert.Equal(TinyPng, await second.GetByteArrayAsync($"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/asset"));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task GeographyGroupsShareStableKeysWithoutMergingRepresentations()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Atlas-free world");
            var worldId = world.GetProperty("id").GetGuid();
            var first = await Upload(client, worldId, world.GetProperty("version").GetInt64(), "Bellowing Wilds", "GM", "GM", true);
            var second = await Upload(client, worldId, first.GetProperty("version").GetInt64(), "Bellowing Wilds", "Player", "Player", false);
            _ = await Upload(client, worldId, second.GetProperty("version").GetInt64(), "Kylandria", "Kylandria", "Neutral", false);

            var list = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}/source-maps");
            var maps = list.GetProperty("sourceMaps").EnumerateArray().ToArray();
            Assert.Equal(3, maps.Length);
            Assert.Equal("Bellowing Wilds", maps[0].GetProperty("geographyKey").GetString());
            Assert.Equal("Bellowing Wilds", maps[1].GetProperty("geographyKey").GetString());
            Assert.Equal("Kylandria", maps[2].GetProperty("geographyKey").GetString());
            Assert.Equal(3, maps.Select(item => item.GetProperty("id").GetGuid()).Distinct().Count());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task AffineRegistrationPersistsTransformAndDerivedCoverage()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            Guid mapId;
            using (var firstFactory = TestWebHost.Create(database))
            using (var client = firstFactory.CreateClient())
            {
                var world = await CreateWorld(client, "Registration");
                worldId = world.GetProperty("id").GetGuid();
                var uploaded = await Upload(client, worldId, world.GetProperty("version").GetInt64(), "Map", "Map", "Neutral", false);
                mapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();
                var version = uploaded.GetProperty("version").GetInt64();
                using var response = await client.PutAsJsonAsync($"/api/overworlds/{worldId:D}/source-maps/{mapId:D}/registration", new
                {
                    expectedVersion = version,
                    controlPoints = new[]
                    {
                        new { sourcePixel = new { x = 0, y = 0 }, worldPoint = new { x = 10, y = -5 } },
                        new { sourcePixel = new { x = 1, y = 0 }, worldPoint = new { x = 12, y = -5 } },
                        new { sourcePixel = new { x = 0, y = 1 }, worldPoint = new { x = 10, y = -2 } }
                    }
                });
                response.EnsureSuccessStatusCode();
                var registered = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Affine", registered.GetProperty("sourceMaps")[0].GetProperty("alignment").GetProperty("kind").GetString());
                Assert.Equal(4, registered.GetProperty("sourceMaps")[0].GetProperty("worldCoverageBoundary").GetArrayLength());
            }

            using var secondFactory = TestWebHost.Create(database);
            using var second = secondFactory.CreateClient();
            var reopened = await second.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            var map = reopened.GetProperty("sourceMaps")[0];
            Assert.Equal("Affine", map.GetProperty("alignment").GetProperty("kind").GetString());
            Assert.Equal(12, map.GetProperty("worldCoverageBoundary")[1].GetProperty("x").GetDouble(), 8);
            Assert.Equal(-2, map.GetProperty("worldCoverageBoundary")[2].GetProperty("y").GetDouble(), 8);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task DegenerateRegistrationIsRejectedWithoutChangingWorldVersion()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Bad registration");
            var worldId = world.GetProperty("id").GetGuid();
            var uploaded = await Upload(client, worldId, world.GetProperty("version").GetInt64(), "Map", "Map", "Neutral", false);
            var mapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();
            var version = uploaded.GetProperty("version").GetInt64();
            using var response = await client.PutAsJsonAsync($"/api/overworlds/{worldId:D}/source-maps/{mapId:D}/registration", new
            {
                expectedVersion = version,
                controlPoints = new[]
                {
                    new { sourcePixel = new { x = 0, y = 0 }, worldPoint = new { x = 0, y = 0 } },
                    new { sourcePixel = new { x = 1, y = 1 }, worldPoint = new { x = 1, y = 1 } },
                    new { sourcePixel = new { x = 2, y = 2 }, worldPoint = new { x = 2, y = 2 } }
                }
            });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
            Assert.Equal(JsonValueKind.Null, reopened.GetProperty("sourceMaps")[0].GetProperty("alignment").ValueKind);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task DeletingRepresentationRemovesBinaryButPreservesSemanticWorldObjects()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Deletion");
            var worldId = world.GetProperty("id").GetGuid();
            using var locationResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/locations", new
            {
                name = "Town",
                category = "settlement",
                position = new { x = 3, y = 4 },
                discoverability = "Obvious",
                expectedVersion = world.GetProperty("version").GetInt64()
            });
            locationResponse.EnsureSuccessStatusCode();
            var withLocation = await locationResponse.Content.ReadFromJsonAsync<JsonElement>();
            var uploaded = await Upload(client, worldId, withLocation.GetProperty("version").GetInt64(), "Map", "Map", "Neutral", false);
            var mapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();
            using var deletion = await client.DeleteAsync($"/api/overworlds/{worldId:D}/source-maps/{mapId:D}?expectedVersion={uploaded.GetProperty("version").GetInt64()}");
            deletion.EnsureSuccessStatusCode();
            var deleted = await deletion.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(deleted.GetProperty("sourceMaps").EnumerateArray());
            Assert.Single(deleted.GetProperty("locations").EnumerateArray());
            using var missingAsset = await client.GetAsync($"/api/overworlds/{worldId:D}/source-maps/{mapId:D}/asset");
            Assert.Equal(HttpStatusCode.NotFound, missingAsset.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> Upload(
        HttpClient client,
        Guid worldId,
        long expectedVersion,
        string geography,
        string name,
        string role,
        bool bakedGrid)
    {
        using var response = await UploadResponse(client, worldId, expectedVersion, geography, name, role, bakedGrid, TinyPng);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> UploadResponse(
        HttpClient client,
        Guid worldId,
        long expectedVersion,
        string geography,
        string name,
        string role,
        bool bakedGrid,
        byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        content.Add(file, "file", "../../untrusted-map-name.png");
        content.Add(new StringContent(name), "name");
        content.Add(new StringContent(geography), "geographyKey");
        content.Add(new StringContent(role), "role");
        content.Add(new StringContent(bakedGrid.ToString()), "containsBakedGrid");
        content.Add(new StringContent(expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");
        return await client.PostAsync($"/api/overworlds/{worldId:D}/source-maps", content);
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
}
