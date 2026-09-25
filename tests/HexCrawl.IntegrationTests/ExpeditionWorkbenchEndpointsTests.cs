using System.Net;
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
            var worldIds = expeditions.EnumerateArray()
                .Select(item => item.GetProperty("context").GetProperty("overworldId").GetGuid())
                .ToArray();
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
            Assert.Equal("WorldBound", context.GetProperty("kind").GetString());
            Assert.Equal(worldId, context.GetProperty("overworldId").GetGuid());
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

    [Fact]
    public async Task FocusedEncounterResolutionPreventsDuplicateWorkbenchCheck()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client);
            var worldId = world.GetProperty("id").GetGuid();

            using var startResponse = await client.PostAsJsonAsync(
                $"/api/overworlds/{worldId:D}/expeditions",
                new
                {
                    name = "Shared encounter resolution",
                    procedureKey = "simple-fixed-distance",
                    presentationKey = "exploration-map",
                    startHex = new { q = 0, r = 0 },
                    procedureSnapshot = new
                    {
                        key = "simple-fixed-distance",
                        name = "Per-watch shared encounter state",
                        watchHours = 4,
                        travelResolution = "ContinuousDistance",
                        actualDistanceResolution = "Fixed",
                        encounterCadence = "PerWatch",
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
            var expedition = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
            var expeditionId = expedition.GetProperty("id").GetGuid();

            using var firstWatch = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/advance",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    intendedDirection = 0,
                    paceKey = "normal",
                    activities = Array.Empty<string>(),
                    navigationAidKey = "none",
                    suppressesNavigationCheck = false,
                    resetsVeerAtBoundary = false,
                    effectiveDistance = 1,
                    encounterOutcome = "None",
                    encounterResolutionSource = "ManualRoll",
                    resolutionSource = "ProcedureDefault",
                    deliberateDoubleBack = false,
                    continueAcrossBoundaries = true
                });
            firstWatch.EnsureSuccessStatusCode();
            expedition = await firstWatch.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, expedition.GetProperty("expedition").GetProperty("completedWatches").GetInt32());

            using var focusedEncounter = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/encounters",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    outcome = "WanderingEncounter",
                    resolutionSource = "ManualRoll",
                    note = "QA cadence result"
                });
            focusedEncounter.EnsureSuccessStatusCode();
            expedition = await focusedEncounter.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(
                expedition.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "EncounterCheckPerformed"
                    && item.GetProperty("watchNumber").GetInt32() == 2);

            using var secondWatch = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/advance",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    intendedDirection = 0,
                    paceKey = "normal",
                    activities = Array.Empty<string>(),
                    navigationAidKey = "none",
                    suppressesNavigationCheck = false,
                    resetsVeerAtBoundary = false,
                    effectiveDistance = 1,
                    resolutionSource = "ProcedureDefault",
                    deliberateDoubleBack = false,
                    continueAcrossBoundaries = true
                });
            secondWatch.EnsureSuccessStatusCode();
            expedition = await secondWatch.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(2, expedition.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
            Assert.Single(
                expedition.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "EncounterCheckPerformed"
                    && item.GetProperty("watchNumber").GetInt32() == 2);
            Assert.DoesNotContain(
                expedition.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "EncounterCheckPerformed"
                    && item.GetProperty("watchNumber").GetInt32() == 2
                    && item.GetProperty("message").GetString()!.Contains("resolved as None", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task FocusedNavigationResolutionPreventsDuplicateWorkbenchCheck()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client);
            var worldId = world.GetProperty("id").GetGuid();

            using var startResponse = await client.PostAsJsonAsync(
                $"/api/overworlds/{worldId:D}/expeditions",
                new
                {
                    name = "Shared navigation resolution",
                    procedureKey = "simple-fixed-distance",
                    presentationKey = "exploration-map",
                    startHex = new { q = 0, r = 0 },
                    procedureSnapshot = new
                    {
                        key = "simple-fixed-distance",
                        name = "Navigation handoff",
                        watchHours = 4,
                        travelResolution = "ContinuousDistance",
                        actualDistanceResolution = "Fixed",
                        encounterCadence = "None",
                        usesNavigationChecks = true,
                        usesPersistentVeer = true,
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
            var expedition = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
            var expeditionId = expedition.GetProperty("id").GetGuid();

            using var focusedNavigation = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/navigation",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    isLost = true,
                    veerSteps = 1,
                    intendedDirection = 0,
                    resolutionSource = "ManualRoll",
                    note = "QA navigation result"
                });
            focusedNavigation.EnsureSuccessStatusCode();
            expedition = await focusedNavigation.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(
                expedition.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "NavigationCheckResolved"
                    && item.GetProperty("watchNumber").GetInt32() == 1);

            using var watchResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/advance",
                new
                {
                    expectedVersion = expedition.GetProperty("version").GetInt64(),
                    intendedDirection = 0,
                    paceKey = "normal",
                    activities = Array.Empty<string>(),
                    navigationAidKey = "none",
                    suppressesNavigationCheck = false,
                    resetsVeerAtBoundary = false,
                    effectiveDistance = 1,
                    resolutionSource = "ProcedureDefault",
                    deliberateDoubleBack = false,
                    continueAcrossBoundaries = true
                });
            watchResponse.EnsureSuccessStatusCode();
            expedition = await watchResponse.Content.ReadFromJsonAsync<JsonElement>();

            var state = expedition.GetProperty("expedition");
            Assert.Equal(1, state.GetProperty("completedWatches").GetInt32());
            Assert.True(state.GetProperty("isLost").GetBoolean());
            Assert.Equal(1, state.GetProperty("veerSteps").GetInt32());
            Assert.Equal(1, state.GetProperty("actualDirection").GetInt32());
            Assert.Single(
                expedition.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "NavigationCheckResolved"
                    && item.GetProperty("watchNumber").GetInt32() == 1);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task AbstractHexSessionPersistsAndAdvancesWithoutAnyOverworld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var worldsBefore = await client.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Equal(0, worldsBefore.GetArrayLength());

                using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
                {
                    name = "Paper map crawl",
                    procedureKey = "simple-fixed-distance",
                    context = new
                    {
                        kind = "AbstractHex",
                        name = "Paper map",
                        orientation = "FlatTop",
                        hexCenterDistance = 12,
                        distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
                    },
                    startHex = new { q = 2, r = -1 }
                });
                startResponse.EnsureSuccessStatusCode();
                var session = await startResponse.Content.ReadFromJsonAsync<JsonElement>();

                Assert.Equal(JsonValueKind.Null, session.GetProperty("overworldId").ValueKind);
                Assert.Equal("AbstractHex", session.GetProperty("context").GetProperty("kind").GetString());
                Assert.Equal("Paper map", session.GetProperty("context").GetProperty("name").GetString());
                Assert.Equal("FlatTop", session.GetProperty("context").GetProperty("orientation").GetString());
                Assert.True(session.GetProperty("expedition").GetProperty("isSpatial").GetBoolean());
                Assert.Equal(2, session.GetProperty("expedition").GetProperty("currentHex").GetProperty("q").GetInt32());

                var sessionId = session.GetProperty("id").GetGuid();
                using var advanceResponse = await client.PostAsJsonAsync($"/api/expeditions/{sessionId:D}/advance", new
                {
                    expectedVersion = session.GetProperty("version").GetInt64(),
                    intendedDirection = 0,
                    paceKey = "normal",
                    activities = Array.Empty<string>(),
                    navigationAidKey = "none",
                    suppressesNavigationCheck = false,
                    resetsVeerAtBoundary = false,
                    effectiveDistance = 3,
                    resolutionSource = "ManualRoll",
                    deliberateDoubleBack = false,
                    continueAcrossBoundaries = true
                });
                advanceResponse.EnsureSuccessStatusCode();
                session = await advanceResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(3, session.GetProperty("expedition").GetProperty("distanceTraveled").GetProperty("value").GetDouble());

                var worldsAfter = await client.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Equal(0, worldsAfter.GetArrayLength());
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restartedClient = restartedFactory.CreateClient())
            {
                var sessions = await restartedClient.GetFromJsonAsync<JsonElement>("/api/expeditions");
                var summary = Assert.Single(sessions.EnumerateArray());
                Assert.Equal("AbstractHex", summary.GetProperty("context").GetProperty("kind").GetString());
                Assert.Equal(JsonValueKind.Null, summary.GetProperty("context").GetProperty("overworldId").ValueKind);
                var worlds = await restartedClient.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Equal(0, worlds.GetArrayLength());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task AbstractHexSessionRejectsMissingDistanceUnit()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/expeditions", new
            {
                name = "Unitless crawl",
                procedureKey = "simple-fixed-distance",
                context = new
                {
                    kind = "AbstractHex",
                    name = "Unitless paper map",
                    orientation = "PointyTop",
                    hexCenterDistance = 12
                },
                startHex = new { q = 0, r = 0 }
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task NonSpatialSessionTracksPartialWatchAcrossRestartWithoutFakeSpatialState()
    {
        var database = TestWebHost.NewDatabasePath();
        Guid sessionId;
        double watchHours;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
                {
                    name = "Procedure clock",
                    procedureKey = "alexandrian-advanced",
                    context = new
                    {
                        kind = "NonSpatial",
                        name = "Procedure only"
                    }
                });
                startResponse.EnsureSuccessStatusCode();
                var session = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                sessionId = session.GetProperty("id").GetGuid();
                watchHours = session.GetProperty("profile").GetProperty("watchHours").GetDouble();

                Assert.Equal(JsonValueKind.Null, session.GetProperty("overworldId").ValueKind);
                Assert.Equal("NonSpatial", session.GetProperty("context").GetProperty("kind").GetString());
                var runtime = session.GetProperty("expedition");
                Assert.False(runtime.GetProperty("isSpatial").GetBoolean());
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("currentHex").ValueKind);
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("distanceTraveled").ValueKind);
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("activeWatchNumber").ValueKind);

                using var watchResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/assistants/watch",
                    new
                    {
                        expectedVersion = session.GetProperty("version").GetInt64(),
                        elapsedHours = 1,
                        resolutionSource = "ProcedureDefault",
                        resolutionNote = "table clock",
                        note = "first segment"
                    });
                watchResponse.EnsureSuccessStatusCode();
                session = await watchResponse.Content.ReadFromJsonAsync<JsonElement>();
                runtime = session.GetProperty("expedition");

                Assert.Equal(1, runtime.GetProperty("activeWatchNumber").GetInt32());
                Assert.Equal(watchHours, runtime.GetProperty("activeWatchTotalHours").GetDouble());
                Assert.Equal(1, runtime.GetProperty("activeWatchElapsedHours").GetDouble());
                Assert.Equal(watchHours - 1, runtime.GetProperty("activeWatchRemainingHours").GetDouble(), 6);
                Assert.Equal(watchHours - 1, session.GetProperty("remainingWatchHours").GetDouble(), 6);
                Assert.Equal(0, runtime.GetProperty("completedWatches").GetInt32());
                Assert.Equal(1, runtime.GetProperty("elapsedTravelHours").GetDouble());
                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "WatchStarted"
                        && item.GetProperty("hex").ValueKind == JsonValueKind.Null);
                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "WatchTimeAdvanced"
                        && item.GetProperty("watchNumber").GetInt32() == 1);

                using var encounterResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/assistants/encounters",
                    new
                    {
                        expectedVersion = session.GetProperty("version").GetInt64(),
                        outcome = "WanderingEncounter",
                        resolutionSource = "ManualRoll",
                        note = "procedure-only check"
                    });
                encounterResponse.EnsureSuccessStatusCode();
                session = await encounterResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "EncounterCheckPerformed"
                        && item.GetProperty("watchNumber").GetInt32() == 1
                        && item.GetProperty("hex").ValueKind == JsonValueKind.Null);

                var worlds = await client.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Equal(0, worlds.GetArrayLength());
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restartedClient = restartedFactory.CreateClient())
            {
                var session = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/expeditions/{sessionId:D}");
                var runtime = session.GetProperty("expedition");
                Assert.Equal(1, runtime.GetProperty("activeWatchNumber").GetInt32());
                Assert.Equal(1, runtime.GetProperty("activeWatchElapsedHours").GetDouble());
                Assert.Equal(watchHours - 1, runtime.GetProperty("activeWatchRemainingHours").GetDouble(), 6);

                using var resumeResponse = await restartedClient.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/assistants/watch",
                    new
                    {
                        expectedVersion = session.GetProperty("version").GetInt64(),
                        elapsedHours = watchHours - 1,
                        resolutionSource = "DmOverride",
                        resolutionNote = "session ruling",
                        note = "finish the watch"
                    });
                resumeResponse.EnsureSuccessStatusCode();
                session = await resumeResponse.Content.ReadFromJsonAsync<JsonElement>();
                runtime = session.GetProperty("expedition");

                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("activeWatchNumber").ValueKind);
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("activeWatchElapsedHours").ValueKind);
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("activeWatchRemainingHours").ValueKind);
                Assert.Equal(1, runtime.GetProperty("completedWatches").GetInt32());
                Assert.Equal(watchHours, runtime.GetProperty("elapsedTravelHours").GetDouble(), 6);
                Assert.Equal(0, session.GetProperty("remainingWatchHours").GetDouble());

                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "WatchCompleted"
                        && item.GetProperty("watchNumber").GetInt32() == 1);
                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "DmOverrideApplied"
                        && item.GetProperty("message").GetString()!.Contains("finish the watch", StringComparison.Ordinal));
                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "ResolutionProvenanceRecorded"
                        && item.GetProperty("message").GetString()!.Contains("watch-assistant=DmOverride", StringComparison.Ordinal));

                using var overrunResponse = await restartedClient.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/assistants/watch",
                    new
                    {
                        expectedVersion = session.GetProperty("version").GetInt64(),
                        elapsedHours = watchHours + 1,
                        resolutionSource = "ProcedureDefault"
                    });
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, overrunResponse.StatusCode);

                var afterRejected = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/expeditions/{sessionId:D}");
                Assert.Equal(1, afterRejected.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
                Assert.Equal(watchHours, afterRejected.GetProperty("expedition").GetProperty("elapsedTravelHours").GetDouble(), 6);

                var worlds = await restartedClient.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Equal(0, worlds.GetArrayLength());
            }
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
