using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class EnvironmentEndpointsTests
{
    [Fact]
    public async Task WorldEnvironmentFeedsCanonicalExpeditionMovementAndSurvivesRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        Guid worldId;
        Guid expeditionId;
        long environmentVersion;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var world = await CreateWorldAsync(client);
                worldId = world.GetProperty("id").GetGuid();
                var worldVersion = world.GetProperty("version").GetInt64();

                using var startResponse = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
                {
                    name = "Environment HTTP crawl",
                    procedureKey = "dnd-3-5e",
                    presentationKey = "dm-controlled",
                    startHex = new { q = 0, r = 0 }
                });
                startResponse.EnsureSuccessStatusCode();
                var expedition = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = expedition.GetProperty("id").GetGuid();
                var expeditionVersion = expedition.GetProperty("version").GetInt64();
                var memberId = Guid.NewGuid();

                using var partyResponse = await client.PutAsJsonAsync($"/api/expeditions/{expeditionId:D}/party", new
                {
                    expectedVersion = expeditionVersion,
                    members = new[]
                    {
                        new
                        {
                            id = memberId,
                            name = "Walker",
                            externalCharacterId = (string?)null,
                            countsTowardPartyMovement = true
                        }
                    },
                    movementContributors = new[]
                    {
                        new
                        {
                            id = Guid.NewGuid(),
                            kind = "Participant",
                            key = "walk",
                            operation = "Base",
                            scope = "Participant",
                            value = 3,
                            unit = "mi",
                            perUnit = "hour",
                            distanceUnit = new
                            {
                                kind = "Mile",
                                symbol = "mi",
                                metersPerUnit = 1609.344
                            },
                            symbolicValue = (string?)null,
                            participantId = memberId,
                            movementUnitKey = (string?)null,
                            replacesParticipantIds = Array.Empty<Guid>(),
                            provenance = "HTTP participant capability",
                            note = (string?)null,
                            enabled = true
                        }
                    }
                });
                partyResponse.EnsureSuccessStatusCode();

                using var environmentResponse = await client.PutAsJsonAsync($"/api/overworlds/{worldId:D}/environment", new
                {
                    expectedVersion = worldVersion,
                    annotations = new[]
                    {
                        new
                        {
                            id = Guid.NewGuid(),
                            scope = new
                            {
                                kind = "Hex",
                                hex = new { q = 0, r = 0 },
                                featureId = (Guid?)null
                            },
                            facts = new[]
                            {
                                Tag("terrain", "difficult", "authored hex truth")
                            }
                        }
                    }
                });
                environmentResponse.EnsureSuccessStatusCode();
                var worldEnvironment = await environmentResponse.Content.ReadFromJsonAsync<JsonElement>();
                environmentVersion = worldEnvironment.GetProperty("version").GetInt64();
                Assert.Equal(worldVersion + 1, environmentVersion);

                var focused = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/environment");
                Assert.Equal("Resolved", focused.GetProperty("effectiveContext").GetProperty("status").GetString());
                Assert.Equal("difficult", focused.GetProperty("evaluation").GetProperty("terrainKey").GetString());
                var effective = Assert.Single(focused.GetProperty("effectiveContext").GetProperty("facts").EnumerateArray());
                Assert.True(effective.GetProperty("effective").GetBoolean());
                Assert.Equal("Hex", effective.GetProperty("source").GetProperty("kind").GetString());
                Assert.Equal("authored hex truth", effective.GetProperty("fact").GetProperty("provenance").GetString());

                var canonical = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
                var movement = canonical.GetProperty("movementComposition");
                Assert.Equal("Resolved", movement.GetProperty("status").GetString());
                Assert.Equal(1.5, movement.GetProperty("effectiveValue").GetDouble());
                Assert.Contains(
                    movement.GetProperty("provenance").EnumerateArray(),
                    value => value.GetString()?.Contains("terrain difficult", StringComparison.OrdinalIgnoreCase) == true);
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restartedClient = restartedFactory.CreateClient())
            {
                var worldEnvironment = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}/environment");
                Assert.Equal(environmentVersion, worldEnvironment.GetProperty("version").GetInt64());
                var annotation = Assert.Single(worldEnvironment.GetProperty("annotations").EnumerateArray());
                Assert.Equal("difficult", Assert.Single(annotation.GetProperty("facts").EnumerateArray()).GetProperty("tag").GetString());

                var focused = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/environment");
                Assert.Equal("difficult", focused.GetProperty("evaluation").GetProperty("terrainKey").GetString());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task MaplessCurrentStateAndDmOverridePersistWithConcurrency()
    {
        var database = TestWebHost.NewDatabasePath();
        Guid expeditionId;
        long savedVersion;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
                {
                    name = "Mapless environment",
                    procedureKey = "simple-fixed-distance",
                    context = new
                    {
                        kind = "NonSpatial",
                        name = "Structural journey"
                    }
                });
                startResponse.EnsureSuccessStatusCode();
                var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();
                var startingVersion = started.GetProperty("version").GetInt64();

                using var updateResponse = await client.PutAsJsonAsync($"/api/expeditions/{expeditionId:D}/environment", new
                {
                    expectedVersion = startingVersion,
                    currentFacts = new[]
                    {
                        Tag("weather", "rain", "current weather"),
                        Measurement("temperature", -5, "C", "current temperature")
                    },
                    overrides = new[]
                    {
                        Tag("visibility", "darkness", "DM current override")
                    }
                });
                updateResponse.EnsureSuccessStatusCode();
                var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
                savedVersion = startingVersion + 1;
                Assert.Equal("Resolved", updated.GetProperty("effectiveContext").GetProperty("status").GetString());
                Assert.Equal(3, updated.GetProperty("effectiveContext").GetProperty("facts").EnumerateArray().Count(value => value.GetProperty("effective").GetBoolean()));

                using var staleResponse = await client.PutAsJsonAsync($"/api/expeditions/{expeditionId:D}/environment", new
                {
                    expectedVersion = startingVersion,
                    currentFacts = Array.Empty<object>(),
                    overrides = Array.Empty<object>()
                });
                Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            }

            using (var restartedFactory = TestWebHost.Create(database))
            using (var restartedClient = restartedFactory.CreateClient())
            {
                var canonical = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
                Assert.Equal(savedVersion, canonical.GetProperty("version").GetInt64());
                var environment = await restartedClient.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}/environment");
                Assert.Equal("rain", environment.GetProperty("state").GetProperty("currentFacts")[0].GetProperty("tag").GetString());
                Assert.Equal("darkness", Assert.Single(environment.GetProperty("state").GetProperty("overrides").EnumerateArray()).GetProperty("tag").GetString());
                Assert.Null(environment.GetProperty("movementComposition").GetProperty("suggestedExpectedDistance").ValueKind == JsonValueKind.Null
                    ? null
                    : environment.GetProperty("movementComposition").GetProperty("suggestedExpectedDistance").GetRawText());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task InvalidSpatialFeatureEnvironmentReferenceIsRejectedByHttpBeforePersistence()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorldAsync(client);
            var worldId = world.GetProperty("id").GetGuid();
            var version = world.GetProperty("version").GetInt64();

            using var response = await client.PutAsJsonAsync($"/api/overworlds/{worldId:D}/environment", new
            {
                expectedVersion = version,
                annotations = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        scope = new
                        {
                            kind = "SpatialFeature",
                            hex = (object?)null,
                            featureId = Guid.NewGuid()
                        },
                        facts = new[] { Tag("route", "good-road") }
                    }
                }
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var current = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}/environment");
            Assert.Equal(version, current.GetProperty("version").GetInt64());
            Assert.Empty(current.GetProperty("annotations").EnumerateArray());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static object Tag(string dimension, string tag, string? provenance = null) => new
    {
        id = Guid.NewGuid(),
        dimension,
        valueKind = "Tag",
        tag,
        measurement = (object?)null,
        provenance,
        note = (string?)null
    };

    private static object Measurement(string dimension, double value, string unit, string? provenance = null) => new
    {
        id = Guid.NewGuid(),
        dimension,
        valueKind = "Measurement",
        tag = (string?)null,
        measurement = new { value, unit },
        provenance,
        note = (string?)null
    };

    private static async Task<JsonElement> CreateWorldAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/overworlds", new
        {
            name = "Environment HTTP world",
            orientation = "PointyTop",
            origin = new { x = 0, y = 0 },
            rotationDegrees = 0,
            hexRadiusWorldUnits = 1,
            neighborCenterDistance = 12,
            distanceUnit = new
            {
                kind = "Mile",
                symbol = "mi",
                metersPerUnit = 1609.344
            }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
