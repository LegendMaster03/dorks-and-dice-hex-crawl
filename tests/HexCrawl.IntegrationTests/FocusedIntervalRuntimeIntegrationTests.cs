using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.IntegrationTests;

public sealed class FocusedIntervalRuntimeIntegrationTests
{
    [Fact]
    public async Task Dnd2024StructuralProcedureExposesOneHourFocusedIntervalAndCanRecordWatch()
    {
        var database = TestWebHost.NewDatabasePath();

        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartAsync(client, "dnd-2024", "Focused D&D interval");
            var expeditionId = started.GetProperty("id").GetGuid();

            Assert.Null(Runtime(started));
            Assert.False(started.GetProperty("procedure").GetProperty("isExecutable").GetBoolean());
            AssertFocusedInterval(started, "Supported", 1);

            using var response = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                WatchRequest(started.GetProperty("version").GetInt64(), 0.5));
            response.EnsureSuccessStatusCode();
            var active = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(1, active.GetProperty("expedition").GetProperty("activeWatchTotalHours").GetDouble());
            Assert.Equal(0.5, active.GetProperty("expedition").GetProperty("activeWatchRemainingHours").GetDouble());
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

            Assert.Null(Runtime(started));
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
                WatchRequest(assigned.GetProperty("version").GetInt64(), 3));
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

            await MutateStoredProcedureAsync(
                factory,
                expeditionId,
                procedure => WithIntervalDuration(procedure, TimeSpan.FromHours(7)));

            var pinned = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            Assert.Null(Runtime(pinned));
            AssertFocusedInterval(pinned, "Supported", 7);

            using var response = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                WatchRequest(pinned.GetProperty("version").GetInt64(), 1));
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
    public async Task OneRingExposesNoFocusedIntervalAndRejectsWatchWithoutChangingJourneyRoles()
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

            Assert.Null(Runtime(started));
            Assert.False(started.GetProperty("procedure").GetProperty("isExecutable").GetBoolean());
            AssertFocusedInterval(started, "None", null);
            Assert.Equal("Role", started.GetProperty("participantActivityPolicy").GetProperty("assignmentScope").GetString());

            var updated = await UpdateAssignmentAsync(
                client,
                expeditionId,
                started.GetProperty("version").GetInt64(),
                memberId,
                assignmentId,
                "Role",
                null,
                "guide");

            using var watchResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                WatchRequest(updated.GetProperty("version").GetInt64(), 1));
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

    [Fact]
    public async Task UnsupportedFutureIntervalPreservesStoredIdentityAndDoesNotExposeDuration()
    {
        var database = TestWebHost.NewDatabasePath();

        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var started = await StartAsync(client, "dnd-2024", "Future interval");
            var expeditionId = started.GetProperty("id").GetGuid();

            await MutateStoredProcedureAsync(factory, expeditionId, WithFutureInterval);

            var future = await client.GetFromJsonAsync<JsonElement>($"/api/expeditions/{expeditionId:D}");
            var policy = future.GetProperty("procedure").GetProperty("focusedIntervalPolicy");
            Assert.Equal("Unsupported", policy.GetProperty("support").GetString());
            Assert.Equal(JsonValueKind.Null, policy.GetProperty("intervalHours").ValueKind);
            Assert.Equal("future-interval-policy", policy.GetProperty("mechanicKey").GetString());
            Assert.Equal(99, policy.GetProperty("mechanicVersion").GetInt32());
            Assert.Equal("future.interval.handler", policy.GetProperty("executionHandler").GetString());

            using var watchResponse = await client.PostAsJsonAsync(
                $"/api/expeditions/{expeditionId:D}/assistants/watch",
                WatchRequest(future.GetProperty("version").GetInt64(), 1));
            Assert.Equal(HttpStatusCode.BadRequest, watchResponse.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static JsonElement? Runtime(JsonElement expedition)
    {
        var runtime = expedition.GetProperty("procedure").GetProperty("runtime");
        return runtime.ValueKind == JsonValueKind.Null ? null : runtime;
    }

    private static object WatchRequest(long expectedVersion, double elapsedHours) => new
    {
        expectedVersion,
        elapsedHours,
        resolutionSource = "ProcedureDefault"
    };

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

    private static async Task MutateStoredProcedureAsync(
        WebApplicationFactory<Program> factory,
        Guid expeditionId,
        Func<CampaignProcedure, CampaignProcedure> mutate)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHexCrawlStore>();
        var stored = await store.GetExpeditionAsync(expeditionId, "integration-user")
            ?? throw new InvalidOperationException("Expected the started expedition to exist.");
        var save = await store.SaveExpeditionAsync(
            stored with { CampaignProcedure = mutate(stored.CampaignProcedure) },
            stored.Version);
        Assert.Equal(SaveOutcome.Saved, save.Outcome);
    }

    private static CampaignProcedure WithIntervalDuration(CampaignProcedure procedure, TimeSpan duration)
    {
        var interval = IntervalModule(procedure) with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["durationTicks"] = duration.Ticks.ToString(CultureInfo.InvariantCulture)
            }
        };
        return ReplaceInterval(procedure, interval);
    }

    private static CampaignProcedure WithFutureInterval(CampaignProcedure procedure)
    {
        var interval = IntervalModule(procedure);
        return ReplaceInterval(
            procedure,
            interval with
            {
                Mechanic = interval.Mechanic with
                {
                    Key = "future-interval-policy",
                    Version = 99,
                    ExecutionHandler = "future.interval.handler"
                },
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["durationTicks"] = TimeSpan.FromHours(7).Ticks.ToString(CultureInfo.InvariantCulture)
                }
            });
    }

    private static MaterializedProcedureModule IntervalModule(CampaignProcedure procedure) =>
        procedure.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);

    private static CampaignProcedure ReplaceInterval(
        CampaignProcedure procedure,
        MaterializedProcedureModule interval) =>
        procedure with
        {
            Modules = procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule ? interval : module)
                .ToArray()
        };

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
