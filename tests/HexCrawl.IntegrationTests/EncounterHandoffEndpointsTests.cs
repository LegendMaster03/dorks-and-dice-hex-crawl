using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class EncounterHandoffEndpointsTests
{
    [Fact]
    public async Task RuntimeHandoffUsesRecordedHistoricalContextAndDoesNotMutateExpedition()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorldAsync(client);
            var worldId = world.GetProperty("id").GetGuid();

            var expedition = await StartWorldExpeditionAsync(
                client, worldId, "Historical encounter", "simple-fixed-distance", 0, 0);
            var expeditionId = expedition.GetProperty("id").GetGuid();

            using (var encounterResponse = await client.PostAsJsonAsync(
                       $"/api/expeditions/{expeditionId:D}/assistants/encounters",
                       new
                       {
                           expectedVersion = expedition.GetProperty("version").GetInt64(),
                           outcome = "WanderingEncounter",
                           resolutionSource = "ManualRoll",
                           note = "recorded at the original hex"
                       }))
            {
                encounterResponse.EnsureSuccessStatusCode();
                expedition = await encounterResponse.Content.ReadFromJsonAsync<JsonElement>();
            }

            var encounterEvent = expedition.GetProperty("history").EnumerateArray()
                .Single(value => value.GetProperty("kind").GetString() == "EncounterTriggered");
            var encounterSequence = encounterEvent.GetProperty("sequence").GetInt64();
            Assert.Equal("WanderingEncounter", encounterEvent.GetProperty("encounterOutcome").GetString());
            Assert.Equal(0, encounterEvent.GetProperty("hex").GetProperty("q").GetInt32());
            Assert.Equal(0, encounterEvent.GetProperty("hex").GetProperty("r").GetInt32());

            using (var travelResponse = await client.PostAsJsonAsync(
                       $"/api/expeditions/{expeditionId:D}/assistants/travel",
                       new
                       {
                           expectedVersion = expedition.GetProperty("version").GetInt64(),
                           elapsedHours = 1,
                           distance = 6,
                           resultingHex = new { q = 1, r = 0 },
                           hexProgress = 0,
                           intendedDirection = 0,
                           completeWatch = false,
                           resolutionSource = "ManualRoll",
                           resolutionNote = "moved after the recorded encounter"
                       }))
            {
                travelResponse.EnsureSuccessStatusCode();
                expedition = await travelResponse.Content.ReadFromJsonAsync<JsonElement>();
            }
            Assert.Equal(1, expedition.GetProperty("expedition").GetProperty("currentHex").GetProperty("q").GetInt32());

            var handoffId = Guid.NewGuid();
            var version = expedition.GetProperty("version").GetInt64();
            var handoff = await CreateRuntimeHandoffAsync(client, expeditionId, version, handoffId, encounterSequence);

            Assert.Equal(2, handoff.GetProperty("version").GetInt32());
            Assert.Equal("hex-crawl", handoff.GetProperty("sourceTool").GetString());
            Assert.Equal(handoffId, handoff.GetProperty("identity").GetProperty("handoffId").GetGuid());
            Assert.Equal($"runtime:{encounterSequence}", handoff.GetProperty("identity").GetProperty("encounterOccurrenceId").GetString());
            Assert.Equal("WanderingEncounter", handoff.GetProperty("encounter").GetProperty("outcome").GetString());
            Assert.Equal(0, handoff.GetProperty("worldContext").GetProperty("hex").GetProperty("q").GetInt32());
            Assert.Equal(0, handoff.GetProperty("worldContext").GetProperty("hex").GetProperty("r").GetInt32());
            Assert.Equal(worldId, handoff.GetProperty("worldContext").GetProperty("overworldId").GetGuid());

            var replay = await CreateRuntimeHandoffAsync(client, expeditionId, version, handoffId, encounterSequence);
            Assert.Equal(handoffId, replay.GetProperty("identity").GetProperty("handoffId").GetGuid());
            Assert.Equal(handoff.GetProperty("identity").GetProperty("encounterOccurrenceId").GetString(),
                replay.GetProperty("identity").GetProperty("encounterOccurrenceId").GetString());

            var after = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(version, after.GetProperty("version").GetInt64());
            Assert.Equal(1, after.GetProperty("expedition").GetProperty("currentHex").GetProperty("q").GetInt32());

            using var stale = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/encounter-handoff",
                new
                {
                    expectedVersion = version - 1,
                    handoffId = Guid.NewGuid(),
                    returnPath = $"/tools/hex-crawl/expeditions/{expeditionId:D}",
                    runtimeEncounterSequence = encounterSequence,
                    journeyEventOccurrenceId = (Guid?)null
                });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            using var unsafeReturn = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/encounter-handoff",
                new
                {
                    expectedVersion = version,
                    handoffId = Guid.NewGuid(),
                    returnPath = "//evil.example/hex-crawl",
                    runtimeEncounterSequence = encounterSequence,
                    journeyEventOccurrenceId = (Guid?)null
                });
            Assert.Equal(HttpStatusCode.BadRequest, unsafeReturn.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task JourneyHandoffProjectsOnlyRetainedOccurrenceContextAndLeavesPendingCircumstanceUntouched()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var guideId = Guid.NewGuid();
            var assignmentId = Guid.NewGuid();
            var resourceId = Guid.NewGuid();

            var expedition = await StartNonSpatialAsync(client, "the-one-ring-2e", "Mapless handoff journey");
            var expeditionId = expedition.GetProperty("id").GetGuid();
            var party = await PutGuideAsync(
                client, expeditionId, expedition.GetProperty("version").GetInt64(), guideId, assignmentId);
            var version = party.GetProperty("version").GetInt64();

            using (var resourceResponse = await client.PutAsJsonAsync(
                       $"/api/expeditions/{expeditionId:D}/survival/resources/{resourceId:D}",
                       new
                       {
                           expectedVersion = version,
                           resourceKey = "torches",
                           target = new { scope = "Party", targetId = (Guid?)null },
                           inventoryModel = "Counted",
                           quantity = 2.0,
                           unit = "torch",
                           symbolicState = (string?)null,
                           supplyDieSides = (int?)null,
                           note = (string?)null,
                           provenance = Provenance("Dm", "handoff-resource-seed")
                       }))
            {
                resourceResponse.EnsureSuccessStatusCode();
                var operation = await resourceResponse.Content.ReadFromJsonAsync<JsonElement>();
                version = operation.GetProperty("expeditionVersion").GetInt64();
            }

            var processId = Guid.NewGuid();
            using (var startProcess = await client.PostAsJsonAsync(
                       $"/api/expeditions/{expeditionId:D}/journeys/processes",
                       new
                       {
                           expectedVersion = version,
                           processId,
                           processKey = "road-to-refuge",
                           displayName = "Road to refuge",
                           destinationReference = "refuge-A",
                           routeReference = "route-A",
                           locationReference = "crossing-A",
                           provenance = Provenance("Dm", "handoff-process-start")
                       }))
            {
                startProcess.EnsureSuccessStatusCode();
                var operation = await startProcess.Content.ReadFromJsonAsync<JsonElement>();
                version = operation.GetProperty("state").GetProperty("expeditionVersion").GetInt64();
            }

            var resolutionId = Guid.NewGuid();
            using (var resolveRoute = await client.PostAsJsonAsync(
                       $"/api/expeditions/{expeditionId:D}/journeys/processes/{processId:D}/resolve",
                       new
                       {
                           expectedVersion = version,
                           captureCurrentEnvironment = false,
                           resolution = new
                           {
                               resolutionId,
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
                               provenance = Provenance("Dm", "handoff-route-progress")
                           }
                       }))
            {
                resolveRoute.EnsureSuccessStatusCode();
                var operation = await resolveRoute.Content.ReadFromJsonAsync<JsonElement>();
                version = operation.GetProperty("state").GetProperty("expeditionVersion").GetInt64();
            }

            var journey = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/journeys");
            var eventId = journey.GetProperty("eventOccurrences")[0].GetProperty("id").GetGuid();
            var consequenceId = Guid.NewGuid();
            using (var resolveEvent = await client.PostAsJsonAsync(
                       $"/api/expeditions/{expeditionId:D}/journeys/events/{eventId:D}/resolve",
                       new
                       {
                           expectedVersion = version,
                           captureCurrentEnvironment = false,
                           resolution = new
                           {
                               occurrenceId = eventId,
                               status = "Resolved",
                               eventKey = "ambush-at-crossing",
                               eventType = "encounter",
                               targetKind = "Role",
                               targetRoleKey = "guide",
                               targetId = guideId,
                               consequences = new object[]
                               {
                                   new
                                   {
                                       id = consequenceId,
                                       consequenceKey = "fatigue-resource-and-bad-position",
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
                                           },
                                           new
                                           {
                                               kind = "resourceChange",
                                               resourceKey = "torches",
                                               resourceId,
                                               operation = "Deplete",
                                               quantity = (double?)null,
                                               unit = (string?)null,
                                               state = (string?)null,
                                               supplyDieSides = (int?)null
                                           },
                                           new
                                           {
                                               kind = "encounterCircumstance",
                                               circumstanceKey = "bad-position",
                                               value = "narrow-route"
                                           }
                                       },
                                       provenance = Provenance("JourneyEvent", "handoff-event-consequence")
                                   }
                               },
                               provenance = Provenance("Dm", "handoff-event-resolution"),
                               note = "Historical event note"
                           }
                       }))
            {
                resolveEvent.EnsureSuccessStatusCode();
                var operation = await resolveEvent.Content.ReadFromJsonAsync<JsonElement>();
                version = operation.GetProperty("state").GetProperty("expeditionVersion").GetInt64();
            }

            var effectsBefore = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/effects");
            Assert.Contains(effectsBefore.GetProperty("pendingConsequences").EnumerateArray(),
                value => value.GetProperty("consequence").GetProperty("id").GetGuid() == consequenceId);
            Assert.Contains(effectsBefore.GetProperty("activeEffects").EnumerateArray(),
                value => value.GetProperty("sourceConsequenceIds").EnumerateArray().Any(id => id.GetGuid() == consequenceId));
            var survivalBefore = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/survival");
            var depletedResource = survivalBefore.GetProperty("resources").EnumerateArray()
                .Single(value => value.GetProperty("id").GetGuid() == resourceId);
            Assert.True(depletedResource.GetProperty("isDepleted").GetBoolean());

            var handoffId = Guid.NewGuid();
            using var response = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/encounter-handoff",
                new
                {
                    expectedVersion = version,
                    handoffId,
                    returnPath = $"/tools/hex-crawl/expeditions/{expeditionId:D}",
                    runtimeEncounterSequence = (long?)null,
                    journeyEventOccurrenceId = eventId
                });
            response.EnsureSuccessStatusCode();
            var handoff = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal($"journey-event:{eventId:D}", handoff.GetProperty("identity").GetProperty("encounterOccurrenceId").GetString());
            Assert.Equal(JsonValueKind.Null, handoff.GetProperty("worldContext").GetProperty("overworldId").ValueKind);
            Assert.Equal(JsonValueKind.Null, handoff.GetProperty("worldContext").GetProperty("hex").ValueKind);
            Assert.Equal(JsonValueKind.Null, handoff.GetProperty("worldContext").GetProperty("location").ValueKind);
            Assert.Equal(JsonValueKind.Null, handoff.GetProperty("timeContext").GetProperty("watchNumber").ValueKind);
            var journeyProvenance = handoff.GetProperty("journeyProvenance");
            Assert.Equal(eventId, journeyProvenance.GetProperty("eventOccurrenceId").GetGuid());
            Assert.Equal(processId, journeyProvenance.GetProperty("processId").GetGuid());
            Assert.Equal("road-to-refuge", journeyProvenance.GetProperty("processKey").GetString());
            Assert.Equal("refuge-A", journeyProvenance.GetProperty("destinationReference").GetString());
            Assert.Equal("route-A", journeyProvenance.GetProperty("routeReference").GetString());
            Assert.Equal("crossing-A", journeyProvenance.GetProperty("locationReference").GetString());
            Assert.Equal("ambush-at-crossing", journeyProvenance.GetProperty("eventKey").GetString());
            Assert.Equal(consequenceId, Assert.Single(handoff.GetProperty("circumstances").EnumerateArray())
                .GetProperty("consequenceId").GetGuid());
            var linkedEffect = Assert.Single(handoff.GetProperty("effects").EnumerateArray());
            Assert.Contains(linkedEffect.GetProperty("sourceConsequenceIds").EnumerateArray(), id => id.GetGuid() == consequenceId);
            var handoffResource = Assert.Single(handoff.GetProperty("resources").EnumerateArray());
            Assert.Equal(resourceId, handoffResource.GetProperty("id").GetGuid());
            Assert.Equal("torches", handoffResource.GetProperty("resourceKey").GetString());
            Assert.True(handoffResource.GetProperty("isDepleted").GetBoolean());

            var effectsAfter = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/effects");
            Assert.Contains(effectsAfter.GetProperty("pendingConsequences").EnumerateArray(),
                value => value.GetProperty("consequence").GetProperty("id").GetGuid() == consequenceId);
            var expeditionAfter = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(version, expeditionAfter.GetProperty("version").GetInt64());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> CreateRuntimeHandoffAsync(
        HttpClient client,
        Guid expeditionId,
        long expectedVersion,
        Guid handoffId,
        long encounterSequence)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/expeditions/{expeditionId:D}/encounter-handoff",
            new
            {
                expectedVersion,
                handoffId,
                returnPath = $"/tools/hex-crawl/expeditions/{expeditionId:D}",
                runtimeEncounterSequence = encounterSequence,
                journeyEventOccurrenceId = (Guid?)null
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> CreateWorldAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/overworlds", new
        {
            name = "Encounter handoff world",
            orientation = "PointyTop",
            origin = new { x = 0d, y = 0d },
            rotationDegrees = 0d,
            hexRadiusWorldUnits = 1d,
            neighborCenterDistance = 12d,
            distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = (double?)null }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> StartWorldExpeditionAsync(
        HttpClient client,
        Guid worldId,
        string name,
        string procedureKey,
        int q,
        int r)
    {
        using var response = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
        {
            name,
            procedureKey,
            presentationKey = "dm-controlled",
            startHex = new { q, r }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> StartNonSpatialAsync(HttpClient client, string procedureKey, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/expeditions", new
        {
            name,
            procedureKey,
            context = new { kind = "NonSpatial", name = "Journey handoff" }
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
