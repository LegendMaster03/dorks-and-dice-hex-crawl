using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;

namespace HexCrawl.IntegrationTests;

/// <summary>
/// No published table content is used: these are fully synthetic, GM-authored
/// entries exercising the generic storage and procedure flow.
/// </summary>
public sealed class ExplorationTableRollEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static object Source(string key) => new
    {
        sourceKind = "Dm", sourceKey = key
    };

    [Fact]
    public async Task SuppliedRollSatisfiesDiscoveryAndDoesNotRerollOnReplayOrRestart()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            long version;
            var operationId = Guid.NewGuid();
            var tableRolls = new[]
            {
                new
                {
                    scope = "Location", tableKey = "location",
                    dieSides = 6, sourceReference = "DM table alpha",
                    rows = new[]
                    {
                        new { from = 1, through = 3, resultText = "Synthetic outpost" },
                        new { from = 4, through = 6, resultText = "Synthetic ruins" }
                    }
                },
                new
                {
                    scope = "Region", tableKey = "region-type",
                    dieSides = 6, sourceReference = "DM table beta",
                    rows = new[]
                    {
                        new { from = 1, through = 2, resultText = "Synthetic meadow" },
                        new { from = 3, through = 6, resultText = "Synthetic scrub" }
                    }
                }
            };
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var create = await client.PostAsJsonAsync("/api/expeditions/cells",
                    new
                    {
                        name = "GM authored table",
                        procedureKey = CrawlProcedureCatalog.WastelandWarfarePresetKey,
                        context = new { kind = "MaplessSpatial", name = "Square session" }
                    }, JsonOptions);
                create.EnsureSuccessStatusCode();
                var expedition = await create.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = expedition.GetProperty("id").GetGuid();
                version = expedition.GetProperty("version").GetInt64();

                var command = new
                {
                    expectedVersion = version, operationId,
                    actionKey = "explore", markVisited = true, markRestSite = false,
                    regionKey = "region-1", tableRolls,
                    provenance = Source("gm-table-roll")
                };
                using var completed = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell", command, JsonOptions);
                completed.EnsureSuccessStatusCode();
                var updated = (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state");
                version = updated.GetProperty("expeditionVersion").GetInt64();
                var cell = updated.GetProperty("explorationCell");
                Assert.True(cell.GetProperty("visited").GetBoolean());
                Assert.Equal("region-1", cell.GetProperty("regionKey").GetString());
                var loc = cell.GetProperty("locationTableResults")[0];
                Assert.Equal("location", loc.GetProperty("tableKey").GetString());
                Assert.StartsWith("1d6=", loc.GetProperty("roll").GetString());
                Assert.Equal("DM table alpha", loc.GetProperty("sourceReference").GetString());
                Assert.Contains(loc.GetProperty("resultText").GetString(),
                    new[] { "Synthetic outpost", "Synthetic ruins" });
                var region = cell.GetProperty("regionTableResults")[0];
                Assert.Equal("region-type", region.GetProperty("tableKey").GetString());
                Assert.StartsWith("1d6=", region.GetProperty("roll").GetString());
                Assert.Equal("DM table beta", region.GetProperty("sourceReference").GetString());

                using var replay = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell", command, JsonOptions);
                replay.EnsureSuccessStatusCode();
                var repeated = await replay.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(repeated.GetProperty("stateChanged").GetBoolean());
                Assert.Equal(version, repeated.GetProperty("state").GetProperty("expeditionVersion").GetInt64());
                Assert.Equal(loc.GetProperty("roll").GetString(), repeated.GetProperty("state")
                    .GetProperty("explorationCell").GetProperty("locationTableResults")[0]
                    .GetProperty("roll").GetString());

                using var changed = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/journeys/exploration-cell",
                    new
                    {
                        expectedVersion = command.expectedVersion, operationId,
                        actionKey = "explore", markVisited = true, markRestSite = false,
                        regionKey = "region-1", provenance = Source("gm-table-roll"),
                        tableRolls = new[]
                        {
                            new
                            {
                                scope = "Location", tableKey = "location",
                                dieSides = 6, sourceReference = "DM table alpha",
                                rows = new[] { new { from = 1, through = 6, resultText = "Changed result" } }
                            }
                        }
                    }, JsonOptions);
                Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
            }

            using (var restarted = TestWebHost.Create(database, "alice"))
            using (var client = restarted.CreateClient())
            {
                var persisted = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{expeditionId:D}/journeys", JsonOptions);
                Assert.Equal(version, persisted.GetProperty("expeditionVersion").GetInt64());
                Assert.Equal("DM table alpha", persisted.GetProperty("explorationCell")
                    .GetProperty("locationTableResults")[0].GetProperty("sourceReference").GetString());
                Assert.Single(persisted.GetProperty("explorationRegions").EnumerateArray());
            }
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task RejectsPartialAndUnattributedTablesWithoutMutatingExpedition()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var create = await client.PostAsJsonAsync("/api/expeditions/cells",
                new
                {
                    name = "Broken tables",
                    procedureKey = CrawlProcedureCatalog.WastelandWandererPresetKey,
                    context = new { kind = "MaplessSpatial", name = "Square session" }
                }, JsonOptions);
            create.EnsureSuccessStatusCode();
            var expedition = await create.Content.ReadFromJsonAsync<JsonElement>();
            var id = expedition.GetProperty("id").GetGuid();
            var version = expedition.GetProperty("version").GetInt64();

            using var invalid = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/journeys/exploration-cell",
                new
                {
                    expectedVersion = version, operationId = Guid.NewGuid(),
                    markVisited = false, markRestSite = false,
                    provenance = Source("invalid-tables"),
                    tableRolls = new[]
                    {
                        new
                        {
                            scope = "Location", tableKey = "site", dieSides = 6,
                            sourceReference = "GM fixture",
                            rows = new[] { new { from = 1, through = 4, resultText = "Only partial coverage" } }
                        }
                    }
                }, JsonOptions);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var after = await client.GetFromJsonAsync<JsonElement>(
                $"/api/expeditions/{id:D}/journeys", JsonOptions);
            Assert.Equal(version, after.GetProperty("expeditionVersion").GetInt64());
            Assert.Empty(after.GetProperty("explorationCells").EnumerateArray());
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
