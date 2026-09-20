using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
    public async Task OwnerCanInspectWonderdraftProjectWithoutMutatingWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Wonderdraft inspection");
            var worldId = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();

            using var content = new MultipartFormDataContent();
            var project = new ByteArrayContent(BuildWonderdraftProject());
            project.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            content.Add(project, "file", "campaign.wonderdraft_map");
            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/wonderdraft/inspect",
                content);
            response.EnsureSuccessStatusCode();

            var inspected = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(15, inspected.GetProperty("formatVersion").GetInt32());
            Assert.Equal(1024, inspected.GetProperty("pixelWidth").GetInt32());
            Assert.Equal(768, inspected.GetProperty("pixelHeight").GetInt32());
            Assert.Equal(2, inspected.GetProperty("symbolCount").GetInt32());
            Assert.Equal(1, inspected.GetProperty("labelCount").GetInt32());
            Assert.Equal(3, inspected.GetProperty("pathCount").GetInt32());
            Assert.Equal(2, inspected.GetProperty("territoryCount").GetInt32());
            Assert.True(inspected.GetProperty("hasGrid").GetBoolean());

            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
            Assert.Empty(reopened.GetProperty("sourceMaps").EnumerateArray());

            using var otherFactory = TestWebHost.Create(database, "bob");
            using var other = otherFactory.CreateClient();
            using var unauthorizedContent = new MultipartFormDataContent();
            unauthorizedContent.Add(
                new ByteArrayContent(BuildWonderdraftProject()),
                "file",
                "campaign.wonderdraft_map");
            using var unauthorized = await other.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/wonderdraft/inspect",
                unauthorizedContent);
            Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task RegisteredSourceMapProjectsWonderdraftCandidatesIntoWorldCoordinatesWithoutMutation()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Wonderdraft candidates");
            var worldId = world.GetProperty("id").GetGuid();
            var uploaded = await Upload(
                client,
                worldId,
                world.GetProperty("version").GetInt64(),
                "Wonderdraft",
                "Registered export",
                "GM",
                false);
            var sourceMapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();

            using var registration = await client.PutAsJsonAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/registration",
                new
                {
                    expectedVersion = uploaded.GetProperty("version").GetInt64(),
                    controlPoints = new[]
                    {
                        new { sourcePixel = new { x = 0, y = 0 }, worldPoint = new { x = 10, y = 20 } },
                        new { sourcePixel = new { x = 1, y = 0 }, worldPoint = new { x = 14, y = 20 } },
                        new { sourcePixel = new { x = 0, y = 1 }, worldPoint = new { x = 10, y = 26 } }
                    }
                });
            registration.EnsureSuccessStatusCode();
            var registered = await registration.Content.ReadFromJsonAsync<JsonElement>();
            var version = registered.GetProperty("version").GetInt64();

            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(BuildWonderdraftProject()), "file", "campaign.wonderdraft_map");
            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/wonderdraft/candidates",
                content);
            response.EnsureSuccessStatusCode();
            var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(sourceMapId, preview.GetProperty("sourceMapId").GetGuid());
            Assert.Equal(1d / 1024d, preview.GetProperty("sourceScaleX").GetDouble(), 12);
            Assert.Equal(1d / 768d, preview.GetProperty("sourceScaleY").GetDouble(), 12);

            var label = preview.GetProperty("candidates").EnumerateArray()
                .Single(item => item.GetProperty("key").GetString() == "label:0");
            Assert.Equal("Old Harbor", label.GetProperty("displayName").GetString());
            Assert.Equal("Point", label.GetProperty("geometryKind").GetString());
            Assert.Equal(12, label.GetProperty("worldPosition").GetProperty("x").GetDouble(), 8);
            Assert.Equal(23, label.GetProperty("worldPosition").GetProperty("y").GetDouble(), 8);

            var pathCandidate = preview.GetProperty("candidates").EnumerateArray()
                .Single(item => item.GetProperty("key").GetString() == "path:0");
            Assert.Equal("Line", pathCandidate.GetProperty("geometryKind").GetString());
            Assert.Equal(2, pathCandidate.GetProperty("worldPoints").GetArrayLength());

            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
            Assert.Empty(reopened.GetProperty("features").EnumerateArray());
            Assert.Empty(reopened.GetProperty("locations").EnumerateArray());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task SelectiveWonderdraftImportPersistsOnlyReviewedCandidatesAndRejectsStaleVersion()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Wonderdraft selective import");
            var worldId = world.GetProperty("id").GetGuid();
            var uploaded = await Upload(
                client,
                worldId,
                world.GetProperty("version").GetInt64(),
                "Wonderdraft",
                "Registered export",
                "GM",
                false);
            var sourceMapId = uploaded.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid();

            using var registration = await client.PutAsJsonAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/registration",
                new
                {
                    expectedVersion = uploaded.GetProperty("version").GetInt64(),
                    controlPoints = new[]
                    {
                        new { sourcePixel = new { x = 0, y = 0 }, worldPoint = new { x = 10, y = 20 } },
                        new { sourcePixel = new { x = 1, y = 0 }, worldPoint = new { x = 14, y = 20 } },
                        new { sourcePixel = new { x = 0, y = 1 }, worldPoint = new { x = 10, y = 26 } }
                    }
                });
            registration.EnsureSuccessStatusCode();
            var registered = await registration.Content.ReadFromJsonAsync<JsonElement>();
            var importVersion = registered.GetProperty("version").GetInt64();

            var selections = JsonSerializer.Serialize(new[]
            {
                new
                {
                    candidateKey = "label:0",
                    target = "Location",
                    name = "Old Harbor",
                    category = "settlement",
                    discoverability = (string?)"Obvious"
                },
                new
                {
                    candidateKey = "path:0",
                    target = "LineFeature",
                    name = "Trade Road",
                    category = "road",
                    discoverability = (string?)null
                }
            });

            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(BuildWonderdraftProject()), "file", "campaign.wonderdraft_map");
            content.Add(new StringContent(selections), "selections");
            content.Add(new StringContent(importVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");
            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/wonderdraft/import",
                content);
            response.EnsureSuccessStatusCode();

            var imported = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(importVersion + 1, imported.GetProperty("version").GetInt64());

            var location = Assert.Single(imported.GetProperty("locations").EnumerateArray());
            Assert.Equal("Old Harbor", location.GetProperty("name").GetString());
            Assert.Equal("settlement", location.GetProperty("category").GetString());
            Assert.Equal(12, location.GetProperty("position").GetProperty("x").GetDouble(), 8);
            Assert.Equal(23, location.GetProperty("position").GetProperty("y").GetDouble(), 8);

            var feature = Assert.Single(imported.GetProperty("features").EnumerateArray());
            Assert.Equal("Trade Road", feature.GetProperty("name").GetString());
            Assert.Equal("Line", feature.GetProperty("kind").GetString());
            Assert.Equal(2, feature.GetProperty("path").GetArrayLength());

            using var staleContent = new MultipartFormDataContent();
            staleContent.Add(new ByteArrayContent(BuildWonderdraftProject()), "file", "campaign.wonderdraft_map");
            staleContent.Add(new StringContent(selections), "selections");
            staleContent.Add(new StringContent(importVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");
            using var stale = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/wonderdraft/import",
                staleContent);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Single(reopened.GetProperty("locations").EnumerateArray());
            Assert.Single(reopened.GetProperty("features").EnumerateArray());
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
    public async Task HeaderValidTruncatedRasterIsRejectedBeforeAssetOrWorldMutation()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Malformed structure");
            var worldId = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();

            using var malformed = await UploadResponse(
                client,
                worldId,
                version,
                "Other",
                "Header-valid truncated PNG",
                "Other",
                false,
                TinyPng[..33]);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
            Assert.Empty(reopened.GetProperty("sourceMaps").EnumerateArray());

            var assetRoot = database + ".assets";
            Assert.True(Directory.Exists(assetRoot));
            Assert.Empty(Directory.EnumerateFiles(assetRoot, "*", SearchOption.AllDirectories));
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

    private static byte[] BuildWonderdraftProject()
    {
        using var body = new MemoryStream();
        WriteVariantHeader(body, 18);
        WriteUInt32(body, 8);
        WriteVariantEntry(body, "version", () => WriteVariantInteger(body, 15));
        WriteVariantEntry(body, "map_width", () => WriteVariantInteger(body, 1024));
        WriteVariantEntry(body, "map_height", () => WriteVariantInteger(body, 768));
        WriteVariantEntry(body, "symbols", () => WriteVariantArray(body, 2, index =>
        {
            if (index == 0)
            {
                WriteVariantDictionary(body,
                    ("texture", () => WriteVariantString(body, "res://sprites/symbols/towns/castle")),
                    ("position", () => WriteVariantVector2(body, 256, 192)));
            }
            else
            {
                WriteVariantHeader(body, 0);
            }
        }));
        WriteVariantEntry(body, "labels", () => WriteVariantArray(body, 1, _ =>
            WriteVariantDictionary(body,
                ("text", () => WriteVariantString(body, "Old Harbor")),
                ("position", () => WriteVariantVector2(body, 512, 384)))));
        WriteVariantEntry(body, "paths", () => WriteVariantArray(body, 3, index =>
        {
            if (index == 0)
            {
                WriteVariantDictionary(body,
                    ("points", () => WriteVariantString(body, "[ Vector2( 100, 200 ), Vector2( 300, 400 ) ]")),
                    ("position", () => WriteVariantVector2(body, 10, 20)));
            }
            else
            {
                WriteVariantHeader(body, 0);
            }
        }));
        WriteVariantEntry(body, "territories", () =>
            WriteVariantDictionary(body,
                ("territories", () => WriteVariantArray(body, 2, index =>
                {
                    if (index == 0)
                    {
                        WriteVariantDictionary(body,
                            ("points", () => WriteVariantPoolVector2Array(
                                body,
                                (100, 100),
                                (200, 100),
                                (200, 200))));
                    }
                    else
                    {
                        WriteVariantHeader(body, 0);
                    }
                }))));
        WriteVariantEntry(body, "grid", () =>
        {
            WriteVariantHeader(body, 18);
            WriteUInt32(body, 0);
        });

        var variant = body.ToArray();
        using var raw = new MemoryStream();
        WriteUInt32(raw, checked((uint)variant.Length));
        raw.Write(variant);

        const uint blockSize = 64;
        var rawBytes = raw.ToArray();
        var blockCount = checked((int)(((uint)rawBytes.Length / blockSize) + 1));
        var blocks = new List<byte[]>(blockCount);
        for (var index = 0; index < blockCount; index++)
        {
            var start = checked((int)(index * blockSize));
            var count = Math.Min(checked((int)blockSize), Math.Max(0, rawBytes.Length - start));
            blocks.Add(EncodeFastLzLiteral(rawBytes.AsSpan(start, count)));
        }

        using var output = new MemoryStream();
        output.Write("GCPF"u8);
        WriteUInt32(output, 0);
        WriteUInt32(output, blockSize);
        WriteUInt32(output, checked((uint)rawBytes.Length));
        foreach (var block in blocks) WriteUInt32(output, checked((uint)block.Length));
        foreach (var block in blocks) output.Write(block);
        output.Write("GCPF"u8);
        return output.ToArray();
    }

    private static byte[] EncodeFastLzLiteral(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return [];
        using var output = new MemoryStream();
        for (var offset = 0; offset < data.Length; offset += 32)
        {
            var count = Math.Min(32, data.Length - offset);
            output.WriteByte((byte)(count - 1));
            output.Write(data.Slice(offset, count));
        }
        return output.ToArray();
    }

    private static void WriteVariantEntry(Stream stream, string key, Action value)
    {
        WriteVariantString(stream, key);
        value();
    }

    private static void WriteVariantArray(Stream stream, int count) =>
        WriteVariantArray(stream, count, _ => WriteVariantHeader(stream, 0));

    private static void WriteVariantArray(Stream stream, int count, Action<int> writeValue)
    {
        WriteVariantHeader(stream, 19);
        WriteUInt32(stream, checked((uint)count));
        for (var index = 0; index < count; index++) writeValue(index);
    }

    private static void WriteVariantDictionary(
        Stream stream,
        params (string Key, Action WriteValue)[] entries)
    {
        WriteVariantHeader(stream, 18);
        WriteUInt32(stream, checked((uint)entries.Length));
        foreach (var (key, writeValue) in entries)
        {
            WriteVariantEntry(stream, key, writeValue);
        }
    }

    private static void WriteVariantVector2(Stream stream, float x, float y)
    {
        WriteVariantHeader(stream, 5);
        WriteSingle(stream, x);
        WriteSingle(stream, y);
    }

    private static void WriteVariantPoolVector2Array(Stream stream, params (float X, float Y)[] points)
    {
        WriteVariantHeader(stream, 24);
        WriteUInt32(stream, checked((uint)points.Length));
        foreach (var point in points)
        {
            WriteSingle(stream, point.X);
            WriteSingle(stream, point.Y);
        }
    }

    private static void WriteSingle(Stream stream, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteVariantInteger(Stream stream, int value)
    {
        WriteVariantHeader(stream, 2);
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteVariantString(Stream stream, string value)
    {
        WriteVariantHeader(stream, 4);
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
        var padding = (4 - (bytes.Length % 4)) % 4;
        if (padding > 0) stream.Write(new byte[padding]);
    }

    private static void WriteVariantHeader(Stream stream, uint type) => WriteUInt32(stream, type);

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
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
