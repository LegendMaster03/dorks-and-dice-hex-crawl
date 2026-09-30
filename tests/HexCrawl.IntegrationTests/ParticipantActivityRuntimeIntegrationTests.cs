using System.Net.Http.Json;
using System.Text.Json;

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
}
