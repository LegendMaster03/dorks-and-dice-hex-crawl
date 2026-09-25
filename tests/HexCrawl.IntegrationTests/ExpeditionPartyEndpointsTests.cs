using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class ExpeditionPartyEndpointsTests
{
    [Fact]
    public async Task PartySheetIsOptionalEditableAndPersistsAcrossRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        var scoutId = Guid.NewGuid();
        var guardId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        Guid expeditionId;
        long savedVersion;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
                {
                    name = "Worksheet crawl",
                    procedureKey = "simple-fixed-distance",
                    context = new
                    {
                        kind = "NonSpatial",
                        name = "Procedure only"
                    }
                });
                startResponse.EnsureSuccessStatusCode();
                var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = started.GetProperty("id").GetGuid();
                Assert.Empty(started.GetProperty("party").GetProperty("members").EnumerateArray());

                using var updateResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = started.GetProperty("version").GetInt64(),
                        members = new[]
                        {
                            new { id = scoutId, name = "Scout", externalCharacterId = (string?)"character:scout", countsTowardPartyMovement = true },
                            new { id = guardId, name = "Guard", externalCharacterId = (string?)null, countsTowardPartyMovement = true }
                        },
                        marchingOrder = new[]
                        {
                            new { memberId = scoutId, rank = 0, file = 0 },
                            new { memberId = guardId, rank = 1, file = 0 }
                        },
                        watchList = new[]
                        {
                            new { slot = 1, memberIds = new[] { guardId }, label = "First rest watch" }
                        },
                        standingOrders = new[]
                        {
                            new { id = orderId, text = "Wake the navigator if the trail disappears.", enabled = true }
                        },
                        defaultNavigatorMemberId = scoutId,
                        baseMovement = new
                        {
                            perHour = new
                            {
                                value = 3,
                                unit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
                            },
                            perWatch = new
                            {
                                value = 12,
                                unit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
                            },
                            perMarch = (object?)null,
                            limitingMemberId = guardId,
                            note = "slowest active traveler"
                        }
                    });
                updateResponse.EnsureSuccessStatusCode();
                var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
                savedVersion = updated.GetProperty("version").GetInt64();

                var party = updated.GetProperty("party");
                Assert.Equal(2, party.GetProperty("members").GetArrayLength());
                Assert.Equal(scoutId, party.GetProperty("defaultNavigatorMemberId").GetGuid());
                Assert.Equal(12, party.GetProperty("baseMovement").GetProperty("perWatch").GetProperty("value").GetDouble());

                using var staleResponse = await client.PutAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/party",
                    new
                    {
                        expectedVersion = started.GetProperty("version").GetInt64(),
                        members = Array.Empty<object>()
                    });
                Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var reloaded = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
                Assert.Equal(savedVersion, reloaded.GetProperty("version").GetInt64());
                var party = reloaded.GetProperty("party");
                Assert.Equal("Scout", party.GetProperty("members")[0].GetProperty("name").GetString());
                Assert.Equal("First rest watch", party.GetProperty("watchList")[0].GetProperty("label").GetString());
                Assert.Equal("Wake the navigator if the trail disappears.", party.GetProperty("standingOrders")[0].GetProperty("text").GetString());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task PartySheetRejectsReferencesToUnknownMembers()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
            {
                name = "Invalid party",
                procedureKey = "simple-fixed-distance",
                context = new { kind = "NonSpatial", name = "Procedure only" }
            });
            startResponse.EnsureSuccessStatusCode();
            var started = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
            var expeditionId = started.GetProperty("id").GetGuid();

            using var updateResponse = await client.PutAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/party",
                new
                {
                    expectedVersion = started.GetProperty("version").GetInt64(),
                    members = Array.Empty<object>(),
                    marchingOrder = new[]
                    {
                        new { memberId = Guid.NewGuid(), rank = 0, file = 0 }
                    }
                });

            Assert.Equal(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
