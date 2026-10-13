using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Api;

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

            using var startResponse = await client.PostAsJsonAsync("/api/expeditions/cells", new
            {
                name = "Environment helper review",
                procedureKey = "alexandrian-advanced",
                context = new
                {
                    kind = "MaplessSpatial",
                    name = "Environment helper review",
                                        physicalDistancePerWorldUnit = 12,
                    distanceUnit = new
                    {
                        kind = "Mile",
                        symbol = "mi",
                        metersPerUnit = 1609.344
                    }
                },
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
                $"/api/expeditions/{expeditionId:D}/cells/resolution-helper",
                HelperRequest(version, expectedDistance: null), Json);
            Assert.Equal(HttpStatusCode.BadRequest, automatic.StatusCode);

            var afterRejected = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(version, afterRejected.GetProperty("version").GetInt64());

            using var manual = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/cells/resolution-helper",
                HelperRequest(version, expectedDistance: new DistanceMeasure(12, DistanceUnit.Miles)),
                Json);
            manual.EnsureSuccessStatusCode();
            var generated = await manual.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(12d, generated.GetProperty("travel").GetProperty("expectedDistance")
                .GetProperty("value").GetDouble());
            Assert.Equal("mi", generated.GetProperty("travel").GetProperty("expectedDistance")
                .GetProperty("unit").GetProperty("symbol").GetString());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static ResolveCellProcedureInputsRequest HelperRequest(
        long expectedVersion, DistanceMeasure? expectedDistance) => new(
        ExpectedVersion: expectedVersion,
        ExpectedDistance: expectedDistance,
        TravelDistanceRule: "walk",
        BaseSpeedFeet: 30,
        NavigationDifficultyClass: -100);
}
