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
