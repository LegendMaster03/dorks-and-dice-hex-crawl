using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class Phase18CellEnvironmentEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public async Task CellEnvironmentRoundTripsThroughHttpAndReevaluatesAtActualBoundary()
    {
        var db = TestWebHost.NewDatabasePath();
        try
        {
            var generated = DelaneyDressHarmonicMetricRealization.Construct(
                "<1:1,1,1:4,4>", 1.5, "world-unit");
            Assert.Equal("realized", generated.Status);
            var tiling = new PeriodicWorldTiling(Guid.NewGuid(),
                generated.Topology!, generated.Realization!, new WorldPoint(0, 0));
            tiling.Validate();
            var current = tiling.Resolve(new PeriodicCellAddress(
                tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
            var exit = tiling.Boundaries(current.Id.Address)[0];
            var neighbor = tiling.Resolve(exit.To.Address);
            var world = new OverworldDefinition
            {
                Id = Guid.NewGuid(), Name = "Qualified environment round-trip",
                Tiling = tiling,
                EnvironmentAnnotations =
                [
                    Annotation(current.Id, "forest"),
                    Annotation(neighbor.Id, "wetlands")
                ]
            };
            world.ValidateEnvironmentAnnotations();
            var original = CrawlProcedureCatalog.Resolve(
                "simple-fixed-distance").MaterializeGeneric().Procedure;
            var modules = original.Modules
                .Where(m => m.Mechanic.ExecutionHandler !=
                    GenericProcedureExecutionHandlers.HexProgressPolicy)
                .Select(m =>
                {
                    if (m.Mechanic.ExecutionHandler !=
                        GenericProcedureExecutionHandlers.MovementResolutionPolicy) return m;
                    var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                    parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                    parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                    parameters["tracksIntraHexProgress"] = "false";
                    return m with
                    {
                        Mechanic = m.Mechanic with { Version = 2 },
                        Parameters = parameters
                    };
                }).ToArray();
            var procedure = original with
            {
                ProcedureId = Guid.NewGuid(),
                TilingDsSymbol = tiling.Topology.QuotientDsSymbol,
                Modules = modules
            };
            procedure.Validate();
            var state = new CellExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = new PeriodicCellTraversal
                {
                    CurrentCell = current.Id,
                    Position = current.Center,
                    SelectedExitInterfaceIndex = exit.InterfaceIndex
                }
            };
            var now = DateTimeOffset.UtcNow;
            var store = new PostgresHexCrawlStore(db);
            await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));
            await store.CreateExpeditionAsync(new StoredExpedition(
                "Environment watcher", state,
                new WorldBoundCrawlSessionContext(world.Id),
                new PlayerKnowledgeState
                {
                    ScopeId = Guid.NewGuid(),
                    OverworldId = world.Id,
                    PresentationPolicy = MapPresentationPolicy.DmControlled()
                },
                procedure, null, TimeSpan.Zero,
                "alice", 1, now, now));

            using (var factory = TestWebHost.Create(db, "alice"))
            using (var client = factory.CreateClient())
            {
                var worldPath = $"/api/overworlds/{world.Id:D}/environment";
                var environment = await client.GetFromJsonAsync<JsonElement>(worldPath);
                var annotations = environment.GetProperty("annotations").EnumerateArray().ToArray();
                Assert.Equal(2, annotations.Length);
                Assert.All(annotations, entry =>
                {
                    var scope = entry.GetProperty("scope");
                    Assert.Equal("Cell", scope.GetProperty("kind").GetString());
                    Assert.Equal(JsonValueKind.Null, scope.GetProperty("hex").ValueKind);
                    Assert.Equal(tiling.Id, scope.GetProperty("cell").GetProperty("tilingId").GetGuid());
                });

                // Feed the server's own serialized cell-scope annotations
                // directly back through its PUT projection. Losing Cell here
                // was a real-world data-loss hazard despite domain support.
                using var saved = await client.PutAsJsonAsync(worldPath, new
                {
                    expectedVersion = environment.GetProperty("version").GetInt64(),
                    annotations = environment.GetProperty("annotations")
                }, Json);
                Assert.True(saved.IsSuccessStatusCode,
                    $"World environment PUT failed: {await saved.Content.ReadAsStringAsync()}");
                var roundTrip = await saved.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(2L, roundTrip.GetProperty("version").GetInt64());
                Assert.Equal(current.Id, ReadCell(annotations.Single(a =>
                    a.GetProperty("facts")[0].GetProperty("tag").GetString() == "forest")
                    .GetProperty("scope").GetProperty("cell")));

                var focusedPath = $"/api/expeditions/{state.Id:D}/environment";
                var before = await client.GetFromJsonAsync<JsonElement>(focusedPath);
                Assert.Equal("forest", EffectiveTerrain(before).GetProperty("fact")
                    .GetProperty("tag").GetString());
                Assert.Equal(current.Id, ReadCell(
                    EffectiveTerrain(before).GetProperty("source").GetProperty("cell")));
                Assert.False(EffectiveTerrain(before).GetProperty("source")
                    .TryGetProperty("hex", out _));

                var midpoint = new WorldPoint(
                    (exit.Start.X + exit.End.X) / 2,
                    (exit.Start.Y + exit.End.Y) / 2);
                var heading = midpoint - current.Center;
                var manual = new ResolutionProvenance(ResolutionSource.ManualRoll);
                using var advanced = await client.PostAsJsonAsync(
                    $"/api/expeditions/{state.Id:D}/cells/advance",
                    new
                    {
                        expectedVersion = 1,
                        plan = new CellWatchTravelPlan(
                            heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None),
                        inputs = new CellWatchAdvanceInputs(
                            ResolvedTravelAmount.CellTransitions(1, manual),
                            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, manual),
                            ResolvedEncounter.None)
                    }, Json);
                Assert.True(advanced.IsSuccessStatusCode,
                    $"Cell advance failed: {await advanced.Content.ReadAsStringAsync()}");

                var after = await client.GetFromJsonAsync<JsonElement>(focusedPath);
                Assert.Equal("wetlands", EffectiveTerrain(after).GetProperty("fact")
                    .GetProperty("tag").GetString());
                Assert.Equal(neighbor.Id, ReadCell(
                    EffectiveTerrain(after).GetProperty("source").GetProperty("cell")));
                Assert.DoesNotContain(after.GetProperty("effectiveContext")
                    .GetProperty("facts").EnumerateArray(),
                    e => e.GetProperty("fact").GetProperty("tag").GetString() == "forest"
                        && e.GetProperty("effective").GetBoolean());
            }
            using (var restartedFactory = TestWebHost.Create(db, "alice"))
            using (var restarted = restartedFactory.CreateClient())
            {
                var worldResponse = await restarted.GetFromJsonAsync<JsonElement>(
                    $"/api/overworlds/{world.Id:D}/environment");
                Assert.Equal(2L, worldResponse.GetProperty("version").GetInt64());
                Assert.Equal(2, worldResponse.GetProperty("annotations").GetArrayLength());
                Assert.All(worldResponse.GetProperty("annotations").EnumerateArray(),
                    e => Assert.Equal(tiling.Id,
                        e.GetProperty("scope").GetProperty("cell")
                            .GetProperty("tilingId").GetGuid()));
                var focused = await restarted.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{state.Id:D}/environment");
                Assert.Equal("wetlands", EffectiveTerrain(focused).GetProperty("fact")
                    .GetProperty("tag").GetString());
            }
            using (var deniedFactory = TestWebHost.Create(db, "bob"))
            using (var denied = deniedFactory.CreateClient())
            using (var response = await denied.GetAsync(
                $"/api/expeditions/{state.Id:D}/environment"))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(db);
        }
    }

    private static EnvironmentAnnotation Annotation(WorldCellId cell, string tag) => new()
    {
        Id = Guid.NewGuid(),
        Scope = new EnvironmentAnnotationScope
        {
            Kind = EnvironmentAnnotationScopeKind.Cell,
            Cell = cell
        },
        Facts =
        [
            new EnvironmentFact
            {
                Id = Guid.NewGuid(), Dimension = "terrain",
                ValueKind = EnvironmentValueKind.Tag,
                Tag = tag
            }
        ]
    };

    private static JsonElement EffectiveTerrain(JsonElement context) =>
        context.GetProperty("effectiveContext").GetProperty("facts")
            .EnumerateArray().Single(e =>
                e.GetProperty("effective").GetBoolean()
                && e.GetProperty("fact").GetProperty("dimension").GetString() == "terrain");

    private static WorldCellId ReadCell(JsonElement value) =>
        JsonSerializer.Deserialize<WorldCellId>(value.GetRawText(), Json)!;
}
