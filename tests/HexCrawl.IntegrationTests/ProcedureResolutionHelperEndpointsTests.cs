using System.Net.Http.Json;
using System.Text.Json;

namespace HexCrawl.IntegrationTests;

public sealed class ProcedureResolutionHelperEndpointsTests
{
    [Fact]
    public async Task HelperReturnsDraftsWithoutMutatingSessionAndExistingAdvanceConsumesThem()
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

            using var helperResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/resolution-helper",
                new
                {
                    expectedVersion = initialVersion,
                    expectedDistance = 12,
                    suppressesNavigationCheck = false,
                    deliberateDoubleBack = false,
                    navigationDifficultyClass = -100,
                    navigationModifier = 0,
                    failureVeerSteps = 1
                });
            helperResponse.EnsureSuccessStatusCode();
            var helper = await helperResponse.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(initialVersion, helper.GetProperty("expeditionVersion").GetInt64());
            var travel = helper.GetProperty("travel");
            Assert.Equal("AutomaticRoll", travel.GetProperty("provenance").GetProperty("source").GetString());
            Assert.Equal(12d, travel.GetProperty("expectedDistance").GetDouble());
            Assert.InRange(travel.GetProperty("actualDistance").GetDouble(), 6d, 18d);

            var navigation = helper.GetProperty("navigation");
            Assert.Equal("Succeeded", navigation.GetProperty("outcome").GetString());
            Assert.Equal("AutomaticRoll", navigation.GetProperty("provenance").GetProperty("source").GetString());

            var encounter = helper.GetProperty("encounter");
            Assert.Equal("AutomaticRoll", encounter.GetProperty("provenance").GetProperty("source").GetString());
            var encounterKind = encounter.GetProperty("kind").GetString()!;
            Assert.Contains(encounterKind, new[] { "None", "WanderingEncounter", "ManualCustom" });

            var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(initialVersion, unchanged.GetProperty("version").GetInt64());
            Assert.Equal(0, unchanged.GetProperty("history").GetArrayLength());

            var advance = new Dictionary<string, object?>
            {
                ["expectedVersion"] = initialVersion,
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
                ["deliberateDoubleBack"] = false,
                ["continueAcrossBoundaries"] = true
            };

            advance["encounterOutcome"] = encounterKind;
            advance["encounterResolutionSource"] = "AutomaticRoll";
            advance["encounterResolutionNote"] = encounter.GetProperty("provenance").GetProperty("note").GetString();
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
            Assert.Equal(initialVersion + 1, applied.GetProperty("version").GetInt64());
            Assert.Contains(
                applied.GetProperty("history").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == "ResolutionProvenanceRecorded"
                    && item.GetProperty("message").GetString()!.Contains("AutomaticRoll", StringComparison.Ordinal));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
