using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.IntegrationTests;

public sealed class ParticipantActivityRuntimeIntegrationTests
{
    [Fact]
    public async Task ActiveNonSpatialIntervalSnapshotsAssignmentsAcrossPartyEditsAndRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        var memberId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        Guid expeditionId;

        try
        {
            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var started = await StartAsync(client, "dnd-2024", "Typed interval");
                expeditionId = started.GetProperty("id").GetGuid();

                Assert.Equal(JsonValueKind.Null, started.GetProperty("procedure").GetProperty("runtime").ValueKind);
                Assert.Equal(false, started.GetProperty("procedure").GetProperty("isExecutable").GetBoolean());
                AssertFocusedInterval(started, "Supported", 1);

                var withForage = await UpdateAssignmentAsync(
                    client,
                    expeditionId,
                    started.GetProperty("version").GetInt64(),
                    memberId,
                    assignmentId,
                    "Participant",
                    "forage",
                    null);

                using var watchResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/assistants/watch",
                    new
                    {
                        expectedVersion = withForage.GetProperty("version").GetInt64(),
                        elapsedHours = 0.5,
                        resolutionSource = "ProcedureDefault"
                    });
                watchResponse.EnsureSuccessStatusCode();
                var active = await watchResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(1, active.GetProperty("expedition").GetProperty("activeWatchTotalHours").GetDouble());
                var activeAssignment = active.GetProperty("expedition")
                    .GetProperty("activeActivityAssignments")[0];
                Assert.Equal("forage", activeAssignment.GetProperty("activityKey").GetString());

                var changedStandingState = await UpdateAssignmentAsync(
                    client,
                    expeditionId,
                    active.GetProperty("version").GetInt64(),
                    memberId,
                    assignmentId,
                    "Participant",
                    "watch",
                    null);

                Assert.Equal(
                    "watch",
                    changedStandingState.GetProperty("party")
                        .GetProperty("activityAssignments")[0]
                        .GetProperty("activityKey").GetString());
                Assert.Equal(
                    "forage",
                    changedStandingState.GetProperty("expedition")
                        .GetProperty("activeActivityAssignments")[0]
                        .GetProperty("activityKey").GetString());
            }

            using (var factory = TestWebHost.Create(database))
            using (var client = factory.CreateClient())
            {
                var restarted = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
                Assert.Equal(
                    "watch",
                    restarted.GetProperty("party")
                        .GetProperty("activityAssignments")[0]
                        .GetProperty("activityKey").GetString());
                Assert.Equal(
                    "forage",
                    restarted.GetProperty("expedition")
                        .GetProperty("activeActivityAssignments")[0]
                        .GetProperty("activityKey").GetString());
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ForbiddenLandsStructuralProcedureUsesSixHourFocusedIntervalAndSnapshotsQuarterDayAssignment()
    {
        var database = TestWebHost.NewDatabasePath();
        var memberId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartAsync(client, "forbidden-lands", "Quarter-day interval");
            var expeditionId = started.GetProperty("id").GetGuid();

            Assert.Equal(JsonValueKind.Null, started.GetProperty("procedure").GetProperty("runtime").ValueKind);
            Assert.False(started.GetProperty("procedure").GetProperty("isExecutable").GetBoolean());
            AssertFocusedInterval(started, "Supported", 6);
            Assert.Equal(
                "quarter-day",
                started.GetProperty("participantActivityPolicy").GetProperty("activityBudgetModel").GetString());

            var assigned = await UpdateAssignmentAsync(
                client,
                expeditionId,
                started.GetProperty("version").GetInt64(),
                memberId,
                assignmentId,
                "Participant",
                "forage",
                "lookout");

            using var response = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                new
                {
                    expectedVersion = assigned.GetProperty("version").GetInt64(),
                    elapsedHours = 3,
                    resolutionSource = "ProcedureDefault"
                });
            response.EnsureSuccessStatusCode();
            var active = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(6, active.GetProperty("expedition").GetProperty("activeWatchTotalHours").GetDouble());
            Assert.Equal(3, active.GetProperty("expedition").GetProperty("activeWatchRemainingHours").GetDouble());
            var snapshot = active.GetProperty("expedition").GetProperty("activeActivityAssignments")[0];
            Assert.Equal("forage", snapshot.GetProperty("activityKey").GetString());
            Assert.Equal("lookout", snapshot.GetProperty("roleKey").GetString());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task StoredSevenHourIntervalDrivesProjectionAndActualBookkeepingInsteadOfCatalogDefault()
    {
        var database = TestWebHost.NewDatabasePath();

        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartAsync(client, "dnd-2024", "Pinned seven-hour interval");
            var expeditionId = started.GetProperty("id").GetGuid();

            using (var scope = factory.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<IHexCrawlStore>();
                var stored = await store.GetExpeditionAsync(expeditionId, "integration-user")
                    ?? throw new InvalidOperationException("Expected the started expedition to exist.");
                var save = await store.SaveExpeditionAsync(
                    stored with
                    {
                        CampaignProcedure = WithIntervalDuration(
                            stored.CampaignProcedure,
                            TimeSpan.FromHours(7))
                    },
                    stored.Version);
                Assert.Equal(SaveOutcome.Saved, save.Outcome);
            }

            var pinned = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(JsonValueKind.Null, pinned.GetProperty("procedure").GetProperty("runtime").ValueKind);
            AssertFocusedInterval(pinned, "Supported", 7);

            using var response = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                new
                {
                    expectedVersion = pinned.GetProperty("version").GetInt64(),
                    elapsedHours = 1,
                    resolutionSource = "ProcedureDefault"
                });
            response.EnsureSuccessStatusCode();
            var active = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(7, active.GetProperty("expedition").GetProperty("activeWatchTotalHours").GetDouble());
            Assert.Equal(1, active.GetProperty("expedition").GetProperty("activeWatchElapsedHours").GetDouble());
            Assert.Equal(6, active.GetProperty("expedition").GetProperty("activeWatchRemainingHours").GetDouble());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task OneRingJourneyRolesEditWithoutFabricatedInterval()
    {
        var database = TestWebHost.NewDatabasePath();
        var memberId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartAsync(client, "the-one-ring-2e", "Role journey");
            var expeditionId = started.GetProperty("id").GetGuid();

            Assert.Equal(
                "Role",
                started.GetProperty("participantActivityPolicy")
                    .GetProperty("assignmentScope").GetString());
            Assert.Equal(JsonValueKind.Null, started.GetProperty("procedure").GetProperty("runtime").ValueKind);
            Assert.False(started.GetProperty("procedure").GetProperty("isExecutable").GetBoolean());
            AssertFocusedInterval(started, "None", null);
            Assert.Equal(JsonValueKind.Null, started.GetProperty("expedition").GetProperty("activeWatchNumber").ValueKind);

            var updated = await UpdateAssignmentAsync(
                client,
                expeditionId,
                started.GetProperty("version").GetInt64(),
                memberId,
                assignmentId,
                "Role",
                null,
                "guide");

            var assignment = updated.GetProperty("party").GetProperty("activityAssignments")[0];
            Assert.Equal(memberId, assignment.GetProperty("participantId").GetGuid());
            Assert.Equal("guide", assignment.GetProperty("roleKey").GetString());
            Assert.Equal(JsonValueKind.Null, assignment.GetProperty("activityKey").ValueKind);
            Assert.Equal(JsonValueKind.Null, updated.GetProperty("expedition").GetProperty("activeWatchNumber").ValueKind);

            using var watchResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                new
                {
                    expectedVersion = updated.GetProperty("version").GetInt64(),
                    elapsedHours = 1,
                    resolutionSource = "ProcedureDefault"
                });
            Assert.Equal(HttpStatusCode.BadRequest, watchResponse.StatusCode);

            var afterRejectedWatch = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Equal(JsonValueKind.Null, afterRejectedWatch.GetProperty("expedition").GetProperty("activeWatchNumber").ValueKind);
            Assert.Equal("guide", afterRejectedWatch.GetProperty("party").GetProperty("activityAssignments")[0].GetProperty("roleKey").GetString());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task<JsonElement> StartAsync(HttpClient client, string procedureKey, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/expeditions", new
        {
            name,
            procedureKey,
            context = new { kind = "NonSpatial", name = "Procedure state" }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> UpdateAssignmentAsync(
        HttpClient client,
        Guid expeditionId,
        long expectedVersion,
        Guid memberId,
        Guid assignmentId,
        string scope,
        string? activityKey,
        string? roleKey)
    {
        using var response = await client.PutAsJsonAsync(
            $"/api/expeditions/{expeditionId:D}/party",
            new
            {
                expectedVersion,
                members = new[]
                {
                    new
                    {
                        id = memberId,
                        name = "Alice",
                        externalCharacterId = (string?)null,
                        countsTowardPartyMovement = true
                    }
                },
                activityAssignments = new[]
                {
                    new
                    {
                        id = assignmentId,
                        scope,
                        participantId = memberId,
                        activityKey,
                        roleKey,
                        note = (string?)null
                    }
                }
            });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static CampaignProcedure WithIntervalDuration(CampaignProcedure procedure, TimeSpan duration)
    {
        var interval = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule) with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["durationTicks"] = duration.Ticks.ToString(CultureInfo.InvariantCulture)
            }
        };
        return procedure with
        {
            Modules = procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule ? interval : module)
                .ToArray()
        };
    }

    private static void AssertFocusedInterval(JsonElement expedition, string support, double? intervalHours)
    {
        var policy = expedition.GetProperty("procedure").GetProperty("focusedIntervalPolicy");
        Assert.Equal(support, policy.GetProperty("support").GetString());
        if (intervalHours.HasValue)
        {
            Assert.Equal(intervalHours.Value, policy.GetProperty("intervalHours").GetDouble());
            Assert.Equal("fixed-interval-duration", policy.GetProperty("mechanicKey").GetString());
            Assert.Equal(1, policy.GetProperty("mechanicVersion").GetInt32());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, policy.GetProperty("intervalHours").ValueKind);
        }
    }
}
