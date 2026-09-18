using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class ProcedureResolutionHelperEndpointsTests
{
    [Fact]
    public async Task HelperPersistsEveryAutomaticAttemptBeforeExistingAdvanceConsumesOne()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var startResponse = await client.PostAsJsonAsync("/api/expeditions", new
            {
                name = "Helper integration",
                procedureKey = "alexandrian-advanced",
                context = new
                {
                    kind = "AbstractHex",
                    name = "Mapless helper",
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
            var initialVersion = started.GetProperty("version").GetInt64();
            Assert.NotEqual(JsonValueKind.Null, started.GetProperty("profile").GetProperty("resolutionHelpers").ValueKind);

            var first = await GenerateAsync(client, expeditionId, initialVersion);
            Assert.Equal(initialVersion + 1, first.GetProperty("expeditionVersion").GetInt64());
            Assert.Equal(1, first.GetProperty("auditSequence").GetInt64());
            AssertAutomaticResult(first);

            var afterFirst = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(initialVersion + 1, afterFirst.GetProperty("version").GetInt64());
            Assert.Equal(0d, afterFirst.GetProperty("expedition").GetProperty("distanceTraveled").GetProperty("value").GetDouble());
            var firstAudit = Assert.Single(afterFirst.GetProperty("history").EnumerateArray());
            Assert.Equal("ProcedureResolutionHelperGenerated", firstAudit.GetProperty("kind").GetString());
            Assert.Contains("attempt #1", firstAudit.GetProperty("message").GetString()!, StringComparison.Ordinal);

            // The old version can not be used to obtain an unrecorded reroll.
            using var staleResponse = await PostGenerateAsync(client, expeditionId, initialVersion);
            Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

            var firstVersion = first.GetProperty("expeditionVersion").GetInt64();
            var second = await GenerateAsync(client, expeditionId, firstVersion);
            Assert.Equal(initialVersion + 2, second.GetProperty("expeditionVersion").GetInt64());
            Assert.Equal(2, second.GetProperty("auditSequence").GetInt64());
            AssertAutomaticResult(second);

            var afterSecond = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(initialVersion + 2, afterSecond.GetProperty("version").GetInt64());
            Assert.Equal(0d, afterSecond.GetProperty("expedition").GetProperty("distanceTraveled").GetProperty("value").GetDouble());
            var helperAudits = afterSecond.GetProperty("history").EnumerateArray()
                .Where(item => item.GetProperty("kind").GetString() == "ProcedureResolutionHelperGenerated")
                .ToArray();
            Assert.Equal(2, helperAudits.Length);
            Assert.Contains("attempt #1", helperAudits[0].GetProperty("message").GetString()!, StringComparison.Ordinal);
            Assert.Contains("attempt #2", helperAudits[1].GetProperty("message").GetString()!, StringComparison.Ordinal);

            var travel = second.GetProperty("travel");
            var navigation = second.GetProperty("navigation");
            var encounter = second.GetProperty("encounter");
            var encounterKind = encounter.GetProperty("kind").GetString()!;
            var applyVersion = second.GetProperty("expeditionVersion").GetInt64();

            var advance = new Dictionary<string, object?>
            {
                ["expectedVersion"] = applyVersion,
                ["intendedDirection"] = 0,
                ["paceKey"] = "normal",
                ["activities"] = Array.Empty<string>(),
                ["navigationAidKey"] = "none",
                ["suppressesNavigationCheck"] = false,
                ["resetsVeerAtBoundary"] = false,
                ["expectedDistance"] = travel.GetProperty("expectedDistance").GetDouble(),
                ["actualDistance"] = travel.GetProperty("actualDistance").GetDouble(),
                ["resolutionSource"] = "ManualRoll",
                ["travelResolutionSource"] = "AutomaticRoll",
                ["travelResolutionNote"] = travel.GetProperty("provenance").GetProperty("note").GetString(),
                ["navigationOutcome"] = navigation.GetProperty("outcome").GetString(),
                ["navigationResolutionSource"] = "AutomaticRoll",
                ["navigationResolutionNote"] = navigation.GetProperty("provenance").GetProperty("note").GetString(),
                ["encounterOutcome"] = encounterKind,
                ["encounterResolutionSource"] = "AutomaticRoll",
                ["encounterResolutionNote"] = encounter.GetProperty("provenance").GetProperty("note").GetString(),
                ["deliberateDoubleBack"] = false,
                ["continueAcrossBoundaries"] = true
            };

            if (encounterKind != "None")
            {
                advance["encounterHour"] = encounter.GetProperty("occursAtHours").GetDouble();
                var note = encounter.GetProperty("note");
                if (note.ValueKind == JsonValueKind.String) advance["encounterNote"] = note.GetString();
            }

            using var advanceResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/advance",
                advance);
            advanceResponse.EnsureSuccessStatusCode();
            var applied = await advanceResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(applyVersion + 1, applied.GetProperty("version").GetInt64());
            Assert.Contains(
                applied.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "ResolutionProvenanceRecorded"
                    && item.GetProperty("message").GetString()!.Contains("AutomaticRoll", StringComparison.Ordinal)
                    && item.GetProperty("message").GetString()!.Contains("audit event #2", StringComparison.Ordinal));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> GenerateAsync(HttpClient client, Guid expeditionId, long expectedVersion)
    {
        using var response = await PostGenerateAsync(client, expeditionId, expectedVersion);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> PostGenerateAsync(HttpClient client, Guid expeditionId, long expectedVersion) =>
        client.PostAsJsonAsync(
            $"/api/expeditions/{expeditionId:D}/resolution-helper",
            new
            {
                expectedVersion,
                expectedDistance = 12,
                suppressesNavigationCheck = false,
                deliberateDoubleBack = false,
                navigationDifficultyClass = -100,
                navigationModifier = 0,
                failureVeerSteps = 1
            });

    private static void AssertAutomaticResult(JsonElement helper)
    {
        var travel = helper.GetProperty("travel");
        Assert.Equal("AutomaticRoll", travel.GetProperty("provenance").GetProperty("source").GetString());
        Assert.Contains("audit event #", travel.GetProperty("provenance").GetProperty("note").GetString()!, StringComparison.Ordinal);
        Assert.Equal(12d, travel.GetProperty("expectedDistance").GetDouble());
        Assert.InRange(travel.GetProperty("actualDistance").GetDouble(), 6d, 18d);

        var navigation = helper.GetProperty("navigation");
        Assert.Equal("Succeeded", navigation.GetProperty("outcome").GetString());
        Assert.Equal("AutomaticRoll", navigation.GetProperty("provenance").GetProperty("source").GetString());
        Assert.Contains("audit event #", navigation.GetProperty("provenance").GetProperty("note").GetString()!, StringComparison.Ordinal);

        var encounter = helper.GetProperty("encounter");
        Assert.Equal("AutomaticRoll", encounter.GetProperty("provenance").GetProperty("source").GetString());
        Assert.Contains("audit event #", encounter.GetProperty("provenance").GetProperty("note").GetString()!, StringComparison.Ordinal);
        Assert.Contains(
            encounter.GetProperty("kind").GetString()!,
            new[] { "None", "WanderingEncounter", "ManualCustom" });
    }
}
