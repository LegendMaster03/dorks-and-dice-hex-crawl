using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class ExpeditionWorkbenchEndpointsTests
{
    [Fact]
    public async Task PresentationCatalogExposesFourBuiltInPolicies()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var presets = await client.GetFromJsonAsync<JsonElement>("/api/presentation/presets");

            Assert.Equal(4, presets.GetArrayLength());
            var keys = presets.EnumerateArray().Select(item => item.GetProperty("key").GetString()).ToArray();
            Assert.Contains("traditional-hidden-hexcrawl", keys);
            Assert.Contains("exploration-map", keys);
            Assert.Contains("open-regional-map", keys);
            Assert.Contains("dm-controlled", keys);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task CustomizedExpeditionContractReturnsProcedurePresentationAndKnownHexState()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client);
            var worldId = world.GetProperty("id").GetGuid();

            using var startResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
            {
                name = "Configured expedition",
                procedureKey = "simple-fixed-distance",
                presentationKey = "exploration-map",
                startHex = new { q = 3, r = -2 },
                procedureSnapshot = new
                {
                    key = "simple-fixed-distance",
                    name = "House six-hour watch",
                    watchHours = 6,
                    travelResolution = "ContinuousDistance",
                    actualDistanceResolution = "Fixed",
                    encounterCadence = "PerDay",
                    usesNavigationChecks = false,
                    usesPersistentVeer = false,
                    tracksIntraHexProgress = true,
                    directionChangesCostProgress = false,
                    supportsDeliberateDoubleBack = false,
                    startingExitProgressFactor = 0.5,
                    nearExitProgressFactor = 0.5,
                    farExitProgressFactor = 1.0,
                    backExitProgressFactor = 0.5,
                    directionChangeProgressCostFactor = 0.0
                }
            });
            startResponse.EnsureSuccessStatusCode();
            var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal("House six-hour watch", started.GetProperty("profile").GetProperty("name").GetString());
            Assert.Equal(6, started.GetProperty("profile").GetProperty("watchHours").GetDouble());
            Assert.Equal("PerDay", started.GetProperty("profile").GetProperty("encounterCadence").GetString());
            Assert.Equal("exploration-map", started.GetProperty("presentation").GetProperty("key").GetString());
            var knownHex = Assert.Single(started.GetProperty("knownHexes").EnumerateArray());
            Assert.Equal(3, knownHex.GetProperty("q").GetInt32());
            Assert.Equal(-2, knownHex.GetProperty("r").GetInt32());
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
            name = "Workbench API",
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
