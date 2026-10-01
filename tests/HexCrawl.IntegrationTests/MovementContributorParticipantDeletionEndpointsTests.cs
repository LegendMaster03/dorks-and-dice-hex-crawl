using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class MovementContributorParticipantDeletionEndpointsTests
{
    [Fact]
    public async Task PartyMemberDeletionPersistsContributorCleanupWithoutMakingOrdinarySavesDestructive()
    {
        var database = TestWebHost.NewDatabasePath();
        var bobId = Guid.NewGuid();
        var caraId = Guid.NewGuid();
        var bobMovementId = Guid.NewGuid();
        var wagonId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        Guid expeditionId;
        long finalVersion;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
                {
                    name = "Movement contributor deletion",
                    procedureKey = "dnd-3-5e",
                    context = new
                    {
                        kind = "NonSpatial",
                        name = "Deletion regression"
                    }
                });
                startResponse.EnsureSuccessStatusCode();
                var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();
                var version = started.GetProperty("version").GetInt64();

                using var seedResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = version,
                        members = new[]
                        {
                            new
                            {
                                id = bobId,
                                name = "Bob",
                                externalCharacterId = (string?)null,
                                countsTowardPartyMovement = true
                            },
                            new
                            {
                                id = caraId,
                                name = "Cara",
                                externalCharacterId = (string?)null,
                                countsTowardPartyMovement = true
                            }
                        },
                        movementContributors = new object[]
                        {
                            ParticipantContributor(bobMovementId, bobId),
                            VehicleContributor(wagonId, "wagon", [bobId, caraId]),
                            VehicleContributor(cartId, "cart", [bobId]),
                            PersistentContributor(unrelatedId)
                        }
                    });
                seedResponse.EnsureSuccessStatusCode();
                var seeded = await seedResponse.Content.ReadFromJsonAsync<JsonElement>();
                version = seeded.GetProperty("version").GetInt64();
                Assert.Equal(4, seeded.GetProperty("party").GetProperty("movementContributors").GetArrayLength());

                using var ordinarySaveResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = version,
                        members = new[]
                        {
                            new
                            {
                                id = bobId,
                                name = "Bob",
                                externalCharacterId = (string?)null,
                                countsTowardPartyMovement = true
                            },
                            new
                            {
                                id = caraId,
                                name = "Cara Updated",
                                externalCharacterId = (string?)null,
                                countsTowardPartyMovement = true
                            }
                        }
                    });
                ordinarySaveResponse.EnsureSuccessStatusCode();
                var ordinarySaved = await ordinarySaveResponse.Content.ReadFromJsonAsync<JsonElement>();
                version = ordinarySaved.GetProperty("version").GetInt64();
                Assert.Equal(4, ordinarySaved.GetProperty("party").GetProperty("movementContributors").GetArrayLength());

                using var deletionResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = version,
                        members = new[]
                        {
                            new
                            {
                                id = caraId,
                                name = "Cara Updated",
                                externalCharacterId = (string?)null,
                                countsTowardPartyMovement = true
                            }
                        },
                        movementContributors = new object[]
                        {
                            VehicleContributor(wagonId, "wagon", [caraId]),
                            VehicleContributor(cartId, "cart", []),
                            PersistentContributor(unrelatedId)
                        }
                    });
                deletionResponse.EnsureSuccessStatusCode();
                var deleted = await deletionResponse.Content.ReadFromJsonAsync<JsonElement>();
                finalVersion = deleted.GetProperty("version").GetInt64();
                AssertCleanedParty(deleted, bobId, caraId, bobMovementId, wagonId, cartId, unrelatedId);
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
                Assert.Equal(finalVersion, reloaded.GetProperty("version").GetInt64());
                AssertCleanedParty(reloaded, bobId, caraId, bobMovementId, wagonId, cartId, unrelatedId);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

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
        provenance = "participant deletion regression",
        note = (string?)null,
        enabled = true
    };

    private static object VehicleContributor(Guid id, string key, Guid[] replacesParticipantIds) => new
    {
        id,
        kind = "Vehicle",
        key,
        operation = "Base",
        scope = "MovementUnit",
        value = 4d,
        unit = "mi",
        perUnit = "hour",
        distanceUnit = new
        {
            kind = "Mile",
            symbol = "mi",
            metersPerUnit = 1609.344
        },
        symbolicValue = (string?)null,
        participantId = (Guid?)null,
        movementUnitKey = (string?)null,
        replacesParticipantIds,
        provenance = "vehicle deletion regression",
        note = (string?)null,
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
        provenance = "unrelated deletion regression",
        note = (string?)null,
        enabled = true
    };

    private static void AssertCleanedParty(
        JsonElement expedition,
        Guid removedMemberId,
        Guid remainingMemberId,
        Guid removedContributorId,
        Guid wagonId,
        Guid cartId,
        Guid unrelatedId)
    {
        var party = expedition.GetProperty("party");
        var members = party.GetProperty("members").EnumerateArray().ToArray();
        Assert.Single(members);
        Assert.Equal(remainingMemberId, members[0].GetProperty("id").GetGuid());

        var contributors = party.GetProperty("movementContributors").EnumerateArray().ToArray();
        Assert.Equal(3, contributors.Length);
        Assert.DoesNotContain(contributors, contributor =>
            contributor.GetProperty("id").GetGuid() == removedContributorId);
        Assert.DoesNotContain(contributors, contributor =>
            contributor.GetProperty("participantId").ValueKind != JsonValueKind.Null
            && contributor.GetProperty("participantId").GetGuid() == removedMemberId);

        var wagon = Assert.Single(contributors, contributor =>
            contributor.GetProperty("id").GetGuid() == wagonId);
        Assert.Equal(
            [remainingMemberId],
            wagon.GetProperty("replacesParticipantIds").EnumerateArray().Select(value => value.GetGuid()).ToArray());

        var cart = Assert.Single(contributors, contributor =>
            contributor.GetProperty("id").GetGuid() == cartId);
        Assert.Equal(0, cart.GetProperty("replacesParticipantIds").GetArrayLength());

        Assert.Contains(contributors, contributor => contributor.GetProperty("id").GetGuid() == unrelatedId);
    }
}
