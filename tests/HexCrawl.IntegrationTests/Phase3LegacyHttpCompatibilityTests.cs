using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class Phase3LegacyHttpCompatibilityTests
{
    [Fact]
    public async Task StandaloneGenericOnlyPresetIsRejectedBeforePersistence()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/expeditions", new
            {
                name = "Generic-only journey",
                procedureKey = CrawlProcedureCatalog.OneRing2ePresetKey,
                context = new
                {
                    kind = "NonSpatial",
                    name = "Journey"
                }
            });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("generic-only", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

            var expeditions = await client.GetFromJsonAsync<JsonElement>("/api/expeditions");
            Assert.Equal(0, expeditions.GetArrayLength());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task WorldBoundGenericOnlyPresetIsRejectedBeforePersistence()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client);
            var worldId = world.GetProperty("id").GetGuid();

            using var response = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
            {
                name = "Generic-only journey",
                procedureKey = CrawlProcedureCatalog.OneRing2ePresetKey,
                presentationKey = "exploration-map",
                startHex = new { q = 0, r = 0 }
            });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("generic-only", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

            var expeditions = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}/expeditions");
            Assert.Equal(0, expeditions.GetArrayLength());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ExistingGenericOnlyExpeditionReturnsDeliberateConflictOnLegacyDetailGet()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var store = new PostgresHexCrawlStore(database);
            await store.InitializeAsync();
            var sessions = new CrawlSessionService(store);
            var seeded = await sessions.StartAsync(
                "integration-user",
                new StartStandaloneCrawlSessionCommand(
                    "Seeded generic-only journey",
                    CrawlProcedureCatalog.OneRing2ePresetKey,
                    new NonSpatialCrawlSessionContext("Journey")));

            Assert.Null(seeded.CompatibilityProfile);
            Assert.NotNull(seeded.CampaignProcedure);

            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.GetAsync($"/api/expeditions/{seeded.Id:D}");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("generic-only", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CampaignProcedure", error.GetProperty("error").GetString(), StringComparison.Ordinal);

            var expeditions = await client.GetFromJsonAsync<JsonElement>("/api/expeditions");
            Assert.Single(expeditions.EnumerateArray());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> CreateWorld(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/overworlds", new
        {
            name = "Phase 3 HTTP boundary",
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
