using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class MovementCapabilityCompositionEndpointsTests
{
    [Fact]
    public async Task MovementContributorsRoundTripExposeCompositionAndSurviveRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        var memberId = Guid.NewGuid();
        var contributorId = Guid.NewGuid();
        Guid expeditionId;
        long savedVersion;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
                {
                    name = "Movement composition crawl",
                    procedureKey = "dnd-3-5e",
                    context = new
                    {
                        kind = "NonSpatial",
                        name = "Movement composition"
                    }
                });
                startResponse.EnsureSuccessStatusCode();
                var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();
                var startingVersion = started.GetProperty("version").GetInt64();

                using var updateResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = startingVersion,
                        members = new[]
                        {
                            new
                            {
                                id = memberId,
                                name = "Walker",
                                externalCharacterId = (string?)"character:walker",
                                countsTowardPartyMovement = true
                            }
                        },
                        movementContributors = new[]
                        {
                            new
                            {
                                id = contributorId,
                                kind = "Participant",
                                key = "manual-walk",
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
                                provenance = "manual endpoint capability",
                                note = "walking rate",
                                enabled = true
                            }
                        }
                    });
                updateResponse.EnsureSuccessStatusCode();
                var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
                savedVersion = updated.GetProperty("version").GetInt64();

                AssertMovementProjection(updated, memberId, contributorId);

                using var staleResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = startingVersion,
                        members = Array.Empty<object>()
                    });
                Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
                Assert.Equal(savedVersion, reloaded.GetProperty("version").GetInt64());
                AssertMovementProjection(reloaded, memberId, contributorId);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static void AssertMovementProjection(
        JsonElement expedition,
        Guid memberId,
        Guid contributorId)
    {
        var party = expedition.GetProperty("party");
        var contributors = party.GetProperty("movementContributors");
        Assert.Single(contributors.EnumerateArray());
        var persisted = contributors[0];
        Assert.Equal(contributorId, persisted.GetProperty("id").GetGuid());
        Assert.Equal("Participant", persisted.GetProperty("kind").GetString());
        Assert.Equal(memberId, persisted.GetProperty("participantId").GetGuid());
        Assert.Equal("manual endpoint capability", persisted.GetProperty("provenance").GetString());

        var composition = expedition.GetProperty("movementComposition");
        Assert.Equal("Resolved", composition.GetProperty("status").GetString());
        Assert.Equal(3, composition.GetProperty("effectiveValue").GetDouble());
        Assert.Equal("mi", composition.GetProperty("effectiveUnit").GetString());
        Assert.Equal("hour", composition.GetProperty("effectivePerUnit").GetString());
        Assert.Equal(memberId, composition.GetProperty("limitingParticipantId").GetGuid());
        Assert.Contains(
            composition.GetProperty("provenance").EnumerateArray(),
            value => value.GetString() == "manual endpoint capability");
        Assert.Contains(
            composition.GetProperty("contributors").EnumerateArray(),
            value => value.GetProperty("id").GetGuid() == contributorId
                && value.GetProperty("applied").GetBoolean());
    }
}
