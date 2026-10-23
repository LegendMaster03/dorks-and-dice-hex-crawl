using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class PlayerMapPreviewEndpointsTests
{
    [Fact]
    public async Task OwnerCanReleaseGeometryWithoutSecretNamesOrUnselectedFeatures()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "Owner's world",
                tilingDsSymbol = "<1:1,1,1:4,4>",
                worldUnitsPerAbstractPeriod = 1.0,
                origin = new { x = 0.0, y = 0.0 },
                playableBounds = new { width = 5, height = 5 }
            });
            created.EnsureSuccessStatusCode();
            var world = await created.Content.ReadFromJsonAsync<JsonElement>();
            var id = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();

            async Task<JsonElement> AddFeature(string name)
            {
                using var response = await client.PostAsJsonAsync($"/api/overworlds/{id:D}/features", new
                {
                    name,
                    category = "secret-category",
                    kind = "Point",
                    position = new { x = 0.0, y = 0.0 },
                    expectedVersion = version
                });
                response.EnsureSuccessStatusCode();
                var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
                version = updated.GetProperty("version").GetInt64();
                return updated.GetProperty("features").EnumerateArray()
                    .Single(item => item.GetProperty("name").GetString() == name);
            }
            var selected = await AddFeature("Visible shape, hidden name");
            _ = await AddFeature("Entirely secret feature");

            using var initially = await client.GetAsync(
                $"/api/overworlds/{id:D}/player-map-preview");
            initially.EnsureSuccessStatusCode();
            var blank = await initially.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(blank.GetProperty("world").GetProperty("features").EnumerateArray());

            using var released = await client.PutAsJsonAsync(
                $"/api/overworlds/{id:D}/player-map-disclosure", new
                {
                    expectedVersion = version,
                    disclosure = new
                    {
                        visibleFeatureIds = new[] { selected.GetProperty("id").GetGuid() },
                        visibleLocationIds = Array.Empty<Guid>(),
                        visibleFeatureLabelIds = Array.Empty<Guid>(),
                        visibleLocationLabelIds = Array.Empty<Guid>(),
                        visibleSourceMapIds = Array.Empty<Guid>(),
                        showTerrain = false,
                        showGrid = true
                    }
                });
            released.EnsureSuccessStatusCode();
            var updatedWorld = await released.Content.ReadFromJsonAsync<JsonElement>();
            version = updatedWorld.GetProperty("version").GetInt64();
            using var previewResponse = await client.GetAsync(
                $"/api/overworlds/{id:D}/player-map-preview");
            previewResponse.EnsureSuccessStatusCode();
            var body = await previewResponse.Content.ReadAsStringAsync();
            var preview = JsonDocument.Parse(body).RootElement;
            var feature = Assert.Single(preview.GetProperty("world").GetProperty("features").EnumerateArray());
            Assert.Equal(selected.GetProperty("id").GetGuid(), feature.GetProperty("id").GetGuid());
            Assert.Equal("Unlabeled", feature.GetProperty("name").GetString());
            Assert.Equal("unlabeled", feature.GetProperty("category").GetString());
            Assert.Empty(preview.GetProperty("featureLabelIds").EnumerateArray());
            Assert.Empty(preview.GetProperty("terrainAnnotations").EnumerateArray());
            Assert.DoesNotContain("Visible shape, hidden name", body);
            Assert.DoesNotContain("Entirely secret feature", body);
            Assert.DoesNotContain("secret-category", body);
            Assert.Empty(preview.GetProperty("world").GetProperty("playerMapDisclosure")
                .GetProperty("visibleFeatureIds").EnumerateArray());

            using var strangerHost = TestWebHost.Create(database, "bob");
            using var stranger = strangerHost.CreateClient();
            using var denied = await stranger.GetAsync($"/api/overworlds/{id:D}/player-map-preview");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task AppearanceAndPlayerDisclosureSurviveWorldReloadAndHostRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid id;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Map state persistence",
                    tilingDsSymbol = "<1:1,1,1:4,4>",
                    worldUnitsPerAbstractPeriod = 1.0,
                    origin = new { x = 0.0, y = 0.0 },
                    playableBounds = new { width = 5, height = 5 }
                });
                created.EnsureSuccessStatusCode();
                var world = await created.Content.ReadFromJsonAsync<JsonElement>();
                id = world.GetProperty("id").GetGuid();
                var version = world.GetProperty("version").GetInt64();

                using var styled = await client.PutAsJsonAsync(
                    $"/api/overworlds/{id:D}/appearance", new
                    {
                        expectedVersion = version,
                        appearance = new
                        {
                            terrainColors = new Dictionary<string, string> { ["forest"] = "#AABBCC" },
                            featureColors = new Dictionary<string, string>(),
                            labelColors = new Dictionary<string, string>()
                        }
                    });
                styled.EnsureSuccessStatusCode();
                var changed = await styled.Content.ReadFromJsonAsync<JsonElement>();
                version = changed.GetProperty("version").GetInt64();

                using var disclosed = await client.PutAsJsonAsync(
                    $"/api/overworlds/{id:D}/player-map-disclosure", new
                    {
                        expectedVersion = version,
                        disclosure = new
                        {
                            visibleFeatureIds = Array.Empty<Guid>(),
                            visibleLocationIds = Array.Empty<Guid>(),
                            visibleFeatureLabelIds = Array.Empty<Guid>(),
                            visibleLocationLabelIds = Array.Empty<Guid>(),
                            visibleSourceMapIds = Array.Empty<Guid>(),
                            showTerrain = true,
                            showGrid = false
                        }
                    });
                disclosed.EnsureSuccessStatusCode();
            }

            // A new application host must reconstruct both fields from the
            // persisted world snapshot rather than using the previous instance.
            using var restarted = TestWebHost.Create(database, "alice");
            using var reader = restarted.CreateClient();
            using var response = await reader.GetAsync($"/api/overworlds/{id:D}");
            response.EnsureSuccessStatusCode();
            var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("#AABBCC", saved.GetProperty("mapAppearance")
                .GetProperty("terrainColors").GetProperty("forest").GetString());
            Assert.True(saved.GetProperty("playerMapDisclosure").GetProperty("showTerrain").GetBoolean());
            Assert.False(saved.GetProperty("playerMapDisclosure").GetProperty("showGrid").GetBoolean());
            using var previewResponse = await reader.GetAsync($"/api/overworlds/{id:D}/player-map-preview");
            previewResponse.EnsureSuccessStatusCode();
            var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(preview.GetProperty("showGrid").GetBoolean());
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task DisclosureRejectsUnknownSubjectIds()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "World",
                tilingDsSymbol = "<1:1,1,1:4,4>",
                worldUnitsPerAbstractPeriod = 1.0,
                origin = new { x = 0.0, y = 0.0 }
            });
            created.EnsureSuccessStatusCode();
            var world = await created.Content.ReadFromJsonAsync<JsonElement>();
            using var invalid = await client.PutAsJsonAsync(
                $"/api/overworlds/{world.GetProperty("id").GetGuid():D}/player-map-disclosure", new
                {
                    expectedVersion = world.GetProperty("version").GetInt64(),
                    disclosure = new { visibleFeatureIds = new[] { Guid.NewGuid() } }
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
