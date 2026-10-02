using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class ExpeditionEffectEndpointsTests
{
    [Fact]
    public async Task ManualEffectRecoveryAndDeleteBodyBindingPersistAcrossTheHttpSurface()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Effect lifecycle");
            var expeditionId = started.GetProperty("id").GetGuid();
            var version = started.GetProperty("version").GetInt64();
            var effectId = Guid.NewGuid();

            using var upsertResponse = await client.PutAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/effects/{effectId:D}",
                new
                {
                    expectedVersion = version,
                    effect = new
                    {
                        id = effectId,
                        effectKey = "manual-fatigue",
                        target = new { scope = "Party", targetId = (Guid?)null },
                        level = 2,
                        recoveryModel = "short-rest"
                    },
                    provenance = Provenance("manual-effect")
                });
            upsertResponse.EnsureSuccessStatusCode();
            var upserted = await upsertResponse.Content.ReadFromJsonAsync<JsonElement>();
            version = upserted.GetProperty("expedition").GetProperty("version").GetInt64();
            var active = Assert.Single(upserted.GetProperty("effects").GetProperty("activeEffects").EnumerateArray());
            Assert.Equal("short-rest", active.GetProperty("recoveryModel").GetString());

            using var recoverResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/effects/{effectId:D}/recover",
                new
                {
                    expectedVersion = version,
                    triggerKey = "short-rest",
                    levelReduction = (int?)null,
                    clear = true,
                    provenance = Provenance("short-rest")
                });
            recoverResponse.EnsureSuccessStatusCode();
            var recovered = await recoverResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Applied", recovered.GetProperty("status").GetString());
            Assert.Empty(recovered.GetProperty("effects").GetProperty("activeEffects").EnumerateArray());
            version = recovered.GetProperty("expedition").GetProperty("version").GetInt64();

            var secondEffectId = Guid.NewGuid();
            using var secondUpsertResponse = await client.PutAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/effects/{secondEffectId:D}",
                new
                {
                    expectedVersion = version,
                    effect = new
                    {
                        id = secondEffectId,
                        effectKey = "manual-marker",
                        target = new { scope = "Expedition", targetId = (Guid?)null },
                        state = "active",
                        recoveryModel = "manual"
                    },
                    provenance = Provenance("manual-marker")
                });
            secondUpsertResponse.EnsureSuccessStatusCode();
            var secondUpserted = await secondUpsertResponse.Content.ReadFromJsonAsync<JsonElement>();
            version = secondUpserted.GetProperty("expedition").GetProperty("version").GetInt64();

            using var deleteRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                $"/api/expeditions/{expeditionId:D}/effects/{secondEffectId:D}")
            {
                Content = JsonContent.Create(new
                {
                    expectedVersion = version,
                    provenance = Provenance("manual-clear")
                })
            };
            using var deleteResponse = await client.SendAsync(deleteRequest);
            deleteResponse.EnsureSuccessStatusCode();
            var deleted = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(deleted.GetProperty("effects").GetProperty("activeEffects").EnumerateArray());

            var current = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/effects");
            Assert.Empty(current.GetProperty("activeEffects").EnumerateArray());
            Assert.Contains(
                current.GetProperty("history").EnumerateArray(),
                value => value.GetProperty("operation").GetString() == "manual:clear");
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task UnsupportedStructuredConsequenceCanBeResolvedWithoutLosingProducerProvenance()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Pending consequence");
            var expeditionId = started.GetProperty("id").GetGuid();
            var consequenceId = Guid.NewGuid();

            using var applyResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/consequences",
                new
                {
                    expectedVersion = started.GetProperty("version").GetInt64(),
                    consequence = new
                    {
                        id = consequenceId,
                        consequenceKey = "provider-weather-handoff",
                        category = "Custom",
                        target = new { scope = "Expedition", targetId = (Guid?)null },
                        components = new object[]
                        {
                            new
                            {
                                kind = "custom",
                                componentKey = "weather-handoff",
                                quantity = 1.0,
                                unit = "step",
                                state = "pending"
                            }
                        },
                        provenance = new
                        {
                            sourceKind = "Provider",
                            sourceKey = "weather-provider",
                            sourceReference = "provider:event:42",
                            providerName = "Test provider",
                            note = "producer context"
                        }
                    }
                });
            applyResponse.EnsureSuccessStatusCode();
            var applied = await applyResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Unsupported", applied.GetProperty("status").GetString());
            Assert.Single(applied.GetProperty("effects").GetProperty("pendingConsequences").EnumerateArray());
            var version = applied.GetProperty("expedition").GetProperty("version").GetInt64();

            using var resolveResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/consequences/{consequenceId:D}/resolve",
                new
                {
                    expectedVersion = version,
                    resolutionNote = "Handled by the DM after provider review.",
                    provenance = Provenance("dm-resolution")
                });
            resolveResponse.EnsureSuccessStatusCode();
            var resolved = await resolveResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(resolved.GetProperty("effects").GetProperty("pendingConsequences").EnumerateArray());
            var record = Assert.Single(resolved.GetProperty("effects").GetProperty("appliedConsequences").EnumerateArray());
            Assert.Equal("Recorded", record.GetProperty("status").GetString());
            Assert.Equal("weather-provider", record.GetProperty("provenance").GetProperty("sourceKey").GetString());
            Assert.Equal("dm-resolution", record.GetProperty("resolutionProvenance").GetProperty("sourceKey").GetString());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task OversizedTimeDelayReturnsBadRequestInsteadOfServerError()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartMaplessExpeditionAsync(client, "Oversized delay");
            var expeditionId = started.GetProperty("id").GetGuid();

            using var response = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/consequences",
                new
                {
                    expectedVersion = started.GetProperty("version").GetInt64(),
                    consequence = new
                    {
                        id = Guid.NewGuid(),
                        consequenceKey = "impossible-delay",
                        category = "TimeDelay",
                        target = new { scope = "Expedition", targetId = (Guid?)null },
                        components = new object[]
                        {
                            new
                            {
                                kind = "timeDelay",
                                value = double.MaxValue,
                                unit = "Days"
                            }
                        },
                        provenance = Provenance("overflow-delay")
                    }
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> StartMaplessExpeditionAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/expeditions", new
        {
            name,
            procedureKey = "simple-fixed-distance",
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
