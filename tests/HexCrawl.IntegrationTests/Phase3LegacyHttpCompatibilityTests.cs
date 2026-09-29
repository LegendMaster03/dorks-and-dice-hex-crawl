using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class Phase3LegacyHttpCompatibilityTests
{
    [Fact]
    public async Task StandaloneGenericOnlyPresetIsRejectedBeforePersistence()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/expeditions", new
            {
                name = "Generic-only journey",
                procedureKey = CrawlProcedureCatalog.OneRing2ePresetKey,
                context = new
                {
                    kind = "NonSpatial",
                    name = "Journey"
                }
            });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("generic-only", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

            var expeditions = await client.GetFromJsonAsync<JsonElement>("/api/expeditions");
            Assert.Equal(0, expeditions.GetArrayLength());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task WorldBoundGenericOnlyPresetIsRejectedBeforePersistence()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var world = await CreateWorld(client);
            var worldId = world.GetProperty("id").GetGuid();

            using var response = await client.PostAsJsonAsync($"/api/overworlds/{worldId:D}/expeditions", new
            {
                name = "Generic-only journey",
                procedureKey = CrawlProcedureCatalog.OneRing2ePresetKey,
                presentationKey = "exploration-map",
                startHex = new { q = 0, r = 0 }
            });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("generic-only", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

            var expeditions = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}/expeditions");
            Assert.Equal(0, expeditions.GetArrayLength());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ExistingGenericOnlyExpeditionReturnsDeliberateConflictOnLegacyDetailGet()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var store = new PostgresHexCrawlStore(database);
            await store.InitializeAsync();
            var sessions = new CrawlSessionService(store);
            var seeded = await sessions.StartAsync(
                "integration-user",
                new StartStandaloneCrawlSessionCommand(
                    "Seeded generic-only journey",
                    CrawlProcedureCatalog.OneRing2ePresetKey,
                    new NonSpatialCrawlSessionContext("Journey")));

            Assert.Null(seeded.CompatibilityProfile);
            Assert.NotNull(seeded.CampaignProcedure);

            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            var expeditions = await client.GetFromJsonAsync<JsonElement>("/api/expeditions");
            var summary = Assert.Single(expeditions.EnumerateArray());
            Assert.Equal(seeded.Id, summary.GetProperty("id").GetGuid());
            Assert.Equal(seeded.Name, summary.GetProperty("name").GetString());

            using var response = await client.GetAsync($"/api/expeditions/{seeded.Id:D}");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("generic-only", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CampaignProcedure", error.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ExistingGenericOnlyExpeditionRejectsLegacyWorkbenchMutationsBeforePersistence()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var store = new PostgresHexCrawlStore(database);
            await store.InitializeAsync();
            var sessions = new CrawlSessionService(store);
            var seeded = await sessions.StartAsync(
                "integration-user",
                new StartStandaloneCrawlSessionCommand(
                    "Mutation boundary proof",
                    CrawlProcedureCatalog.OneRing2ePresetKey,
                    new NonSpatialCrawlSessionContext("Journey")));
            var before = Assert.IsType<StoredExpedition>(
                await store.GetExpeditionAsync(seeded.Id, "integration-user"));

            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            var expeditions = await client.GetFromJsonAsync<JsonElement>("/api/expeditions");
            var summary = Assert.Single(expeditions.EnumerateArray());
            Assert.Equal(seeded.Id, summary.GetProperty("id").GetGuid());

            await AssertGenericOnlyConflictAsync(
                await client.GetAsync($"/api/expeditions/{seeded.Id:D}"));

            var memberId = Guid.NewGuid();
            await AssertGenericOnlyConflictAsync(
                await client.PutAsJsonAsync($"/api/expeditions/{seeded.Id:D}/party", new
                {
                    expectedVersion = seeded.Version,
                    members = new[]
                    {
                        new
                        {
                            id = memberId,
                            name = "Must not persist",
                            externalCharacterId = (string?)null,
                            countsTowardPartyMovement = true
                        }
                    }
                }));

            await AssertGenericOnlyConflictAsync(
                await client.PostAsJsonAsync($"/api/expeditions/{seeded.Id:D}/discover", new
                {
                    expectedVersion = seeded.Version,
                    subjectId = Guid.NewGuid(),
                    subjectType = "Location",
                    source = "must-not-persist"
                }));

            await AssertGenericOnlyConflictAsync(
                await client.PostAsJsonAsync($"/api/expeditions/{seeded.Id:D}/assistants/watch", new
                {
                    expectedVersion = seeded.Version,
                    elapsedHours = 2,
                    resolutionSource = "ManualRoll",
                    note = "must-not-persist"
                }));

            await AssertGenericOnlyConflictAsync(
                await client.PostAsJsonAsync($"/api/expeditions/{seeded.Id:D}/assistants/travel", new
                {
                    expectedVersion = seeded.Version,
                    elapsedHours = 1,
                    distance = 1,
                    resultingHex = new { q = 0, r = 0 },
                    completeWatch = false,
                    resolutionSource = "ManualRoll",
                    note = "must-not-persist"
                }));

            await AssertGenericOnlyConflictAsync(
                await client.PostAsJsonAsync($"/api/expeditions/{seeded.Id:D}/assistants/navigation", new
                {
                    expectedVersion = seeded.Version,
                    isLost = true,
                    veerSteps = 1,
                    intendedDirection = 0,
                    resolutionSource = "ManualRoll",
                    note = "must-not-persist"
                }));

            await AssertGenericOnlyConflictAsync(
                await client.PostAsJsonAsync($"/api/expeditions/{seeded.Id:D}/assistants/encounters", new
                {
                    expectedVersion = seeded.Version,
                    outcome = "WanderingEncounter",
                    resolutionSource = "ManualRoll",
                    note = "must-not-persist"
                }));

            await AssertGenericOnlyConflictAsync(
                await client.PostAsJsonAsync($"/api/expeditions/{seeded.Id:D}/advance", new
                {
                    expectedVersion = seeded.Version,
                    intendedDirection = 0,
                    paceKey = "normal",
                    activities = Array.Empty<string>(),
                    navigationAidKey = "none",
                    resolutionSource = "ProcedureDefault"
                }));

            var after = Assert.IsType<StoredExpedition>(
                await store.GetExpeditionAsync(seeded.Id, "integration-user"));
            AssertPersistentStateUnchanged(before, after);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static async Task AssertGenericOnlyConflictAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("generic-only", error.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CampaignProcedure", error.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
    }

    private static void AssertPersistentStateUnchanged(StoredExpedition before, StoredExpedition after)
    {
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.PauseReason, after.PauseReason);
        Assert.Equal(before.RemainingWatchTime, after.RemainingWatchTime);

        Assert.Equal(before.Party.Members.ToArray(), after.Party.Members.ToArray());
        Assert.Equal(before.Party.MarchingOrder.ToArray(), after.Party.MarchingOrder.ToArray());
        Assert.Equal(before.Party.WatchList.ToArray(), after.Party.WatchList.ToArray());
        Assert.Equal(before.Party.StandingOrders.ToArray(), after.Party.StandingOrders.ToArray());
        Assert.Equal(before.Party.DefaultNavigatorMemberId, after.Party.DefaultNavigatorMemberId);
        Assert.Equal(before.Party.BaseMovement, after.Party.BaseMovement);

        Assert.Null(before.Knowledge);
        Assert.Null(after.Knowledge);
        Assert.Equal(
            before.GeneratedProcedureResolutions.ToArray(),
            after.GeneratedProcedureResolutions.ToArray());

        var beforeRuntime = Assert.IsType<NonSpatialSessionState>(before.Runtime);
        var afterRuntime = Assert.IsType<NonSpatialSessionState>(after.Runtime);
        Assert.Equal(beforeRuntime.ElapsedTime, afterRuntime.ElapsedTime);
        Assert.Equal(beforeRuntime.CompletedWatches, afterRuntime.CompletedWatches);
        Assert.Equal(beforeRuntime.ActiveWatch, afterRuntime.ActiveWatch);
        Assert.Equal(beforeRuntime.History.ToArray(), afterRuntime.History.ToArray());
    }

    private static async Task<JsonElement> CreateWorld(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/overworlds", new
        {
            name = "Phase 3 HTTP boundary",
            orientation = "PointyTop",
            origin = new { x = 0, y = 0 },
            rotationDegrees = 0,
            hexRadiusWorldUnits = 1,
            neighborCenterDistance = 12,
            distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
