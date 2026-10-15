using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;

namespace HexCrawl.IntegrationTests;

public sealed class Phase20ExplorationFlowEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static object Provenance(string key) => new
    {
        sourceKind = "Dm",
        sourceKey = key,
        sourceReference = (string?)null,
        providerName = (string?)null,
        note = (string?)null
    };

    [Theory]
    [InlineData(CrawlProcedureCatalog.WastelandWandererPresetKey, 4)]
    [InlineData(CrawlProcedureCatalog.WastelandWarfarePresetKey, 2)]
    public async Task PinnedExplorationFlowPersistsThroughStageResolutionAndRestart(
        string presetKey, int expectedStages)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId, processId = Guid.NewGuid();
            long version;
            string[] stageKeys;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var started = await client.PostAsJsonAsync("/api/expeditions/cells", new
                {
                    name = "Configurable exploration round",
                    procedureKey = presetKey,
                    context = new { kind = "MaplessSpatial", name = "Square world" }
                }, JsonOptions);
                Assert.Equal(HttpStatusCode.Created, started.StatusCode);
                var expedition = await started.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = expedition.GetProperty("id").GetGuid();
                version = expedition.GetProperty("version").GetInt64();

                using var first = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-flow",
                    new { expectedVersion = version, processId, provenance = Provenance("start-round") },
                    JsonOptions);
                first.EnsureSuccessStatusCode();
                var response = await first.Content.ReadFromJsonAsync<JsonElement>();
                var journey = response.GetProperty("state");
                version = journey.GetProperty("expeditionVersion").GetInt64();
                var active = journey.GetProperty("activeProcesses")[0];
                Assert.Equal("procedure.exploration.flow",
                    active.GetProperty("execution").GetProperty("executionHandler").GetString());
                Assert.Equal("Sequential",
                    active.GetProperty("execution").GetProperty("stageTransitionModel").GetString());
                stageKeys = active.GetProperty("definition").GetProperty("stageOrder")
                    .EnumerateArray().Select(value => value.GetString()!).ToArray();
                Assert.Equal(expectedStages, stageKeys.Length);

                // Neither a second active flow nor a stale mutation can fork the session.
                using var duplicate = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-flow",
                    new { expectedVersion = version, processId = Guid.NewGuid(),
                        provenance = Provenance("duplicate") }, JsonOptions);
                Assert.NotEqual(HttpStatusCode.OK, duplicate.StatusCode);

                using var stale = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-flow",
                    new { expectedVersion = version - 1, processId = Guid.NewGuid(),
                        provenance = Provenance("stale") }, JsonOptions);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

                for (var index = 0; index < stageKeys.Length; index++)
                {
                    if (presetKey == CrawlProcedureCatalog.WastelandWarfarePresetKey && index == 1)
                    {
                        // A selected action creates an unresolved event; resolving the
                        // existing generic event atomically completes the action stage.
                        using var recorded = await client.PostAsJsonAsync(
                            $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                            new
                            {
                                expectedVersion = version,
                                operationId = Guid.NewGuid(),
                                processId, actionKey = "explore", markVisited = true,
                                markRestSite = false, provenance = Provenance("record-action")
                            }, JsonOptions);
                        recorded.EnsureSuccessStatusCode();
                        journey = (await recorded.Content.ReadFromJsonAsync<JsonElement>())
                            .GetProperty("state");
                        version = journey.GetProperty("expeditionVersion").GetInt64();
                        Assert.Equal(stageKeys[index], journey.GetProperty("activeProcesses")[0]
                            .GetProperty("currentStageKey").GetString());
                        var eventId = journey.GetProperty("eventOccurrences")[0].GetProperty("id").GetGuid();

                        using var bypass = await client.PostAsJsonAsync(
                            $"/api/expeditions/{expeditionId:D}/journeys/processes/{processId:D}/resolve",
                            new
                            {
                                expectedVersion = version,
                                captureCurrentEnvironment = false,
                                resolution = new
                                {
                                    resolutionId = Guid.NewGuid(), processId,
                                    stageKey = stageKeys[index], completeStage = true,
                                    consequences = Array.Empty<object>(), provenance = Provenance("bypass")
                                }
                            }, JsonOptions);
                        Assert.Equal(HttpStatusCode.BadRequest, bypass.StatusCode);

                        using var encounter = await client.PostAsJsonAsync(
                            $"/api/expeditions/{expeditionId:D}/journeys/events/{eventId:D}/resolve",
                            new
                            {
                                expectedVersion = version,
                                captureCurrentEnvironment = false,
                                resolution = new
                                {
                                    occurrenceId = eventId, status = "Resolved",
                                    eventKey = "resolved-encounter", targetKind = "Expedition",
                                    consequences = Array.Empty<object>(), provenance = Provenance("resolve-encounter")
                                }
                            }, JsonOptions);
                        encounter.EnsureSuccessStatusCode();
                        journey = (await encounter.Content.ReadFromJsonAsync<JsonElement>())
                            .GetProperty("state");
                        version = journey.GetProperty("expeditionVersion").GetInt64();
                        continue;
                    }

                    using var resolved = await client.PostAsJsonAsync(
                        $"/api/expeditions/{expeditionId:D}/journeys/processes/{processId:D}/resolve",
                        new
                        {
                            expectedVersion = version,
                            captureCurrentEnvironment = false,
                            resolution = new
                            {
                                resolutionId = Guid.NewGuid(),
                                processId,
                                stageKey = stageKeys[index],
                                outcomeKey = presetKey == CrawlProcedureCatalog.WastelandWarfarePresetKey
                                    ? "explore" : null,
                                successDelta = 0,
                                failureDelta = 0,
                                complicationDelta = 0,
                                completeStage = true,
                                completeProcess = false,
                                failProcess = false,
                                consequences = Array.Empty<object>(),
                                provenance = Provenance("complete-stage")
                            }
                        }, JsonOptions);
                    resolved.EnsureSuccessStatusCode();
                    var updated = await resolved.Content.ReadFromJsonAsync<JsonElement>();
                    journey = updated.GetProperty("state");
                    version = journey.GetProperty("expeditionVersion").GetInt64();
                    if (index + 1 < stageKeys.Length)
                        Assert.Equal(stageKeys[index + 1],
                            journey.GetProperty("activeProcesses")[0]
                                .GetProperty("currentStageKey").GetString());
                }
                Assert.Empty(journey.GetProperty("activeProcesses").EnumerateArray());
                Assert.Equal(processId,
                    journey.GetProperty("closedProcesses")[0].GetProperty("id").GetGuid());
                Assert.Equal(expectedStages, journey.GetProperty("resolutions").GetArrayLength());
                if (presetKey == CrawlProcedureCatalog.WastelandWarfarePresetKey)
                    Assert.Equal("Resolved", journey.GetProperty("eventOccurrences")[0]
                        .GetProperty("status").GetString());
            }

            using (var restarted = TestWebHost.Create(database, "alice"))
            using (var client = restarted.CreateClient())
            {
                var persisted = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}/journeys", JsonOptions);
                Assert.Empty(persisted.GetProperty("activeProcesses").EnumerateArray());
                Assert.Equal(processId,
                    persisted.GetProperty("closedProcesses")[0].GetProperty("id").GetGuid());
                Assert.Equal(expectedStages, persisted.GetProperty("resolutions").GetArrayLength());
                Assert.Equal(version, persisted.GetProperty("expeditionVersion").GetInt64());
            }
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }


    [Theory]
    [InlineData(CrawlProcedureCatalog.WastelandWandererPresetKey, null)]
    [InlineData(CrawlProcedureCatalog.WastelandWarfarePresetKey, "explore")]
    public async Task ExplorationCellActionsArePinnedPersistentAndIdempotent(
        string presetKey, string? firstAction)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId, operationId = Guid.NewGuid();
            long version;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var started = await client.PostAsJsonAsync("/api/expeditions/cells", new
                {
                    name = "Site ledger",
                    procedureKey = presetKey,
                    context = new { kind = "MaplessSpatial", name = "Unexplored world" }
                }, JsonOptions);
                started.EnsureSuccessStatusCode();
                var created = await started.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = created.GetProperty("id").GetGuid();
                version = created.GetProperty("version").GetInt64();

                using var wrongAction = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    new { expectedVersion = version, operationId = Guid.NewGuid(),
                        actionKey = "nonexistent-action", markVisited = true,
                        markRestSite = false, provenance = Provenance("bad-action") }, JsonOptions);
                Assert.Equal(HttpStatusCode.BadRequest, wrongAction.StatusCode);

                using var visit = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    new { expectedVersion = version, operationId,
                        actionKey = firstAction, markVisited = true, markRestSite = false,
                        locationName = "Manually resolved site", dmNote = "GM only",
                        encounterOccurs = (bool?)null,
                        provenance = Provenance("site-visit") }, JsonOptions);
                visit.EnsureSuccessStatusCode();
                var response = await visit.Content.ReadFromJsonAsync<JsonElement>();
                var state = response.GetProperty("state");
                version = state.GetProperty("expeditionVersion").GetInt64();
                var site = state.GetProperty("explorationCell");
                Assert.True(site.GetProperty("visited").GetBoolean());
                Assert.Equal("Manually resolved site", site.GetProperty("locationName").GetString());
                Assert.Equal("GM only", site.GetProperty("dmNote").GetString());
                if (firstAction is not null)
                {
                    Assert.Single(state.GetProperty("eventOccurrences").EnumerateArray());
                    Assert.Equal("ResolutionRequired",
                        state.GetProperty("eventOccurrences")[0].GetProperty("status").GetString());
                    Assert.Contains("travel", site.GetProperty("availableActions")
                        .EnumerateArray().Select(value => value.GetString()));
                }
                else Assert.Empty(state.GetProperty("eventOccurrences").EnumerateArray());

                using var replay = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    new { expectedVersion = version - 1, operationId,
                        actionKey = firstAction, markVisited = true, markRestSite = false,
                        locationName = "Manually resolved site", dmNote = "GM only",
                        provenance = Provenance("site-visit") }, JsonOptions);
                replay.EnsureSuccessStatusCode();
                var duplicate = await replay.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(duplicate.GetProperty("stateChanged").GetBoolean());
                Assert.Equal(version,
                    duplicate.GetProperty("state").GetProperty("expeditionVersion").GetInt64());

                using var mismatchedReplay = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    new { expectedVersion = version - 1, operationId,
                        actionKey = firstAction, markVisited = true, markRestSite = false,
                        locationName = "Different location", dmNote = "GM only",
                        provenance = Provenance("site-visit") }, JsonOptions);
                Assert.Equal(HttpStatusCode.BadRequest, mismatchedReplay.StatusCode);

                if (firstAction is not null)
                {
                    using var unresolved = await client.PostAsJsonAsync(
                        $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                        new { expectedVersion = version, operationId = Guid.NewGuid(),
                            actionKey = "travel", markVisited = true, markRestSite = false,
                            provenance = Provenance("travel-unresolved") }, JsonOptions);
                    Assert.Equal(HttpStatusCode.BadRequest, unresolved.StatusCode);

                    using var declined = await client.PostAsJsonAsync(
                        $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                        new { expectedVersion = version, operationId = Guid.NewGuid(),
                            actionKey = "travel", markVisited = true, markRestSite = false,
                            encounterOccurs = false, provenance = Provenance("travel-declined") }, JsonOptions);
                    declined.EnsureSuccessStatusCode();
                    var declinedState = (await declined.Content.ReadFromJsonAsync<JsonElement>())
                        .GetProperty("state");
                    Assert.Single(declinedState.GetProperty("eventOccurrences").EnumerateArray());
                    version = declinedState.GetProperty("expeditionVersion").GetInt64();

                    using var triggered = await client.PostAsJsonAsync(
                        $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                        new { expectedVersion = version, operationId = Guid.NewGuid(),
                            actionKey = "travel", markVisited = true, markRestSite = false,
                            encounterOccurs = true, provenance = Provenance("travel-triggered") }, JsonOptions);
                    triggered.EnsureSuccessStatusCode();
                    var triggeredState = (await triggered.Content.ReadFromJsonAsync<JsonElement>())
                        .GetProperty("state");
                    Assert.Equal(2, triggeredState.GetProperty("eventOccurrences").GetArrayLength());
                    version = triggeredState.GetProperty("expeditionVersion").GetInt64();
                }

                using var stale = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    new { expectedVersion = version - 1, operationId = Guid.NewGuid(),
                        markRestSite = true, provenance = Provenance("stale") }, JsonOptions);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            using (var restarted = TestWebHost.Create(database, "alice"))
            using (var client = restarted.CreateClient())
            {
                var persisted = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}/journeys", JsonOptions);
                Assert.Equal(version, persisted.GetProperty("expeditionVersion").GetInt64());
                Assert.True(persisted.GetProperty("explorationCell").GetProperty("visited").GetBoolean());
            }
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
    [Fact]
    public async Task UnconfiguredProcedureCannotStartExplorationFlow()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var started = await client.PostAsJsonAsync("/api/expeditions/cells", new
            {
                name = "No exploration flow",
                procedureKey = "simple-hex-step",
                context = new { kind = "MaplessSpatial", name = "Unbounded hex map" }
            }, JsonOptions);
            started.EnsureSuccessStatusCode();
            var expedition = await started.Content.ReadFromJsonAsync<JsonElement>();
            var id = expedition.GetProperty("id").GetGuid();
            using var rejected = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/journeys/exploration-flow",
                new { expectedVersion = expedition.GetProperty("version").GetInt64(),
                    processId = Guid.NewGuid(), provenance = Provenance("invalid-flow") },
                JsonOptions);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
