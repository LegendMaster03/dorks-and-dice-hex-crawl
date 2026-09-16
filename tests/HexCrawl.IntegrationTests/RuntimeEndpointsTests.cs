using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class RuntimeEndpointsTests
{
    [Fact]
    public async Task RuntimeProfilesExposeAdvancedAndSimplifiedProcedures()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var profiles = await client.GetFromJsonAsync<JsonElement>("/api/runtime/profiles");
            Assert.Equal(3, profiles.GetArrayLength());
            Assert.Contains(profiles.EnumerateArray(), profile => profile.GetProperty("key").GetString() == "alexandrian-advanced");
            Assert.Contains(profiles.EnumerateArray(), profile => profile.GetProperty("key").GetString() == "simple-fixed-distance");
            Assert.Contains(profiles.EnumerateArray(), profile => profile.GetProperty("key").GetString() == "simple-hex-step");
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task PersistentEndpointsRequireTrustedOrExplicitDevelopmentIdentity()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, null);
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/api/overworlds");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task OwnerCanCreateWorldButOtherUserCanNotEnumerateOrOpenIt()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            using (var ownerFactory = TestWebHost.Create(database, "alice"))
            using (var ownerClient = ownerFactory.CreateClient())
            {
                var world = await CreateWorld(ownerClient, "Alice world");
                worldId = world.GetProperty("id").GetGuid();
                var worlds = await ownerClient.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Single(worlds.EnumerateArray());
            }

            using var otherFactory = TestWebHost.Create(database, "bob");
            using var otherClient = otherFactory.CreateClient();
            var otherWorlds = await otherClient.GetFromJsonAsync<JsonElement>("/api/overworlds");
            Assert.Empty(otherWorlds.EnumerateArray());
            using var forbiddenByNonEnumeration = await otherClient.GetAsync($"/api/overworlds/{worldId:D}");
            Assert.Equal(HttpStatusCode.NotFound, forbiddenByNonEnumeration.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task GeometryAndOptimisticConcurrencyFlowThroughHttpApi()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client, "Geometry");
            var worldId = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();

            using var lineResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/features", new
            {
                name = "River",
                category = "river",
                kind = "Line",
                position = (object?)null,
                path = new[] { new { x = 0d, y = 0d }, new { x = 1.5d, y = 2d }, new { x = 3d, y = 1d } },
                boundary = (object?)null,
                expectedVersion = version
            });
            lineResponse.EnsureSuccessStatusCode();
            var changed = await lineResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, changed.GetProperty("features")[0].GetProperty("path").GetArrayLength());

            using var stale = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/locations", new
            {
                name = "Stale",
                category = "site",
                position = new { x = 0, y = 0 },
                discoverability = "Obvious",
                expectedVersion = version
            });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ExpeditionStateSurvivesApplicationRestartAndContinues()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            long savedVersion;
            using (var firstFactory = TestWebHost.Create(database))
            using (var firstClient = firstFactory.CreateClient())
            {
                var world = await CreateWorld(firstClient, "Runtime");
                var worldId = world.GetProperty("id").GetGuid();
                using var startResponse = await firstClient.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
                {
                    name = "Persistent expedition",
                    procedureKey = "simple-fixed-distance",
                    startHex = new { q = 0, r = 0 }
                });
                startResponse.EnsureSuccessStatusCode();
                var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();
                var version = started.GetProperty("version").GetInt64();

                using var advanceResponse = await firstClient.PostAsJsonAsync($"/api/expeditions/{expeditionId:D}/advance", new
                {
                    expectedVersion = version,
                    intendedDirection = 0,
                    paceKey = "normal",
                    activities = Array.Empty<string>(),
                    navigationAidKey = "none",
                    expectedDistance = 12,
                    actualDistance = 12,
                    resolutionSource = "ManualRoll",
                    deliberateDoubleBack = false,
                    continueAcrossBoundaries = false
                });
                advanceResponse.EnsureSuccessStatusCode();
                var advanced = await advanceResponse.Content.ReadFromJsonAsync<JsonElement>();
                savedVersion = advanced.GetProperty("version").GetInt64();
                Assert.Equal("ConditionsReviewRequired", advanced.GetProperty("pauseReason").GetString());
                Assert.Equal(2, advanced.GetProperty("remainingWatchHours").GetDouble());
            }

            using var restartedFactory = TestWebHost.Create(database);
            using var restartedClient = restartedFactory.CreateClient();
            var loaded = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(savedVersion, loaded.GetProperty("version").GetInt64());
            Assert.Equal(6, loaded.GetProperty("expedition").GetProperty("distanceTraveled").GetProperty("value").GetDouble());
            Assert.True(loaded.GetProperty("history").GetArrayLength() > 0);

            using var continueResponse = await restartedClient.PostAsJsonAsync($"/api/expeditions/{expeditionId:D}/advance", new
            {
                expectedVersion = savedVersion,
                intendedDirection = 0,
                paceKey = "normal",
                activities = Array.Empty<string>(),
                navigationAidKey = "none",
                expectedDistance = 6,
                actualDistance = 6,
                resolutionSource = "ManualRoll",
                deliberateDoubleBack = false,
                continueAcrossBoundaries = true
            });
            continueResponse.EnsureSuccessStatusCode();
            var continued = await continueResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(continued.GetProperty("version").GetInt64() > savedVersion);
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
}
