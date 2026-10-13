using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

public sealed class ExpeditionWorkbenchEndpointsTests
{
    private static readonly JsonSerializerOptions CellJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

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
    public async Task CellExpeditionContractReturnsPinnedProcedurePresentationAndQualifiedKnowledge()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client);
            var worldId = world.GetProperty("id").GetGuid();

            using var startResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/cell-expeditions", new
            {
                name = "Configured expedition",
                presetKey = "simple-fixed-distance",
                presentationKey = "exploration-map",
                startCell = new { motifCellId = "face-0", translation = new { u = 3L, v = -2L } }
            });
            startResponse.EnsureSuccessStatusCode();
            var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();

            var procedure = started.GetProperty("procedure");
            Assert.Equal("simple-fixed-distance", procedure.GetProperty("key").GetString());
            Assert.Equal("Simple Fixed Distance", procedure.GetProperty("name").GetString());
            Assert.True(procedure.GetProperty("isExecutable").GetBoolean());
            Assert.Equal(4, procedure.GetProperty("runtime").GetProperty("intervalHours").GetDouble());
            Assert.Equal("None", procedure.GetProperty("runtime").GetProperty("encounterCadence").GetString());
            Assert.Equal("exploration-map", started.GetProperty("presentation").GetProperty("key").GetString());
            var knownCell = Assert.Single(started.GetProperty("knownCells").EnumerateArray());
            Assert.Equal("face-0", knownCell.GetProperty("address").GetProperty("motifCellId").GetString());
            Assert.Equal(3L, knownCell.GetProperty("address").GetProperty("translation").GetProperty("u").GetInt64());
            Assert.Equal(-2L, knownCell.GetProperty("address").GetProperty("translation").GetProperty("v").GetInt64());
            Assert.Equal(knownCell.GetRawText(),
                started.GetProperty("expedition").GetProperty("currentCell").GetRawText());
            Assert.False(started.TryGetProperty("knownHexes", out _));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task DmCanRepositionToQualifiedCellWithoutRecordingTravel()
    {
        var database = TestWebHost.NewDatabasePath();
        Guid expeditionId;
        Guid worldId;
        string targetCell;
        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var world = await CreateWorld(client);
                worldId = world.GetProperty("id").GetGuid();

                using var startResponse = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions",
                    new
                    {
                        name = "Reposition cell expedition",
                        presetKey = "simple-fixed-distance",
                        presentationKey = "exploration-map",
                        startCell = new { motifCellId = "face-0", translation = new { u = 0L, v = 0L } }
                    });
                startResponse.EnsureSuccessStatusCode();
                var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();

                var destination = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/overworlds/{worldId:D}/cells/resolve?motifCellId=face-0&u=7&v=-4");
                var cellId = destination.GetProperty("id");
                targetCell = cellId.GetRawText();
                using var moveResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/reposition",
                    new
                    {
                        expectedVersion = started.GetProperty("version").GetInt64(),
                        targetCell = cellId,
                        position = destination.GetProperty("center"),
                        note = "Teleportation test."
                    });
                moveResponse.EnsureSuccessStatusCode();
                var moved = await moveResponse.Content.ReadFromJsonAsync<JsonElement>();
                var state = moved.GetProperty("expedition");

                Assert.Equal(targetCell, state.GetProperty("currentCell").GetRawText());
                Assert.False(state.TryGetProperty("currentHex", out _));
                Assert.Equal(0, state.GetProperty("distanceTraveled").GetProperty("value").GetDouble());
                Assert.Equal(0, state.GetProperty("elapsedTravelHours").GetDouble());
                Assert.False(state.GetProperty("isLost").GetBoolean());
                Assert.Equal(JsonValueKind.Null, state.GetProperty("activeWatchNumber").ValueKind);
                Assert.Equal(JsonValueKind.Null, moved.GetProperty("pauseReason").ValueKind);
                Assert.Contains(moved.GetProperty("knownCells").EnumerateArray(),
                    cell => cell.GetRawText() == targetCell);
                Assert.Contains(moved.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "DmOverrideApplied"
                        && item.GetProperty("cell").GetRawText() == targetCell
                        && item.GetProperty("message").GetString()!.Contains("Teleportation test", StringComparison.Ordinal));
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restartedClient = restartedFactory.CreateClient())
            {
                var reloaded = await restartedClient.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}");
                var state = reloaded.GetProperty("expedition");
                Assert.Equal(targetCell, state.GetProperty("currentCell").GetRawText());
                Assert.Equal(0, state.GetProperty("distanceTraveled").GetProperty("value").GetDouble());
                Assert.False(state.TryGetProperty("currentHex", out _));
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ProcedureRuntimeHelpersRoundTripThroughHttpAndRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            Guid procedureId;
            int procedureRevision;
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var world = await CreateWorld(client);
                var worldId = world.GetProperty("id").GetGuid();

                using var startResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/cell-expeditions", new
                {
                    name = "Procedure helper expedition",
                    presetKey = "alexandrian-advanced",
                    presentationKey = "exploration-map",
                    startCell = new { motifCellId = "face-0", translation = new { u = 0L, v = 0L } }
                });
                startResponse.EnsureSuccessStatusCode();
                var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();
                var procedure = started.GetProperty("procedure");
                procedureId = procedure.GetProperty("procedureId").GetGuid();
                procedureRevision = procedure.GetProperty("revision").GetInt32();
                AssertAlexandrianProcedure(procedure);
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restartedClient = restartedFactory.CreateClient())
            {
                var reloaded = await restartedClient.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}");
                var procedure = reloaded.GetProperty("procedure");
                Assert.Equal(procedureId, procedure.GetProperty("procedureId").GetGuid());
                Assert.Equal(procedureRevision, procedure.GetProperty("revision").GetInt32());
                AssertAlexandrianProcedure(procedure);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static void AssertAlexandrianProcedure(JsonElement procedure)
    {
        Assert.Equal("alexandrian-advanced", procedure.GetProperty("key").GetString());
        Assert.Equal("Alexandrian Advanced", procedure.GetProperty("name").GetString());
        Assert.True(procedure.GetProperty("isExecutable").GetBoolean());

        var runtime = procedure.GetProperty("runtime");
        Assert.Equal(4, runtime.GetProperty("intervalHours").GetDouble());
        Assert.Equal("PerWatch", runtime.GetProperty("encounterCadence").GetString());
        var helpers = runtime.GetProperty("resolutionHelpers");
        var travel = helpers.GetProperty("travel");
        Assert.Equal(2, travel.GetProperty("roll").GetProperty("diceCount").GetInt32());
        Assert.Equal(6, travel.GetProperty("roll").GetProperty("dieSides").GetInt32());
        Assert.Equal(3, travel.GetProperty("roll").GetProperty("modifier").GetInt32());
        Assert.Equal(0.1, travel.GetProperty("distanceFactorPerRollPoint").GetDouble(), 6);

        var navigation = helpers.GetProperty("navigation");
        Assert.Equal(1, navigation.GetProperty("checkRoll").GetProperty("diceCount").GetInt32());
        Assert.Equal(20, navigation.GetProperty("checkRoll").GetProperty("dieSides").GetInt32());
        Assert.Equal(0, navigation.GetProperty("checkRoll").GetProperty("modifier").GetInt32());

        var encounter = helpers.GetProperty("encounter");
        Assert.Equal(8, encounter.GetProperty("timingSlots").GetInt32());
        Assert.Equal([1], encounter.GetProperty("wanderingResults").EnumerateArray().Select(item => item.GetInt32()).ToArray());
        Assert.Equal([8], encounter.GetProperty("keyedLocationResults").EnumerateArray().Select(item => item.GetInt32()).ToArray());
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
                using var startResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/cell-expeditions", new
                {
                    name,
                    presetKey = "simple-fixed-distance",
                    presentationKey = "dm-controlled",
                    startCell = new { motifCellId = "face-0", translation = new { u = 0L, v = 0L } }
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
    public async Task FiniteAlternatePacePersistsAcrossPartialTravelRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        Guid sessionId;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var procedure = await SyntheticProcedureApiFixture.CreatePacedGeometricAsync(database);
                using var startResponse = await client.PostAsJsonAsync("/api/expeditions/cells", new
                {
                    name = "Pace persistence",
                    procedureId = procedure.ProcedureId,
                    procedureRevision = procedure.Revision,
                    context = new
                    {
                        kind = "MaplessSpatial",
                        name = "Pace proof",
                        physicalDistancePerWorldUnit = 12,
                        distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
                    }
                });
                startResponse.EnsureSuccessStatusCode();
                var session = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                sessionId = session.GetProperty("id").GetGuid();

                var modes = session.GetProperty("movementComposition")
                    .GetProperty("policy")
                    .GetProperty("travelModeKeys")
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .ToArray();
                Assert.Equal(new[] { "normal", "fast", "slow" }, modes);

                var topology = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{sessionId:D}/cell-topology");
                var center = topology.GetProperty("center");
                var side = topology.GetProperty("interfaces")[0];
                var heading = new WorldPoint(
                    (side.GetProperty("start").GetProperty("x").GetDouble()
                     + side.GetProperty("end").GetProperty("x").GetDouble()) / 2
                     - center.GetProperty("x").GetDouble(),
                    (side.GetProperty("start").GetProperty("y").GetDouble()
                     + side.GetProperty("end").GetProperty("y").GetDouble()) / 2
                     - center.GetProperty("y").GetDouble());
                var manual = new ResolutionProvenance(ResolutionSource.ManualRoll);
                using var advance = await client.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/cells/advance",
                    new AdvanceCellWatchRequest(
                        session.GetProperty("version").GetInt64(),
                        new CellWatchTravelPlan(heading, false, false,
                            new TravelModeSelection("fast", []),
                            NavigationAidSelection.None),
                        new CellWatchAdvanceInputs(
                            ResolvedTravelAmount.Distance(
                                new DistanceMeasure(12, DistanceUnit.Miles),
                                new DistanceMeasure(12, DistanceUnit.Miles), manual),
                            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, manual),
                            ResolvedEncounter.None)),
                    CellJson);
                advance.EnsureSuccessStatusCode();
                session = await advance.Content.ReadFromJsonAsync<JsonElement>();

                Assert.Equal("ConditionsReviewRequired", session.GetProperty("pauseReason").GetString());
                Assert.Equal("fast", session.GetProperty("expedition").GetProperty("activePaceKey").GetString());
                Assert.True(session.GetProperty("expedition").GetProperty("activeWatchRemainingHours").GetDouble() > 0);
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restartedClient = restartedFactory.CreateClient())
            {
                var reloaded = await restartedClient.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{sessionId:D}");

                Assert.Equal("ConditionsReviewRequired", reloaded.GetProperty("pauseReason").GetString());
                Assert.Equal("fast", reloaded.GetProperty("expedition").GetProperty("activePaceKey").GetString());
                Assert.Contains(
                    reloaded.GetProperty("movementComposition").GetProperty("policy").GetProperty("travelModeKeys").EnumerateArray(),
                    value => value.GetString() == "fast");
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task MaplessPeriodicCellExpeditionPersistsAndAdvancesWithoutAnAuthoredWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        Guid sessionId;
        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var worldsBefore = await client.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Equal(0, worldsBefore.GetArrayLength());

                using var startResponse = await client.PostAsJsonAsync(
                    "/api/expeditions/cells", new
                    {
                        name = "Paper map crawl",
                        procedureKey = "simple-fixed-distance",
                        context = new
                        {
                            kind = "MaplessSpatial",
                            name = "Paper map",
                            physicalDistancePerWorldUnit = 12,
                            distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
                        },
                        startCell = new
                        {
                            motifCellId = DelaneyDressHarmonicMetricRealization
                                .Construct("<1:1,1,1:6,3>", 1d, "world-unit")
                                .Topology!.MotifCells[0].Id,
                            translation = new { u = 2, v = -1 }
                        }
                    }, CellJson);
                startResponse.EnsureSuccessStatusCode();
                var session = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                sessionId = session.GetProperty("id").GetGuid();
                Assert.Equal(JsonValueKind.Null, session.GetProperty("overworldId").ValueKind);
                Assert.Equal("MaplessSpatial", session.GetProperty("context").GetProperty("kind").GetString());
                Assert.Equal("Paper map", session.GetProperty("context").GetProperty("name").GetString());
                Assert.True(session.GetProperty("context").TryGetProperty("tiling", out _));
                var spatial = session.GetProperty("expedition");
                Assert.True(spatial.GetProperty("isSpatial").GetBoolean());
                Assert.False(spatial.TryGetProperty("currentHex", out _));
                var currentCell = spatial.GetProperty("currentCell").GetProperty("address");
                Assert.Equal(DelaneyDressHarmonicMetricRealization
                    .Construct("<1:1,1,1:6,3>", 1d, "world-unit")
                    .Topology!.MotifCells[0].Id, currentCell.GetProperty("motifCellId").GetString());
                Assert.Equal(2L, currentCell.GetProperty("translation").GetProperty("u").GetInt64());
                Assert.Equal(-1L, currentCell.GetProperty("translation").GetProperty("v").GetInt64());

                var topology = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{sessionId:D}/cell-topology");
                var center = topology.GetProperty("center");
                var edge = topology.GetProperty("interfaces")[0];
                var heading = new WorldPoint(
                    (edge.GetProperty("start").GetProperty("x").GetDouble()
                     + edge.GetProperty("end").GetProperty("x").GetDouble()) / 2
                     - center.GetProperty("x").GetDouble(),
                    (edge.GetProperty("start").GetProperty("y").GetDouble()
                     + edge.GetProperty("end").GetProperty("y").GetDouble()) / 2
                     - center.GetProperty("y").GetDouble());
                var manual = new ResolutionProvenance(ResolutionSource.ManualRoll);
                using var advanceResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/cells/advance",
                    new AdvanceCellWatchRequest(
                        session.GetProperty("version").GetInt64(),
                        new CellWatchTravelPlan(heading, false, false,
                            TravelModeSelection.Normal, NavigationAidSelection.None),
                        new CellWatchAdvanceInputs(
                            ResolvedTravelAmount.Distance(
                                new DistanceMeasure(3, DistanceUnit.Miles),
                                new DistanceMeasure(3, DistanceUnit.Miles), manual),
                            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, manual),
                            ResolvedEncounter.None)), CellJson);
                advanceResponse.EnsureSuccessStatusCode();
                session = await advanceResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(3, session.GetProperty("expedition")
                    .GetProperty("distanceTraveled").GetProperty("value").GetDouble(), 6);
                Assert.False(session.GetProperty("expedition").TryGetProperty("currentHex", out _));
                var worldsAfter = await client.GetFromJsonAsync<JsonElement>("/api/overworlds");
                Assert.Equal(0, worldsAfter.GetArrayLength());
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restarted = restartedFactory.CreateClient())
            {
                var sessions = await restarted.GetFromJsonAsync<JsonElement>("/api/expeditions");
                var summary = Assert.Single(sessions.EnumerateArray());
                Assert.Equal(sessionId, summary.GetProperty("id").GetGuid());
                Assert.Equal("MaplessSpatial", summary.GetProperty("context").GetProperty("kind").GetString());
                Assert.Equal(JsonValueKind.Null,
                    summary.GetProperty("context").GetProperty("overworldId").ValueKind);
                var reloaded = await restarted.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{sessionId:D}");
                Assert.Equal(3, reloaded.GetProperty("expedition")
                    .GetProperty("distanceTraveled").GetProperty("value").GetDouble(), 6);
                Assert.Empty((await restarted.GetFromJsonAsync<JsonElement>("/api/overworlds")).EnumerateArray());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task MaplessSessionRejectsMissingDistanceUnit()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/expeditions/cells", new
            {
                name = "Unitless crawl",
                procedureKey = "simple-fixed-distance",
                context = new
                {
                    kind = "MaplessSpatial",
                    name = "Unitless paper map",
                                        physicalDistancePerWorldUnit = 12
                },
                startCell = new { motifCellId = "cell-0", translation = new { u = 0, v = 0 } }
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
                watchHours = session.GetProperty("procedure").GetProperty("runtime").GetProperty("intervalHours").GetDouble();

                Assert.Equal(JsonValueKind.Null, session.GetProperty("overworldId").ValueKind);
                Assert.Equal("NonSpatial", session.GetProperty("context").GetProperty("kind").GetString());
                var runtime = session.GetProperty("expedition");
                Assert.False(runtime.GetProperty("isSpatial").GetBoolean());
                Assert.False(runtime.TryGetProperty("currentHex", out _));
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
                        && !item.TryGetProperty("hex", out _));
                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "WatchTimeAdvanced"
                        && item.GetProperty("watchNumber").GetInt32() == 1);

                using var encounterResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/nonspatial/encounters",
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
                        && !item.TryGetProperty("hex", out _));

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
                Assert.Equal("EncounterTriggered", session.GetProperty("pauseReason").GetString());
                var pending = runtime.GetProperty("pendingEncounter");
                var occurrenceId = pending.GetProperty("id").GetGuid();

                using (var blockedResume = await restartedClient.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/assistants/watch",
                    new
                    {
                        expectedVersion = session.GetProperty("version").GetInt64(),
                        elapsedHours = watchHours - 1,
                        resolutionSource = "DmOverride",
                        resolutionNote = "must not bypass encounter",
                        note = "blocked"
                    }))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, blockedResume.StatusCode);
                }

                var pendingVersion = session.GetProperty("version").GetInt64();
                using (var resolveResponse = await restartedClient.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/nonspatial/encounters/{occurrenceId:D}/resolve",
                    new
                    {
                        expectedVersion = pendingVersion,
                        resolutionSource = "DmOverride",
                        resolutionNote = "resolved at table",
                        resultNote = "complete"
                    }))
                {
                    resolveResponse.EnsureSuccessStatusCode();
                    session = await resolveResponse.Content.ReadFromJsonAsync<JsonElement>();
                    runtime = session.GetProperty("expedition");
                }

                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("pendingEncounter").ValueKind);
                Assert.Equal(JsonValueKind.Null, session.GetProperty("pauseReason").ValueKind);
                Assert.Contains(
                    session.GetProperty("history").EnumerateArray(),
                    item => item.GetProperty("kind").GetString() == "EncounterResolved"
                        && item.GetProperty("encounterOccurrenceId").GetGuid() == occurrenceId);

                using (var staleResolve = await restartedClient.PostAsJsonAsync(
                    $"/api/expeditions/{sessionId:D}/nonspatial/encounters/{occurrenceId:D}/resolve",
                    new
                    {
                        expectedVersion = pendingVersion,
                        resolutionSource = "DmOverride"
                    }))
                {
                    Assert.Equal(HttpStatusCode.Conflict, staleResolve.StatusCode);
                }

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

    [Fact]
    public async Task CellActiveWatchContractPreservesNavigationAidFlagsAcrossRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        Guid expeditionId;
        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var world = await CreateWorld(client);
                var worldId = world.GetProperty("id").GetGuid();
                using var startedResponse = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions",
                    new
                    {
                        name = "Navigation aid persistence",
                        presetKey = "alexandrian-advanced",
                        presentationKey = "exploration-map",
                        startCell = new { motifCellId = "face-0", translation = new { u = 0L, v = 0L } }
                    });
                startedResponse.EnsureSuccessStatusCode();
                var started = await startedResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();
                var topology = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}/cell-topology");
                var center = topology.GetProperty("center");
                var face = topology.GetProperty("interfaces")[0];
                var heading = new WorldPoint(
                    (face.GetProperty("start").GetProperty("x").GetDouble()
                     + face.GetProperty("end").GetProperty("x").GetDouble()) / 2
                    - center.GetProperty("x").GetDouble(),
                    (face.GetProperty("start").GetProperty("y").GetDouble()
                     + face.GetProperty("end").GetProperty("y").GetDouble()) / 2
                    - center.GetProperty("y").GetDouble());
                var manual = new ResolutionProvenance(ResolutionSource.ManualRoll);
                var input = new AdvanceCellWatchRequest(
                    started.GetProperty("version").GetInt64(),
                    new CellWatchTravelPlan(
                        heading, false, false, TravelModeSelection.Normal,
                        new NavigationAidSelection("trail-markers", true, true)),
                    new CellWatchAdvanceInputs(
                        ResolvedTravelAmount.Distance(
                            new DistanceMeasure(0.25, DistanceUnit.Miles),
                            new DistanceMeasure(0.25, DistanceUnit.Miles), manual),
                        null,
                        new ResolvedEncounter(
                            EncounterOutcomeKind.WanderingEncounter,
                            TimeSpan.FromHours(2), null, "Midwatch encounter", manual)));
                using var response = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/advance", input, CellJson);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var active = result.GetProperty("expedition");
                Assert.Equal("EncounterTriggered", result.GetProperty("pauseReason").GetString());
                Assert.Equal(1, active.GetProperty("activeWatchNumber").GetInt32());
                Assert.Equal("trail-markers", active.GetProperty("activeNavigationAidKey").GetString());
                Assert.True(active.GetProperty("activeSuppressesNavigationCheck").GetBoolean());
                Assert.True(active.GetProperty("activeResetsVeerAtBoundary").GetBoolean());
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restarted = restartedFactory.CreateClient())
            {
                var persisted = await restarted.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}");
                var active = persisted.GetProperty("expedition");
                Assert.Equal(1, active.GetProperty("activeWatchNumber").GetInt32());
                Assert.Equal("trail-markers", active.GetProperty("activeNavigationAidKey").GetString());
                Assert.True(active.GetProperty("activeSuppressesNavigationCheck").GetBoolean());
                Assert.True(active.GetProperty("activeResetsVeerAtBoundary").GetBoolean());
                Assert.Equal("EncounterTriggered", persisted.GetProperty("pauseReason").GetString());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> CreateWorld(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/overworlds/periodic", new
        {
            name = "Workbench API",
            tilingDsSymbol = "<1:1,1,1:6,3>",
            worldUnitsPerAbstractPeriod = 1.0,
            origin = new { x = 0.0, y = 0.0 },
            physicalDistancePerWorldUnit = new
            {
                value = 12.0,
                unit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
            }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
