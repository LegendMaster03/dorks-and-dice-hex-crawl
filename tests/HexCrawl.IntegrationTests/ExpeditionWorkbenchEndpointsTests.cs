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

    [Fact]
    public async Task ExpeditionCollectionListsOwnerExpeditionsAcrossWorlds()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var firstWorld = await CreateWorld(client);
            var secondWorld = await CreateWorld(client);
            var firstWorldId = firstWorld.GetProperty("id").GetGuid();
            var secondWorldId = secondWorld.GetProperty("id").GetGuid();

            foreach (var (worldId, name) in new[] { (firstWorldId, "First crawl"), (secondWorldId, "Second crawl") })
            {
                using var startResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
                {
                    name,
                    procedureKey = "simple-fixed-distance",
                    presentationKey = "dm-controlled",
                    startHex = new { q = 0, r = 0 }
                });
                startResponse.EnsureSuccessStatusCode();
            }

            var expeditions = await client.GetFromJsonAsync<JsonElement>("/api/expeditions");
            Assert.Equal(2, expeditions.GetArrayLength());
            var worldIds = expeditions.EnumerateArray().Select(item => item.GetProperty("overworldId").GetGuid()).ToArray();
            Assert.Contains(firstWorldId, worldIds);
            Assert.Contains(secondWorldId, worldIds);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task FocusedAssistantsMutateOneExpeditionWithoutFullWorkbenchInputs()
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
                name = "Assistant expedition",
                procedureKey = "simple-fixed-distance",
                presentationKey = "dm-controlled",
                startHex = new { q = 0, r = 0 }
            });
            startResponse.EnsureSuccessStatusCode();
            var expedition = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
            var expeditionId = expedition.GetProperty("id").GetGuid();

            var context = expedition.GetProperty("context");
            Assert.Equal(worldId, context.GetProperty("id").GetGuid());
            Assert.Equal("Workbench API", context.GetProperty("name").GetString());
            Assert.Equal(12, context.GetProperty("hexCenterDistance").GetProperty("value").GetDouble());

            using var travelResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/travel",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    elapsedHours = 2,
                    distance = 6,
                    resultingHex = new { q = 1, r = 0 },
                    hexProgress = 2,
                    intendedDirection = 0,
                    completeWatch = true,
                    resolutionSource = "ManualRoll",
                    resolutionNote = "physical map"
                });
            travelResponse.EnsureSuccessStatusCode();
            expedition = await travelResponse.Content.ReadFromJsonAsync<JsonElement>();
            var travelState = expedition.GetProperty("expedition");
            Assert.Equal(6, travelState.GetProperty("distanceTraveled").GetProperty("value").GetDouble());
            Assert.Equal(2, travelState.GetProperty("elapsedTravelHours").GetDouble());
            Assert.Equal(1, travelState.GetProperty("completedWatches").GetInt32());
            Assert.Equal(1, travelState.GetProperty("currentHex").GetProperty("q").GetInt32());

            using var navigationResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/navigation",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    isLost = true,
                    veerSteps = 1,
                    intendedDirection = 0,
                    resolutionSource = "ExternalSystem",
                    resolutionNote = "other VTT"
                });
            navigationResponse.EnsureSuccessStatusCode();
            expedition = await navigationResponse.Content.ReadFromJsonAsync<JsonElement>();
            var navigationState = expedition.GetProperty("expedition");
            Assert.True(navigationState.GetProperty("isLost").GetBoolean());
            Assert.Equal(1, navigationState.GetProperty("veerSteps").GetInt32());
            Assert.Equal(6, navigationState.GetProperty("distanceTraveled").GetProperty("value").GetDouble());
            Assert.Equal(2, navigationState.GetProperty("elapsedTravelHours").GetDouble());

            using var encounterResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/encounters",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    outcome = "WanderingEncounter",
                    resolutionSource = "ManualRoll",
                    note = "table result"
                });
            encounterResponse.EnsureSuccessStatusCode();
            expedition = await encounterResponse.Content.ReadFromJsonAsync<JsonElement>();
            var encounterState = expedition.GetProperty("expedition");
            Assert.Equal(6, encounterState.GetProperty("distanceTraveled").GetProperty("value").GetDouble());
            Assert.Equal(2, encounterState.GetProperty("elapsedTravelHours").GetDouble());
            Assert.Contains(
                expedition.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "EncounterCheckPerformed");
            Assert.Contains(
                expedition.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "ResolutionProvenanceRecorded"
                    && item.GetProperty("message").GetString()!.Contains("encounter-assistant=ManualRoll", StringComparison.Ordinal));
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
