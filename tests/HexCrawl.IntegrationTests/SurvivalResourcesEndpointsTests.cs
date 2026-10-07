using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class SurvivalResourcesEndpointsTests
{
    [Fact]
    public async Task SupplyDieConsumptionRoundTripsAndReplayDoesNotAdvanceVersion()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Supply die survival", "forbidden-lands");
            var expeditionId = started.GetProperty("id").GetGuid();
            var version = started.GetProperty("version").GetInt64();

            var initial = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/survival");
            Assert.Equal("Supported", initial.GetProperty("resourcePolicy").GetProperty("support").GetString());
            Assert.Equal("SupplyDie", initial.GetProperty("resourcePolicy").GetProperty("inventoryModel").GetString());

            var resourceId = Guid.NewGuid();
            using var addResponse = await client.PutAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/resources/{resourceId:D}",
                new
                {
                    expectedVersion = version,
                    resourceKey = "food",
                    target = new { scope = "Party", targetId = (Guid?)null },
                    inventoryModel = "SupplyDie",
                    quantity = (double?)null,
                    unit = (string?)null,
                    symbolicState = (string?)null,
                    supplyDieSides = 8,
                    note = (string?)null,
                    provenance = Provenance("add-supply-die")
                });
            addResponse.EnsureSuccessStatusCode();
            var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
            version = added.GetProperty("expeditionVersion").GetInt64();
            var addedResource = Assert.Single(added.GetProperty("state").GetProperty("resources").EnumerateArray());
            Assert.Equal(8, addedResource.GetProperty("supplyDieSides").GetInt32());

            var occurrenceId = Guid.NewGuid();
            using var consumeResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/consumption",
                new
                {
                    expectedVersion = version,
                    occurrenceId,
                    due = true,
                    target = new { scope = "Party", targetId = (Guid?)null },
                    changes = new[]
                    {
                        new
                        {
                            resourceKey = "food",
                            resourceId = (Guid?)resourceId,
                            operation = "SetSupplyDie",
                            quantity = (double?)null,
                            unit = (string?)null,
                            state = (string?)null,
                            supplyDieSides = (int?)6
                        }
                    },
                    provenance = Provenance("resolved-usage-roll")
                });
            consumeResponse.EnsureSuccessStatusCode();
            var consumed = await consumeResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Applied", consumed.GetProperty("status").GetString());
            version = consumed.GetProperty("expeditionVersion").GetInt64();
            var consumedResource = Assert.Single(consumed.GetProperty("state").GetProperty("resources").EnumerateArray());
            Assert.Equal(6, consumedResource.GetProperty("supplyDieSides").GetInt32());

            using var replayResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/consumption",
                new
                {
                    expectedVersion = version,
                    occurrenceId,
                    due = true,
                    target = new { scope = "Party", targetId = (Guid?)null },
                    changes = new[]
                    {
                        new
                        {
                            resourceKey = "food",
                            resourceId = (Guid?)resourceId,
                            operation = "SetSupplyDie",
                            quantity = (double?)null,
                            unit = (string?)null,
                            state = (string?)null,
                            supplyDieSides = (int?)6
                        }
                    },
                    provenance = Provenance("resolved-usage-roll")
                });
            replayResponse.EnsureSuccessStatusCode();
            var replayed = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("AlreadyApplied", replayed.GetProperty("status").GetString());
            Assert.Equal(version, replayed.GetProperty("expeditionVersion").GetInt64());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ResourceRemovalRejectsPendingImplicitKeyAndTargetReference()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Implicit pending target", "bx");
            var expeditionId = started.GetProperty("id").GetGuid();
            var version = started.GetProperty("version").GetInt64();
            var resourceId = Guid.NewGuid();

            using var addResponse = await client.PutAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/resources/{resourceId:D}",
                new
                {
                    expectedVersion = version,
                    resourceKey = "food",
                    target = new { scope = "Party", targetId = (Guid?)null },
                    inventoryModel = "Counted",
                    quantity = 5.0,
                    unit = "ration",
                    symbolicState = (string?)null,
                    supplyDieSides = (int?)null,
                    note = (string?)null,
                    provenance = Provenance("add-food")
                });
            addResponse.EnsureSuccessStatusCode();
            var added = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
            version = added.GetProperty("expeditionVersion").GetInt64();

            using var consequenceResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/consequences",
                new
                {
                    expectedVersion = version,
                    consequence = new
                    {
                        id = Guid.NewGuid(),
                        consequenceKey = "implicit-food-use",
                        category = "ResourceChange",
                        target = new { scope = "Party", targetId = (Guid?)null },
                        components = new object[]
                        {
                            new
                            {
                                kind = "resourceChange",
                                resourceKey = "food",
                                resourceId = (Guid?)null,
                                operation = "AdjustQuantity",
                                quantity = -1.0,
                                unit = "ration",
                                state = (string?)null,
                                supplyDieSides = (int?)null
                            }
                        },
                        provenance = Provenance("pending-food-use")
                    }
                });
            consequenceResponse.EnsureSuccessStatusCode();
            var pending = await consequenceResponse.Content.ReadFromJsonAsync<JsonElement>();
            version = pending.GetProperty("expedition").GetProperty("version").GetInt64();
            Assert.Equal("Deferred", pending.GetProperty("status").GetString());

            using var deleteRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/expeditions/{expeditionId:D}/survival/resources/{resourceId:D}")
            {
                Content = JsonContent.Create(new
                {
                    expectedVersion = version,
                    provenance = Provenance("remove-food")
                })
            };
            using var deleteResponse = await client.SendAsync(deleteRequest);
            Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);

            var current = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/survival");
            Assert.Single(current.GetProperty("resources").EnumerateArray());
            Assert.Single(current.GetProperty("pendingResourceConsequences").EnumerateArray());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ForcedTravelProjectionDerivesFailureTargetScopeFromPinnedEffectPolicy()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Forced travel target", CrawlProcedureCatalog.Dnd2024PresetKey);
            var expeditionId = started.GetProperty("id").GetGuid();

            var state = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/survival");

            Assert.Equal(
                "Participant",
                state.GetProperty("forcedTravelPolicy").GetProperty("failureTargetScope").GetString());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ForcedTravelParticipantFailureAppliesPersistsAndRecoversEffect()
    {
        var database = TestWebHost.NewDatabasePath();
        var memberId = Guid.NewGuid();
        Guid expeditionId;
        Guid effectId;
        long versionAfterFailure;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var started = await StartMaplessExpeditionAsync(
                    client,
                    "Forced travel participant lifecycle",
                    CrawlProcedureCatalog.Dnd2024PresetKey);
                expeditionId = started.GetProperty("id").GetGuid();
                var version = started.GetProperty("version").GetInt64();

                using var partyResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = version,
                        members = new[]
                        {
                            new
                            {
                                id = memberId,
                                name = "Walker",
                                externalCharacterId = (string?)null,
                                countsTowardPartyMovement = true
                            }
                        }
                    });
                partyResponse.EnsureSuccessStatusCode();
                var party = await partyResponse.Content.ReadFromJsonAsync<JsonElement>();
                version = party.GetProperty("version").GetInt64();

                using var usageResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/survival/forced-travel/usage",
                    new
                    {
                        expectedVersion = version,
                        occurrenceId = Guid.NewGuid(),
                        amount = 9.0,
                        unit = "hours",
                        provenance = Provenance("forced-travel-over-limit")
                    });
                usageResponse.EnsureSuccessStatusCode();
                var usage = await usageResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(usage.GetProperty("state").GetProperty("forcedTravel").GetProperty("checkDue").GetBoolean());
                var checkId = usage.GetProperty("state").GetProperty("forcedTravel").GetProperty("pendingCheckId").GetGuid();
                var versionBeforeFailure = usage.GetProperty("expeditionVersion").GetInt64();

                using var failureResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/survival/forced-travel/check",
                    new
                    {
                        expectedVersion = versionBeforeFailure,
                        checkId,
                        success = false,
                        target = new { scope = "Participant", targetId = memberId },
                        failureComponents = new[]
                        {
                            new
                            {
                                kind = "PersistentEffect",
                                key = "exhaustion",
                                effectOperation = "AdjustLevel",
                                levelDelta = (int?)1,
                                level = (int?)null,
                                magnitude = (double?)null,
                                delta = (double?)null,
                                unit = (string?)null,
                                state = (string?)null
                            }
                        },
                        provenance = Provenance("forced-travel-check-resolution")
                    });
                failureResponse.EnsureSuccessStatusCode();
                var failure = await failureResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Applied", failure.GetProperty("status").GetString());
                versionAfterFailure = failure.GetProperty("expeditionVersion").GetInt64();

                var effects = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/effects");
                var active = Assert.Single(effects.GetProperty("activeEffects").EnumerateArray());
                effectId = active.GetProperty("id").GetGuid();
                Assert.Equal("exhaustion", active.GetProperty("effectKey").GetString());
                Assert.Equal("Participant", active.GetProperty("target").GetProperty("scope").GetString());
                Assert.Equal(memberId, active.GetProperty("target").GetProperty("targetId").GetGuid());
                Assert.Equal(1, active.GetProperty("level").GetInt32());

                using var staleReset = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/survival/forced-travel/reset",
                    new
                    {
                        expectedVersion = versionBeforeFailure,
                        provenance = Provenance("stale-forced-travel-reset")
                    });
                Assert.Equal(HttpStatusCode.Conflict, staleReset.StatusCode);
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var reloadedEffects = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/effects");
                var persisted = Assert.Single(reloadedEffects.GetProperty("activeEffects").EnumerateArray());
                Assert.Equal(effectId, persisted.GetProperty("id").GetGuid());
                Assert.Equal(memberId, persisted.GetProperty("target").GetProperty("targetId").GetGuid());
                Assert.Equal(1, persisted.GetProperty("level").GetInt32());

                var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
                Assert.Equal(versionAfterFailure, reloaded.GetProperty("version").GetInt64());

                using var recoverResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/effects/{effectId:D}/recover",
                    new
                    {
                        expectedVersion = versionAfterFailure,
                        triggerKey = "manual",
                        levelReduction = (int?)null,
                        clear = true,
                        provenance = Provenance("forced-travel-effect-recovery")
                    });
                recoverResponse.EnsureSuccessStatusCode();
                var recovered = await recoverResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Applied", recovered.GetProperty("status").GetString());
                Assert.Empty(recovered.GetProperty("effects").GetProperty("activeEffects").EnumerateArray());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task EmptyForcedTravelOccurrenceIsRejectedWithoutAdvancingVersion()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Forced travel identity", "forbidden-lands");
            var expeditionId = started.GetProperty("id").GetGuid();
            var version = started.GetProperty("version").GetInt64();

            using var response = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/forced-travel/usage",
                new
                {
                    expectedVersion = version,
                    occurrenceId = Guid.Empty,
                    amount = 1.0,
                    unit = "quarter-days",
                    provenance = Provenance("bad-occurrence")
                });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var current = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/survival");
            Assert.Equal(version, current.GetProperty("expeditionVersion").GetInt64());
            Assert.Equal(0, current.GetProperty("forcedTravel").GetProperty("amountSinceReset").GetDouble());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ExposureIdentityIsNormalizedAndReplaysDoNotAdvanceVersion()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Exposure identity", "bx");
            var expeditionId = started.GetProperty("id").GetGuid();
            var version = started.GetProperty("version").GetInt64();

            var store = new PostgresHexCrawlStore(database);
            var expedition = await store.GetExpeditionAsync(expeditionId, "integration-user");
            Assert.NotNull(expedition);
            var procedure = expedition.CampaignProcedure with
            {
                Modules = expedition.CampaignProcedure.Modules.Append(
                    new MaterializedProcedureModule(
                        Phase11GenericProcedureCatalog.ExposureModuleDefinition,
                        Phase11GenericProcedureCatalog.ExposureMechanicDefinition,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["dimensions"] = "temperature;elevation;weather",
                            ["evaluationModel"] = "resolved-check",
                            ["evaluationInterval"] = "travel-day",
                            ["targetScope"] = "party",
                            ["consequenceModel"] = "resolved-structured-consequence"
                        })).ToArray()
            };
            procedure.Validate();
            var procedureSave = await store.SaveExpeditionAsync(
                expedition with { CampaignProcedure = procedure }, version);
            Assert.Equal(SaveOutcome.Saved, procedureSave.Outcome);
            version = procedureSave.Value!.Version;

            var progressOccurrence = Guid.NewGuid();
            using var firstResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/exposure",
                new
                {
                    expectedVersion = version,
                    occurrenceId = progressOccurrence,
                    exposureKey = "  cold  ",
                    target = new { scope = "Party", targetId = (Guid?)null },
                    progressDelta = 1.0,
                    progressUnit = "  hours  ",
                    consequenceComponents = Array.Empty<object>(),
                    provenance = Provenance("cold-progress")
                });
            firstResponse.EnsureSuccessStatusCode();
            var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Applied", first.GetProperty("status").GetString());
            version = first.GetProperty("expeditionVersion").GetInt64();
            var progress = Assert.Single(first.GetProperty("state").GetProperty("exposure").EnumerateArray());
            Assert.Equal("cold", progress.GetProperty("exposureKey").GetString());
            Assert.Equal("hours", progress.GetProperty("unit").GetString());

            using var replayResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/exposure",
                new
                {
                    expectedVersion = version,
                    occurrenceId = progressOccurrence,
                    exposureKey = "cold",
                    target = new { scope = "Party", targetId = (Guid?)null },
                    progressDelta = 1.0,
                    progressUnit = "hours",
                    consequenceComponents = Array.Empty<object>(),
                    provenance = Provenance("cold-progress")
                });
            replayResponse.EnsureSuccessStatusCode();
            var replay = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("AlreadyApplied", replay.GetProperty("status").GetString());
            Assert.Equal(version, replay.GetProperty("expeditionVersion").GetInt64());

            using var collisionResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/exposure",
                new
                {
                    expectedVersion = version,
                    occurrenceId = progressOccurrence,
                    exposureKey = "altitude",
                    target = new { scope = "Party", targetId = (Guid?)null },
                    progressDelta = 1.0,
                    progressUnit = "hours",
                    consequenceComponents = Array.Empty<object>(),
                    provenance = Provenance("collision")
                });
            collisionResponse.EnsureSuccessStatusCode();
            var collision = await collisionResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("RequiresAdjudication", collision.GetProperty("status").GetString());
            Assert.Equal(version, collision.GetProperty("expeditionVersion").GetInt64());

            var consequenceOccurrence = Guid.NewGuid();
            using var consequenceResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/exposure",
                new
                {
                    expectedVersion = version,
                    occurrenceId = consequenceOccurrence,
                    exposureKey = "cold",
                    target = new { scope = "Party", targetId = (Guid?)null },
                    progressDelta = (double?)null,
                    progressUnit = (string?)null,
                    consequenceComponents = new[]
                    {
                        new
                        {
                            kind = "PersistentEffect",
                            key = "cold-fatigue",
                            effectOperation = "AdjustLevel",
                            levelDelta = (int?)1,
                            level = (int?)null,
                            magnitude = (double?)null,
                            delta = (double?)null,
                            unit = (string?)null,
                            state = (string?)null
                        }
                    },
                    provenance = Provenance("cold-effect")
                });
            consequenceResponse.EnsureSuccessStatusCode();
            var consequence = await consequenceResponse.Content.ReadFromJsonAsync<JsonElement>();
            version = consequence.GetProperty("expeditionVersion").GetInt64();

            using var consequenceReplayResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/survival/exposure",
                new
                {
                    expectedVersion = version,
                    occurrenceId = consequenceOccurrence,
                    exposureKey = "cold",
                    target = new { scope = "Party", targetId = (Guid?)null },
                    progressDelta = (double?)null,
                    progressUnit = (string?)null,
                    consequenceComponents = new[]
                    {
                        new
                        {
                            kind = "PersistentEffect",
                            key = "cold-fatigue",
                            effectOperation = "AdjustLevel",
                            levelDelta = (int?)1,
                            level = (int?)null,
                            magnitude = (double?)null,
                            delta = (double?)null,
                            unit = (string?)null,
                            state = (string?)null
                        }
                    },
                    provenance = Provenance("cold-effect")
                });
            consequenceReplayResponse.EnsureSuccessStatusCode();
            var consequenceReplay = await consequenceReplayResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("AlreadyApplied", consequenceReplay.GetProperty("status").GetString());
            Assert.Equal(version, consequenceReplay.GetProperty("expeditionVersion").GetInt64());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> StartMaplessExpeditionAsync(HttpClient client, string name, string procedureKey)
    {
        using var response = await client.PostAsJsonAsync("/api/expeditions", new
        {
            name,
            procedureKey,
            context = new
            {
                kind = "NonSpatial",
                name = "Procedure only"
            }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Provenance(string sourceKey) => new
    {
        sourceKind = "Dm",
        sourceKey,
        sourceReference = (string?)null,
        providerName = (string?)null,
        note = (string?)null
    };
}
