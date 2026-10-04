using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class Phase12JourneyRuntimeIntegrationTests
{
    [Fact]
    public async Task OneRingMaplessJourneyRunsThroughRolesProgressEventFatigueCompletionAndRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        var guideId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        Guid expeditionId;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var started = await StartAsync(client, "the-one-ring-2e", "Mapless role journey");
                expeditionId = started.GetProperty("id").GetGuid();
                Assert.Equal(JsonValueKind.Null, started.GetProperty("procedure").GetProperty("runtime").ValueKind);
                Assert.Equal(JsonValueKind.Null, started.GetProperty("expedition").GetProperty("activeWatchNumber").ValueKind);

                var party = await PutGuideAsync(
                    client,
                    expeditionId,
                    started.GetProperty("version").GetInt64(),
                    guideId,
                    assignmentId);
                var version = party.GetProperty("version").GetInt64();

                var initialJourney = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/journeys");
                Assert.Equal("Supported", initialJourney.GetProperty("processPolicy").GetProperty("support").GetString());
                Assert.Equal("None", initialJourney.GetProperty("processPolicy").GetProperty("intervalIntegrationModel").GetString());
                Assert.Equal("journey-progress", initialJourney.GetProperty("processPolicy").GetProperty("progressUnit").GetString());
                Assert.Equal(3, initialJourney.GetProperty("processPolicy").GetProperty("stageKeys").GetArrayLength());

                var processId = Guid.NewGuid();
                using (var response = await client.PostAsJsonAsync(
                           $"/api/expeditions/{expeditionId:D}/journeys/processes",
                           new
                           {
                               expectedVersion = version,
                               processId,
                               processKey = "road-to-refuge",
                               displayName = "Road to refuge",
                               destinationReference = "refuge-A",
                               routeReference = "route-A",
                               provenance = Provenance("Dm", "one-ring-process-start")
                           }))
                {
                    response.EnsureSuccessStatusCode();
                    var operation = await response.Content.ReadFromJsonAsync<JsonElement>();
                    version = operation.GetProperty("state").GetProperty("expeditionVersion").GetInt64();
                    var process = operation.GetProperty("state").GetProperty("activeProcesses")[0];
                    Assert.Equal("route", process.GetProperty("currentStageKey").GetString());
                    Assert.Equal(0, process.GetProperty("stageStates")[0].GetProperty("numericProgress").GetDouble());
                }

                var routeResolutionId = Guid.NewGuid();
                using (var response = await client.PostAsJsonAsync(
                           $"/api/expeditions/{expeditionId:D}/journeys/processes/{processId:D}/resolve",
                           new
                           {
                               expectedVersion = version,
                               captureCurrentEnvironment = true,
                               resolution = new
                               {
                                   resolutionId = routeResolutionId,
                                   processId,
                                   stageKey = "route",
                                   roleKey = "guide",
                                   progressDelta = 1.0,
                                   successDelta = 0,
                                   failureDelta = 0,
                                   complicationDelta = 0,
                                   completeStage = true,
                                   completeProcess = false,
                                   failProcess = false,
                                   consequences = Array.Empty<object>(),
                                   provenance = Provenance("Dm", "resolved-guide-progress")
                               }
                           }))
                {
                    response.EnsureSuccessStatusCode();
                    var operation = await response.Content.ReadFromJsonAsync<JsonElement>();
                    var state = operation.GetProperty("state");
                    version = state.GetProperty("expeditionVersion").GetInt64();
                    Assert.Equal("events", state.GetProperty("activeProcesses")[0].GetProperty("currentStageKey").GetString());
                    Assert.Single(state.GetProperty("eventOccurrences").EnumerateArray());
                    Assert.Equal("ProcessProgress", state.GetProperty("eventOccurrences")[0].GetProperty("trigger").GetString());
                    Assert.Equal(guideId,
                        state.GetProperty("resolutions")[0].GetProperty("actor").GetProperty("participantId").GetGuid());
                }

                var journey = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/journeys");
                var eventId = journey.GetProperty("eventOccurrences")[0].GetProperty("id").GetGuid();
                var consequenceId = Guid.NewGuid();
                using (var response = await client.PostAsJsonAsync(
                           $"/api/expeditions/{expeditionId:D}/journeys/events/{eventId:D}/resolve",
                           new
                           {
                               expectedVersion = version,
                               captureCurrentEnvironment = true,
                               resolution = new
                               {
                                   occurrenceId = eventId,
                                   status = "Resolved",
                                   eventKey = "resolved-fatigue-event",
                                   eventType = "fatigue",
                                   targetKind = "Role",
                                   targetRoleKey = "guide",
                                   targetId = guideId,
                                   consequences = new object[]
                                   {
                                       new
                                       {
                                           id = consequenceId,
                                           consequenceKey = "fatigue",
                                           category = "PersistentEffectChange",
                                           target = new { scope = "Participant", targetId = guideId },
                                           components = new object[]
                                           {
                                               new
                                               {
                                                   kind = "persistentEffectChange",
                                                   effectKey = "fatigue",
                                                   operation = "AdjustLevel",
                                                   levelDelta = 1,
                                                   explicitlyResolved = true,
                                                   movementComponents = Array.Empty<object>()
                                               }
                                           },
                                           provenance = Provenance("JourneyEvent", "one-ring-event")
                                       }
                                   },
                                   provenance = Provenance("Dm", "one-ring-event-resolution")
                               }
                           }))
                {
                    response.EnsureSuccessStatusCode();
                    var operation = await response.Content.ReadFromJsonAsync<JsonElement>();
                    version = operation.GetProperty("state").GetProperty("expeditionVersion").GetInt64();
                    var occurrence = operation.GetProperty("state").GetProperty("eventOccurrences")[0];
                    Assert.Equal("Resolved", occurrence.GetProperty("status").GetString());
                    Assert.Equal(guideId, occurrence.GetProperty("participantSnapshot").GetProperty("participantId").GetGuid());
                    Assert.Equal(consequenceId, occurrence.GetProperty("consequenceIds")[0].GetGuid());
                }

                var effects = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/effects");
                Assert.Equal("fatigue", effects.GetProperty("activeEffects")[0].GetProperty("effectKey").GetString());
                Assert.Equal(1, effects.GetProperty("activeEffects")[0].GetProperty("level").GetInt32());

                version = await ResolveStageAsync(client, expeditionId, processId, version, "events", "guide");
                version = await ResolveStageAsync(client, expeditionId, processId, version, "arrival", "guide");

                var completed = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/journeys");
                Assert.Empty(completed.GetProperty("activeProcesses").EnumerateArray());
                Assert.Equal("Completed", completed.GetProperty("closedProcesses")[0].GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null,
                    (await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}"))
                    .GetProperty("expedition").GetProperty("activeWatchNumber").ValueKind);
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var restarted = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/journeys");
                Assert.Empty(restarted.GetProperty("activeProcesses").EnumerateArray());
                Assert.Equal("Completed", restarted.GetProperty("closedProcesses")[0].GetProperty("status").GetString());
                Assert.Equal("resolved-fatigue-event", restarted.GetProperty("eventOccurrences")[0].GetProperty("eventKey").GetString());
                Assert.Contains(restarted.GetProperty("history").EnumerateArray(),
                    value => value.GetProperty("kind").GetString() == "ProcessCompleted");
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task MixedHouseRuleWatchAndLandmarkCreateStandaloneStableEventOpportunities()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartAsync(client, "mixed-house-rule", "Mixed events");
            var expeditionId = started.GetProperty("id").GetGuid();

            using var watchResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                new
                {
                    expectedVersion = started.GetProperty("version").GetInt64(),
                    elapsedHours = 4,
                    resolutionSource = "ProcedureDefault"
                });
            watchResponse.EnsureSuccessStatusCode();
            var afterWatch = await watchResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, afterWatch.GetProperty("expedition").GetProperty("completedWatches").GetInt32());

            var journeys = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/journeys");
            Assert.Equal("None", journeys.GetProperty("processPolicy").GetProperty("support").GetString());
            Assert.Equal("Supported", journeys.GetProperty("eventPolicy").GetProperty("support").GetString());
            Assert.Single(journeys.GetProperty("eventOccurrences").EnumerateArray());
            Assert.Equal("WatchCompleted", journeys.GetProperty("eventOccurrences")[0].GetProperty("trigger").GetString());
            Assert.Equal("watch:1", journeys.GetProperty("eventOccurrences")[0].GetProperty("triggerReference").GetString());

            var version = journeys.GetProperty("expeditionVersion").GetInt64();
            var landmarkId = Guid.NewGuid();
            using var landmarkResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/journeys/events",
                new
                {
                    expectedVersion = version,
                    captureCurrentEnvironment = true,
                    opportunity = new
                    {
                        occurrenceId = landmarkId,
                        trigger = "Landmark",
                        triggerReference = "landmark:old-bridge",
                        targetKind = "Unresolved",
                        environment = Array.Empty<object>(),
                        provenance = Provenance("Dm", "landmark-event")
                    }
                });
            landmarkResponse.EnsureSuccessStatusCode();
            var landmark = await landmarkResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, landmark.GetProperty("state").GetProperty("eventOccurrences").GetArrayLength());
            Assert.Contains(landmark.GetProperty("state").GetProperty("eventOccurrences").EnumerateArray(),
                value => value.GetProperty("id").GetGuid() == landmarkId
                         && value.GetProperty("trigger").GetString() == "Landmark");
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<long> ResolveStageAsync(
        HttpClient client,
        Guid expeditionId,
        Guid processId,
        long version,
        string stageKey,
        string roleKey)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/expeditions/{expeditionId:D}/journeys/processes/{processId:D}/resolve",
            new
            {
                expectedVersion = version,
                captureCurrentEnvironment = true,
                resolution = new
                {
                    resolutionId = Guid.NewGuid(),
                    processId,
                    stageKey,
                    roleKey,
                    successDelta = 0,
                    failureDelta = 0,
                    complicationDelta = 0,
                    completeStage = true,
                    completeProcess = false,
                    failProcess = false,
                    consequences = Array.Empty<object>(),
                    provenance = Provenance("Dm", $"complete-{stageKey}")
                }
            });
        response.EnsureSuccessStatusCode();
        var operation = await response.Content.ReadFromJsonAsync<JsonElement>();
        return operation.GetProperty("state").GetProperty("expeditionVersion").GetInt64();
    }

    private static async Task<JsonElement> StartAsync(HttpClient client, string procedureKey, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/expeditions", new
        {
            name,
            procedureKey,
            context = new { kind = "NonSpatial", name = "Journey state" }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> PutGuideAsync(
        HttpClient client,
        Guid expeditionId,
        long expectedVersion,
        Guid guideId,
        Guid assignmentId)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/expeditions/{expeditionId:D}/party",
            new
            {
                expectedVersion,
                members = new[]
                {
                    new { id = guideId, name = "Guide", externalCharacterId = (string?)null, countsTowardPartyMovement = true }
                },
                activityAssignments = new[]
                {
                    new
                    {
                        id = assignmentId,
                        scope = "Role",
                        participantId = guideId,
                        activityKey = "guide",
                        roleKey = "guide",
                        note = (string?)null
                    }
                }
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Provenance(string sourceKind, string sourceKey) => new
    {
        sourceKind,
        sourceKey,
        sourceReference = (string?)null,
        providerName = (string?)null,
        note = (string?)null
    };
}