using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

public sealed class RuntimeEndpointsTests
{
    [Fact]
    public async Task ProcedurePresetsExposeAdvancedAndSimplifiedProcedures()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var presets = await client.GetFromJsonAsync<JsonElement>("/api/procedures/presets");

            var advancedPreset = Assert.Single(
                presets.EnumerateArray(),
                preset => preset.GetProperty("presetKey").GetString() == "alexandrian-advanced");
            var advanced = advancedPreset.GetProperty("procedure");
            Assert.Equal("alexandrian-advanced", advanced.GetProperty("key").GetString());
            Assert.True(advanced.GetProperty("isExecutable").GetBoolean());
            var advancedRuntime = advanced.GetProperty("runtime");
            Assert.True(advancedRuntime.GetProperty("usesNavigationChecks").GetBoolean());
            Assert.True(advancedRuntime.GetProperty("usesPersistentVeer").GetBoolean());
            var helpers = advancedRuntime.GetProperty("resolutionHelpers");
            Assert.Equal(2, helpers.GetProperty("travel").GetProperty("roll").GetProperty("diceCount").GetInt32());
            Assert.Equal(6, helpers.GetProperty("travel").GetProperty("roll").GetProperty("dieSides").GetInt32());
            Assert.Equal(3, helpers.GetProperty("travel").GetProperty("roll").GetProperty("modifier").GetInt32());
            Assert.Equal(0.1d, helpers.GetProperty("travel").GetProperty("distanceFactorPerRollPoint").GetDouble(), 12);
            Assert.Equal(20, helpers.GetProperty("navigation").GetProperty("checkRoll").GetProperty("dieSides").GetInt32());
            Assert.Equal(8, helpers.GetProperty("encounter").GetProperty("checkRoll").GetProperty("dieSides").GetInt32());
            Assert.Equal(8, helpers.GetProperty("encounter").GetProperty("timingSlots").GetInt32());

            var fixedPreset = Assert.Single(
                presets.EnumerateArray(),
                preset => preset.GetProperty("presetKey").GetString() == "simple-fixed-distance");
            Assert.Equal(
                "Fixed",
                fixedPreset.GetProperty("procedure").GetProperty("runtime").GetProperty("actualDistanceResolution").GetString());

            var hexStepPreset = Assert.Single(
                presets.EnumerateArray(),
                preset => preset.GetProperty("presetKey").GetString() == "simple-hex-step");
            Assert.Equal(
                "CellSteps",
                hexStepPreset.GetProperty("procedure").GetProperty("runtime").GetProperty("travelResolution").GetString());

            var oneRingPreset = Assert.Single(
                presets.EnumerateArray(),
                preset => preset.GetProperty("presetKey").GetString() == "the-one-ring-2e");
            Assert.Equal("role-driven-journey", oneRingPreset.GetProperty("procedure").GetProperty("key").GetString());
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
    public async Task CellAdvanceIsOwnerScopedVersionedAndDoesNotFabricateAxialState()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            long initialVersion;
            long advancedVersion;
            using (var ownerFactory = TestWebHost.Create(database, "alice"))
            using (var owner = ownerFactory.CreateClient())
            {
                var world = await CreateWorld(owner, "Cell travel authority");
                var id = world.GetProperty("id").GetGuid();
                var started = await StartCellAsync(owner, id, "Cell travel authority", 1, -1);
                expeditionId = started.GetProperty("id").GetGuid();
                initialVersion = started.GetProperty("version").GetInt64();

                var request = await AdvanceRequestAsync(owner, expeditionId, initialVersion);
                using var first = await owner.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/advance", request, CellJson);
                first.EnsureSuccessStatusCode();
                var advanced = await first.Content.ReadFromJsonAsync<JsonElement>();
                advancedVersion = advanced.GetProperty("version").GetInt64();
                Assert.True(advancedVersion > initialVersion);
                Assert.False(advanced.GetProperty("expedition").TryGetProperty("currentHex", out _));
                Assert.Equal(1, advanced.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
                Assert.Equal(0.25, advanced.GetProperty("expedition")
                    .GetProperty("distanceTraveled").GetProperty("value").GetDouble(), 6);

                using var stale = await owner.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/advance", request, CellJson);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

                var unchanged = await owner.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}");
                Assert.Equal(advancedVersion, unchanged.GetProperty("version").GetInt64());
                Assert.Equal(1, unchanged.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
            }

            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            {
                var foreignRequest = new AdvanceCellWatchRequest(
                    advancedVersion,
                    new CellWatchTravelPlan(new WorldPoint(1, 0), false, false,
                        TravelModeSelection.Normal, NavigationAidSelection.None),
                    Inputs(0.25));
                using var foreign = await other.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/advance",
                    foreignRequest, CellJson);
                Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            }

            using var restartedFactory = TestWebHost.Create(database, "alice");
            using var reopened = restartedFactory.CreateClient();
            var persisted = await reopened.GetFromJsonAsync<JsonElement>(
                $"/api/expeditions/{expeditionId:D}");
            Assert.Equal(advancedVersion, persisted.GetProperty("version").GetInt64());
            Assert.False(persisted.GetProperty("expedition").TryGetProperty("currentHex", out _));
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
    public async Task CellExpeditionPersistsAcrossApplicationRestartAndContinues()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            long savedVersion;
            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                var world = await CreateWorld(client, "Cell runtime restart");
                var started = await StartCellAsync(
                    client, world.GetProperty("id").GetGuid(), "Persistent cell expedition", 0, 0);
                expeditionId = started.GetProperty("id").GetGuid();
                var initialVersion = started.GetProperty("version").GetInt64();
                var input = await AdvanceRequestAsync(client, expeditionId, initialVersion);
                using var response = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/advance", input, CellJson);
                response.EnsureSuccessStatusCode();
                var completed = await response.Content.ReadFromJsonAsync<JsonElement>();
                savedVersion = completed.GetProperty("version").GetInt64();
                Assert.Equal(1, completed.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
                Assert.Equal(0.25, completed.GetProperty("expedition")
                    .GetProperty("distanceTraveled").GetProperty("value").GetDouble(), 6);
            }

            using var restartedFactory = TestWebHost.Create(database, "alice");
            using var restarted = restartedFactory.CreateClient();
            var before = await restarted.GetFromJsonAsync<JsonElement>(
                $"/api/expeditions/{expeditionId:D}");
            Assert.Equal(savedVersion, before.GetProperty("version").GetInt64());
            Assert.Equal(1, before.GetProperty("expedition").GetProperty("completedWatches").GetInt32());

            var next = await AdvanceRequestAsync(restarted, expeditionId, savedVersion);
            using var continuedResponse = await restarted.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/cells/advance", next, CellJson);
            continuedResponse.EnsureSuccessStatusCode();
            var continued = await continuedResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(continued.GetProperty("version").GetInt64() > savedVersion);
            Assert.Equal(2, continued.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
            Assert.Equal(0.5, continued.GetProperty("expedition")
                .GetProperty("distanceTraveled").GetProperty("value").GetDouble(), 6);
            Assert.NotEmpty(continued.GetProperty("history").EnumerateArray());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static readonly JsonSerializerOptions CellJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static CellWatchAdvanceInputs Inputs(double miles)
    {
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        return new CellWatchAdvanceInputs(
            ResolvedTravelAmount.Distance(
                new DistanceMeasure(miles, DistanceUnit.Miles),
                new DistanceMeasure(miles, DistanceUnit.Miles), source),
            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, source),
            ResolvedEncounter.None);
    }

    private static async Task<AdvanceCellWatchRequest> AdvanceRequestAsync(
        HttpClient client, Guid expeditionId, long version)
    {
        var topology = await client.GetFromJsonAsync<JsonElement>(
            $"/api/expeditions/{expeditionId:D}/cell-topology");
        var center = topology.GetProperty("center");
        var boundary = topology.GetProperty("interfaces")[0];
        var midpointX = (boundary.GetProperty("start").GetProperty("x").GetDouble()
                         + boundary.GetProperty("end").GetProperty("x").GetDouble()) / 2;
        var midpointY = (boundary.GetProperty("start").GetProperty("y").GetDouble()
                         + boundary.GetProperty("end").GetProperty("y").GetDouble()) / 2;
        var heading = new WorldPoint(
            midpointX - center.GetProperty("x").GetDouble(),
            midpointY - center.GetProperty("y").GetDouble());
        return new AdvanceCellWatchRequest(
            version,
            new CellWatchTravelPlan(heading, false, false,
                TravelModeSelection.Normal, NavigationAidSelection.None),
            Inputs(0.25));
    }

    private static async Task<JsonElement> StartCellAsync(
        HttpClient client, Guid worldId, string name, long u, long v)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/overworlds/{worldId:D}/cell-expeditions",
            new
            {
                name,
                presetKey = "simple-fixed-distance",
                presentationKey = "dm-controlled",
                startCell = new { motifCellId = "hex", translation = new { u, v } }
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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
