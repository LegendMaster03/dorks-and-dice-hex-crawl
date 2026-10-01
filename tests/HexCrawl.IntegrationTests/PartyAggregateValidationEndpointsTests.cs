using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class PartyAggregateValidationEndpointsTests
{
    [Fact]
    public async Task OmittedMovementContributorsCanNotPersistDanglingParticipantReference()
    {
        var database = TestWebHost.NewDatabasePath();
        var bobId = Guid.NewGuid();
        var caraId = Guid.NewGuid();
        var bobMovementId = Guid.NewGuid();
        Guid expeditionId;
        long seededVersion;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var seeded = await StartAndSeedAsync(
                    client,
                    "Rejected omitted contributor deletion",
                    bobId,
                    caraId,
                    [ParticipantContributor(bobMovementId, bobId)]);
                expeditionId = seeded.GetProperty("id").GetGuid();
                seededVersion = seeded.GetProperty("version").GetInt64();

                using var response = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = seededVersion,
                        members = new[]
                        {
                            Member(caraId, "Cara")
                        }
                    });

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Contains(
                    "Movement contributor references a party member that does not exist.",
                    error.GetProperty("error").GetString(),
                    StringComparison.Ordinal);

                var unchanged = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}");
                AssertOriginalParty(unchanged, seededVersion, bobId, caraId, bobMovementId);
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var reloaded = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}");
                AssertOriginalParty(reloaded, seededVersion, bobId, caraId, bobMovementId);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task OmittedContributorsPreserveStateAndExplicitReplacementStillWins()
    {
        var database = TestWebHost.NewDatabasePath();
        var bobId = Guid.NewGuid();
        var caraId = Guid.NewGuid();
        var bobMovementId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        Guid expeditionId;
        long finalVersion;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var seeded = await StartAndSeedAsync(
                    client,
                    "Preserved omitted contributors",
                    bobId,
                    caraId,
                    [
                        ParticipantContributor(bobMovementId, bobId),
                        PersistentContributor(unrelatedId)
                    ]);
                expeditionId = seeded.GetProperty("id").GetGuid();
                var seededVersion = seeded.GetProperty("version").GetInt64();

                using var ordinaryResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = seededVersion,
                        members = new[]
                        {
                            Member(bobId, "Bob"),
                            Member(caraId, "Cara Updated")
                        }
                    });
                ordinaryResponse.EnsureSuccessStatusCode();
                var ordinary = await ordinaryResponse.Content.ReadFromJsonAsync<JsonElement>();
                var ordinaryVersion = ordinary.GetProperty("version").GetInt64();
                Assert.Equal(seededVersion + 1, ordinaryVersion);
                AssertContributorSet(ordinary, bobMovementId, unrelatedId, bobId);

                using var replacementResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = ordinaryVersion,
                        members = new[]
                        {
                            Member(caraId, "Cara Updated")
                        },
                        movementContributors = new[]
                        {
                            PersistentContributor(unrelatedId)
                        }
                    });
                replacementResponse.EnsureSuccessStatusCode();
                var replaced = await replacementResponse.Content.ReadFromJsonAsync<JsonElement>();
                finalVersion = replaced.GetProperty("version").GetInt64();
                Assert.Equal(ordinaryVersion + 1, finalVersion);
                AssertExplicitReplacement(replaced, caraId, unrelatedId, bobMovementId);
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var reloaded = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}");
                Assert.Equal(finalVersion, reloaded.GetProperty("version").GetInt64());
                AssertExplicitReplacement(reloaded, caraId, unrelatedId, bobMovementId);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> StartAndSeedAsync(
        HttpClient client,
        string name,
        Guid bobId,
        Guid caraId,
        object[] movementContributors)
    {
        using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
        {
            name,
            procedureKey = "dnd-3-5e",
            context = new
            {
                kind = "NonSpatial",
                name = "Party aggregate validation"
            }
        });
        startResponse.EnsureSuccessStatusCode();
        var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var expeditionId = started.GetProperty("id").GetGuid();
        var version = started.GetProperty("version").GetInt64();

        using var seedResponse = await client.PutAsJsonAsync(
            $"/api/expeditions/{expeditionId:D}/party",
            new
            {
                expectedVersion = version,
                members = new[]
                {
                    Member(bobId, "Bob"),
                    Member(caraId, "Cara")
                },
                movementContributors
            });
        seedResponse.EnsureSuccessStatusCode();
        return await seedResponse.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Member(Guid id, string name) => new
    {
        id,
        name,
        externalCharacterId = (string?)null,
        countsTowardPartyMovement = true
    };

    private static object ParticipantContributor(Guid id, Guid participantId) => new
    {
        id,
        kind = "Participant",
        key = "bob-walk",
        operation = "Base",
        scope = "Participant",
        value = 3d,
        unit = "mi",
        perUnit = "hour",
        distanceUnit = new
        {
            kind = "Mile",
            symbol = "mi",
            metersPerUnit = 1609.344
        },
        symbolicValue = (string?)null,
        participantId = (Guid?)participantId,
        movementUnitKey = (string?)null,
        replacesParticipantIds = Array.Empty<Guid>(),
        provenance = "final aggregate validation regression",
        note = "Bob walking capability",
        enabled = true
    };

    private static object PersistentContributor(Guid id) => new
    {
        id,
        kind = "PersistentEffect",
        key = "unrelated-effect",
        operation = "Multiply",
        scope = "Party",
        value = 0.75d,
        unit = "factor",
        perUnit = (string?)null,
        distanceUnit = (object?)null,
        symbolicValue = (string?)null,
        participantId = (Guid?)null,
        movementUnitKey = (string?)null,
        replacesParticipantIds = Array.Empty<Guid>(),
        provenance = "unrelated preserved contributor",
        note = "Must survive ordinary party edits",
        enabled = true
    };

    private static void AssertOriginalParty(
        JsonElement expedition,
        long expectedVersion,
        Guid bobId,
        Guid caraId,
        Guid bobMovementId)
    {
        Assert.Equal(expectedVersion, expedition.GetProperty("version").GetInt64());
        var party = expedition.GetProperty("party");
        var members = party.GetProperty("members").EnumerateArray().ToArray();
        Assert.Equal(2, members.Length);
        Assert.Contains(members, member => member.GetProperty("id").GetGuid() == bobId);
        Assert.Contains(members, member => member.GetProperty("id").GetGuid() == caraId);

        var contributors = party.GetProperty("movementContributors").EnumerateArray().ToArray();
        var contributor = Assert.Single(contributors);
        Assert.Equal(bobMovementId, contributor.GetProperty("id").GetGuid());
        Assert.Equal(bobId, contributor.GetProperty("participantId").GetGuid());
    }

    private static void AssertContributorSet(
        JsonElement expedition,
        Guid bobMovementId,
        Guid unrelatedId,
        Guid bobId)
    {
        var contributors = expedition
            .GetProperty("party")
            .GetProperty("movementContributors")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, contributors.Length);

        var bob = Assert.Single(contributors, contributor =>
            contributor.GetProperty("id").GetGuid() == bobMovementId);
        Assert.Equal(bobId, bob.GetProperty("participantId").GetGuid());
        Assert.Equal("final aggregate validation regression", bob.GetProperty("provenance").GetString());
        Assert.Equal("Bob walking capability", bob.GetProperty("note").GetString());

        var unrelated = Assert.Single(contributors, contributor =>
            contributor.GetProperty("id").GetGuid() == unrelatedId);
        Assert.Equal("unrelated-effect", unrelated.GetProperty("key").GetString());
        Assert.Equal(0.75d, unrelated.GetProperty("value").GetDouble());
        Assert.Equal("unrelated preserved contributor", unrelated.GetProperty("provenance").GetString());
        Assert.Equal("Must survive ordinary party edits", unrelated.GetProperty("note").GetString());
    }

    private static void AssertExplicitReplacement(
        JsonElement expedition,
        Guid remainingMemberId,
        Guid unrelatedId,
        Guid removedContributorId)
    {
        var party = expedition.GetProperty("party");
        var member = Assert.Single(party.GetProperty("members").EnumerateArray().ToArray());
        Assert.Equal(remainingMemberId, member.GetProperty("id").GetGuid());

        var contributors = party.GetProperty("movementContributors").EnumerateArray().ToArray();
        var contributor = Assert.Single(contributors);
        Assert.Equal(unrelatedId, contributor.GetProperty("id").GetGuid());
        Assert.NotEqual(removedContributorId, contributor.GetProperty("id").GetGuid());
        Assert.Equal("unrelated-effect", contributor.GetProperty("key").GetString());
        Assert.Equal("unrelated preserved contributor", contributor.GetProperty("provenance").GetString());
        Assert.Equal("Must survive ordinary party edits", contributor.GetProperty("note").GetString());
    }
}
