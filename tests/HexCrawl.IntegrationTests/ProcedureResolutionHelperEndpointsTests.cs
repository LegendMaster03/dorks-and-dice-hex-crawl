using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

public sealed class ProcedureResolutionHelperEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public async Task CellHelperPersistsAuditedGenerationAndOnlyExactCurrentValuesAuthorizeAutomaticRoll()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartAsync(client, "V2 helper integration");
            var id = started.GetProperty("id").GetGuid();
            var firstVersion = started.GetProperty("version").GetInt64();
            Assert.NotEqual(JsonValueKind.Null,
                started.GetProperty("procedure").GetProperty("runtime").GetProperty("resolutionHelpers").ValueKind);

            var first = await GenerateAsync(client, id, firstVersion);
            Assert.Equal(firstVersion + 1, first.ExpeditionVersion);
            Assert.Equal(1, first.AuditSequence);
            Assert.NotEqual(Guid.Empty, first.GeneratedResolutionId);
            AssertAutomaticResult(first);
            var recorded = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{id:D}");
            Assert.Equal(first.ExpeditionVersion, recorded.GetProperty("version").GetInt64());
            Assert.Equal(0, recorded.GetProperty("expedition")
                .GetProperty("distanceTraveled").GetProperty("value").GetDouble());
            var audit = Assert.Single(recorded.GetProperty("history").EnumerateArray());
            Assert.Equal("ProcedureResolutionHelperGenerated", audit.GetProperty("kind").GetString());
            Assert.NotEqual(JsonValueKind.Null, audit.GetProperty("cell").ValueKind);
            Assert.Equal(JsonValueKind.Null, audit.GetProperty("hex").ValueKind);

            using (var stale = await PostGenerateAsync(client, id, firstVersion))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var second = await GenerateAsync(client, id, first.ExpeditionVersion);
            Assert.Equal(firstVersion + 2, second.ExpeditionVersion);
            Assert.NotEqual(first.GeneratedResolutionId, second.GeneratedResolutionId);
            Assert.Equal(2, second.AuditSequence);
            AssertAutomaticResult(second);

            var topology = await client.GetFromJsonAsync<JsonElement>(
                $"/api/expeditions/{id:D}/cell-topology");
            var heading = Heading(topology);
            using (var oldGrant = await PostAdvanceAsync(client, id,
                Request(first, second.ExpeditionVersion, heading, first.GeneratedResolutionId)))
                Assert.Equal(HttpStatusCode.BadRequest, oldGrant.StatusCode);
            using (var forged = await PostAdvanceAsync(client, id,
                Request(second, second.ExpeditionVersion, heading, null)))
                Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

            var other = await StartAsync(client, "Other geometric session");
            var otherId = other.GetProperty("id").GetGuid();
            using (var crossSession = await PostAdvanceAsync(client, otherId,
                Request(second, other.GetProperty("version").GetInt64(),
                    heading, second.GeneratedResolutionId)))
                Assert.Equal(HttpStatusCode.BadRequest, crossSession.StatusCode);

            var tampered = second.Travel! with
            {
                ActualDistance = new DistanceMeasure(second.Travel!.ActualDistance!.Value.Value + 0.25d,
                    second.Travel.ActualDistance.Value.Unit)
            };
            using (var changed = await PostAdvanceAsync(client, id,
                Request(second with { Travel = tampered },
                    second.ExpeditionVersion, heading, second.GeneratedResolutionId)))
                Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);

            // A generated navigation roll cannot be silently consumed by
            // changing the plan to suppress the check after generation.
            var alteredPlan = Request(
                second, second.ExpeditionVersion, heading, second.GeneratedResolutionId);
            alteredPlan = alteredPlan with
            {
                Plan = alteredPlan.Plan with
                {
                    NavigationAid = new NavigationAidSelection("none", true, false)
                }
            };
            using (var suppressed = await PostAdvanceAsync(client, id, alteredPlan))
                Assert.Equal(HttpStatusCode.BadRequest, suppressed.StatusCode);
            var afterSuppression = await client.GetFromJsonAsync<JsonElement>(
                $"/api/expeditions/{id:D}");
            Assert.Equal(second.ExpeditionVersion,
                afterSuppression.GetProperty("version").GetInt64());

            using var valid = await PostAdvanceAsync(client, id,
                Request(second, second.ExpeditionVersion, heading, second.GeneratedResolutionId));
            valid.EnsureSuccessStatusCode();
            var advanced = await valid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(second.ExpeditionVersion + 1, advanced.GetProperty("version").GetInt64());
            Assert.Contains(advanced.GetProperty("history").EnumerateArray(),
                value => value.GetProperty("kind").GetString() == "ProcedureResolutionHelperConsumed"
                    && value.GetProperty("message").GetString()!.Contains(
                        second.GeneratedResolutionId!.Value.ToString("D"), StringComparison.Ordinal));
            Assert.Contains(advanced.GetProperty("history").EnumerateArray(),
                value => value.GetProperty("kind").GetString() == "ResolutionProvenanceRecorded"
                    && value.GetProperty("message").GetString()!.Contains("AutomaticRoll", StringComparison.Ordinal));
            using var reused = await PostAdvanceAsync(client, id,
                Request(second, advanced.GetProperty("version").GetInt64(),
                    heading, second.GeneratedResolutionId));
            Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);

            var persisted = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{id:D}");
            Assert.Equal(advanced.GetProperty("version").GetInt64(),
                persisted.GetProperty("version").GetInt64());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static WorldPoint Heading(JsonElement topology)
    {
        var center = topology.GetProperty("center");
        var side = topology.GetProperty("interfaces")[0];
        return new WorldPoint(
            (side.GetProperty("start").GetProperty("x").GetDouble()
                + side.GetProperty("end").GetProperty("x").GetDouble()) / 2
                - center.GetProperty("x").GetDouble(),
            (side.GetProperty("start").GetProperty("y").GetDouble()
                + side.GetProperty("end").GetProperty("y").GetDouble()) / 2
                - center.GetProperty("y").GetDouble());
    }

    private static AdvanceCellWatchRequest Request(
        CellProcedureResolutionHelperResult generated, long version,
        WorldPoint heading, Guid? generationId) => new(
            version,
            new CellWatchTravelPlan(heading, false, true,
                TravelModeSelection.Normal, NavigationAidSelection.None),
            new CellWatchAdvanceInputs(
                generated.Travel!, generated.Navigation, generated.Encounter),
            generationId);

    private static async Task<HttpResponseMessage> PostAdvanceAsync(
        HttpClient client, Guid id, AdvanceCellWatchRequest request) =>
        await client.PostAsJsonAsync($"/api/expeditions/{id:D}/cells/advance", request, Json);

    private static async Task<CellProcedureResolutionHelperResult> GenerateAsync(
        HttpClient client, Guid id, long version)
    {
        using var response = await PostGenerateAsync(client, id, version);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CellProcedureResolutionHelperResult>(Json))!;
    }

    private static Task<HttpResponseMessage> PostGenerateAsync(HttpClient client, Guid id, long version) =>
        client.PostAsJsonAsync($"/api/expeditions/{id:D}/cells/resolution-helper",
            new ResolveCellProcedureInputsRequest(
                version, new DistanceMeasure(1, DistanceUnit.Miles),
                NavigationDifficultyClass: -100), Json);

    private static async Task<JsonElement> StartAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/expeditions/cells", new
        {
            name,
            procedureKey = "alexandrian-advanced",
            context = new
            {
                kind = "AbstractHex",
                name,
                orientation = "PointyTop",
                hexCenterDistance = 12,
                distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
            },
            startHex = new { q = 0, r = 0 }
        }, Json);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static void AssertAutomaticResult(CellProcedureResolutionHelperResult generated)
    {
        Assert.NotNull(generated.Travel);
        Assert.Equal(ResolutionSource.AutomaticRoll, generated.Travel.Provenance.Source);
        Assert.Equal(1, generated.Travel.ExpectedDistance!.Value.Value);
        Assert.Equal(DistanceUnit.Miles, generated.Travel.ExpectedDistance.Value.Unit);
        Assert.InRange(generated.Travel.ActualDistance!.Value.Value, 0.5, 1.5);
        Assert.Equal(ResolutionSource.AutomaticRoll, generated.Navigation!.Provenance.Source);
        Assert.Equal(NavigationCheckOutcome.Succeeded, generated.Navigation.Outcome);
        Assert.Null(generated.Navigation.VeerDegreesOnFailure);
        Assert.Equal(ResolutionSource.AutomaticRoll, generated.Encounter!.Provenance.Source);
        Assert.Contains(generated.Encounter.Kind,
            new[] { EncounterOutcomeKind.None, EncounterOutcomeKind.WanderingEncounter,
                EncounterOutcomeKind.ManualCustom });
    }
}
