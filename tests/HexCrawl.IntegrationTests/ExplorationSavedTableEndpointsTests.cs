using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;

namespace HexCrawl.IntegrationTests;

public sealed class ExplorationSavedTableEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static object Provenance(string key) => new
    {
        sourceKind = "Dm", sourceKey = key
    };

    private static object Table(string source, string low, string high) => new
    {
        scope = "Location", tableKey = "location",
        dieSides = 6, sourceReference = source,
        rows = new[]
        {
            new { from = 1, through = 3, resultText = low },
            new { from = 4, through = 6, resultText = high }
        }
    };

    [Fact]
    public async Task SavedLibrarySupportsReuseMutationDeletionAndDurableResults()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            long version;
            var saveId = Guid.NewGuid();
            var visitId = Guid.NewGuid();
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var start = await client.PostAsJsonAsync("/api/expeditions/cells", new
                {
                    name = "Saved GM roll tables",
                    procedureKey = CrawlProcedureCatalog.WastelandWarfarePresetKey,
                    context = new { kind = "MaplessSpatial", name = "Mapless square session" }
                }, JsonOptions);
                start.EnsureSuccessStatusCode();
                var created = await start.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = created.GetProperty("id").GetGuid();
                version = created.GetProperty("version").GetInt64();

                var save = new
                {
                    expectedVersion = version,
                    operationId = saveId,
                    scope = "Location",
                    tableKey = "location",
                    definition = Table("GM source 1", "Synthetic outpost", "Synthetic ruins"),
                    provenance = Provenance("save-table")
                };
                using var added = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-tables", save, JsonOptions);
                added.EnsureSuccessStatusCode();
                var state = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state");
                version = state.GetProperty("expeditionVersion").GetInt64();
                Assert.Single(state.GetProperty("explorationSavedTables").EnumerateArray());

                using var replay = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-tables", save, JsonOptions);
                replay.EnsureSuccessStatusCode();
                var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(replayed.GetProperty("stateChanged").GetBoolean());
                Assert.Equal(version, replayed.GetProperty("state")
                    .GetProperty("expeditionVersion").GetInt64());

                using var reusedId = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-tables",
                    new
                    {
                        expectedVersion = version, operationId = saveId, scope = "Location",
                        tableKey = "location",
                        definition = Table("GM source 2", "Altered low", "Altered high"),
                        provenance = Provenance("save-table")
                    }, JsonOptions);
                Assert.Equal(HttpStatusCode.BadRequest, reusedId.StatusCode);

                using var stale = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-tables",
                    new
                    {
                        expectedVersion = version - 1, operationId = Guid.NewGuid(),
                        scope = "Location", tableKey = "location",
                        definition = Table("GM source 1", "Synthetic outpost", "Synthetic ruins"),
                        provenance = Provenance("stale-save")
                    }, JsonOptions);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

                var visit = new
                {
                    expectedVersion = version, operationId = visitId,
                    actionKey = "explore", markVisited = true, markRestSite = false,
                    regionKey = "region-1", provenance = Provenance("rolled-saved-table"),
                    savedTableReferences = new[]
                    {
                        new { scope = "Location", tableKey = "location" }
                    }
                };
                using var rolled = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    visit, JsonOptions);
                rolled.EnsureSuccessStatusCode();
                state = (await rolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state");
                version = state.GetProperty("expeditionVersion").GetInt64();
                var result = state.GetProperty("explorationCell")
                    .GetProperty("locationTableResults")[0];
                Assert.Equal("GM source 1", result.GetProperty("sourceReference").GetString());
                Assert.StartsWith("1d6=", result.GetProperty("roll").GetString());
                Assert.Contains(result.GetProperty("resultText").GetString(),
                    new[] { "Synthetic outpost", "Synthetic ruins" });
                var initialRoll = result.GetProperty("roll").GetString();
                var initialOutcome = result.GetProperty("resultText").GetString();

                using var retryRoll = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    visit, JsonOptions);
                retryRoll.EnsureSuccessStatusCode();
                var same = await retryRoll.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(same.GetProperty("stateChanged").GetBoolean());
                Assert.Equal(version, same.GetProperty("state")
                    .GetProperty("expeditionVersion").GetInt64());

                using var changed = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-tables",
                    new
                    {
                        expectedVersion = version, operationId = Guid.NewGuid(),
                        scope = "Location", tableKey = "location",
                        definition = Table("GM source 2", "Revised low", "Revised high"),
                        provenance = Provenance("revise-table")
                    }, JsonOptions);
                changed.EnsureSuccessStatusCode();
                state = (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state");
                version = state.GetProperty("expeditionVersion").GetInt64();
                Assert.Equal(initialOutcome, state.GetProperty("explorationCell")
                    .GetProperty("locationTableResults")[0].GetProperty("resultText").GetString());

                using var remove = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-tables",
                    new
                    {
                        expectedVersion = version, operationId = Guid.NewGuid(),
                        scope = "Location", tableKey = "location",
                        definition = (object?)null,
                        provenance = Provenance("remove-table")
                    }, JsonOptions);
                remove.EnsureSuccessStatusCode();
                state = (await remove.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state");
                version = state.GetProperty("expeditionVersion").GetInt64();
                Assert.Empty(state.GetProperty("explorationSavedTables").EnumerateArray());
                Assert.Equal(initialRoll, state.GetProperty("explorationCell")
                    .GetProperty("locationTableResults")[0].GetProperty("roll").GetString());

                using var missing = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-tables",
                    new
                    {
                        expectedVersion = version, operationId = Guid.NewGuid(),
                        scope = "Location", tableKey = "location",
                        definition = (object?)null,
                        provenance = Provenance("remove-missing")
                    }, JsonOptions);
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            }

            using (var restarted = TestWebHost.Create(database, "alice"))
            using (var client = restarted.CreateClient())
            {
                var state = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}/journeys", JsonOptions);
                Assert.Equal(version, state.GetProperty("expeditionVersion").GetInt64());
                Assert.Empty(state.GetProperty("explorationSavedTables").EnumerateArray());
                Assert.Equal("GM source 1", state.GetProperty("explorationCell")
                    .GetProperty("locationTableResults")[0].GetProperty("sourceReference").GetString());
            }
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task RejectsInvalidSavedTablesAndMissingReferencesWithoutStateChanges()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var create = await client.PostAsJsonAsync("/api/expeditions/cells", new
            {
                name = "Missing library entries",
                procedureKey = CrawlProcedureCatalog.WastelandWandererPresetKey,
                context = new { kind = "MaplessSpatial", name = "Square session" }
            }, JsonOptions);
            create.EnsureSuccessStatusCode();
            var expedition = await create.Content.ReadFromJsonAsync<JsonElement>();
            var id = expedition.GetProperty("id").GetGuid();
            var version = expedition.GetProperty("version").GetInt64();

            using var invalid = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/journeys/exploration-tables",
                new
                {
                    expectedVersion = version, operationId = Guid.NewGuid(),
                    scope = "Location", tableKey = "location",
                    definition = new
                    {
                        scope = "Location", tableKey = "location",
                        dieSides = 6, sourceReference = "GM fixture",
                        rows = new[] { new { from = 1, through = 5, resultText = "Uncovered six" } }
                    },
                    provenance = Provenance("bad-table")
                }, JsonOptions);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

            using var unknown = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/journeys/exploration-cell",
                new
                {
                    expectedVersion = version, operationId = Guid.NewGuid(),
                    markVisited = false, markRestSite = false,
                    provenance = Provenance("missing-ref"),
                    savedTableReferences = new[] { new { scope = "Location", tableKey = "missing" } }
                }, JsonOptions);
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            var state = await client.GetFromJsonAsync<JsonElement>(
                $"/api/expeditions/{id:D}/journeys", JsonOptions);
            Assert.Equal(version, state.GetProperty("expeditionVersion").GetInt64());
            Assert.Empty(state.GetProperty("explorationSavedTables").EnumerateArray());
            Assert.Empty(state.GetProperty("explorationCells").EnumerateArray());
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
