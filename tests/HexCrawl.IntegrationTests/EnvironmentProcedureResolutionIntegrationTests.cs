using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class EnvironmentProcedureResolutionIntegrationTests
{
    [Fact]
    public async Task AutomaticHelperCanNotBypassUnresolvedEnvironmentButExplicitDmDistanceCan()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
            {
                name = "Environment helper review",
                procedureKey = "alexandrian-advanced",
                context = new
                {
                    kind = "AbstractHex",
                    name = "Environment helper review",
                    orientation = "PointyTop",
                    hexCenterDistance = 12,
                    distanceUnit = new
                    {
                        kind = "Mile",
                        symbol = "mi",
                        metersPerUnit = 1609.344
                    }
                },
                startHex = new { q = 0, r = 0 }
            });
            startResponse.EnsureSuccessStatusCode();
            var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
            var expeditionId = started.GetProperty("id").GetGuid();
            var version = started.GetProperty("version").GetInt64();
            var memberId = Guid.NewGuid();

            using var partyResponse = await client.PutAsJsonAsync($"/api/expeditions/{expeditionId:D}/party", new
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
            var party = await partyResponse.Content.ReadFromJsonAsync<JsonElement>();
            version = party.GetProperty("version").GetInt64();

            using var environmentResponse = await client.PutAsJsonAsync($"/api/expeditions/{expeditionId:D}/environment", new
            {
                expectedVersion = version,
                currentFacts = new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        dimension = "weather",
                        valueKind = "Tag",
                        tag = "blizzard",
                        measurement = (object?)null,
                        provenance = "current weather",
                        note = (string?)null
                    }
                },
                overrides = Array.Empty<object>()
            });
            environmentResponse.EnsureSuccessStatusCode();
            version++;

            using var automatic = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/resolution-helper",
                HelperRequest(version, expectedDistance: null));
            Assert.Equal(HttpStatusCode.BadRequest, automatic.StatusCode);

            var afterRejected = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(version, afterRejected.GetProperty("version").GetInt64());

            using var manual = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/resolution-helper",
                HelperRequest(version, expectedDistance: 12));
            manual.EnsureSuccessStatusCode();
            var generated = await manual.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(12d, generated.GetProperty("travel").GetProperty("expectedDistance").GetDouble());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static object HelperRequest(long expectedVersion, double? expectedDistance) => new
    {
        expectedVersion,
        expectedDistance,
        travelDistanceRule = "walk",
        baseSpeedFeet = 30,
        suppressesNavigationCheck = false,
        deliberateDoubleBack = false,
        navigationDifficultyClass = -100,
        navigationModifier = 0,
        failureVeerSteps = 1
    };
}
