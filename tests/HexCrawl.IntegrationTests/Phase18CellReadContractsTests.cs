using HexCrawl.Web.Api;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class Phase18CellReadContractsTests
{
    [Fact]
    public async Task ExistingExpeditionGetExposesQualifiedCellWithoutFakeHexOrScale()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var generated = DelaneyDressHarmonicMetricRealization.Construct(
                "<1:1,1,1:4,4>", 1.5, "world-unit");
            Assert.Equal("realized", generated.Status);
            var tiling = new PeriodicWorldTiling(
                Guid.NewGuid(), generated.Topology!, generated.Realization!,
                new WorldPoint(0, 0));
            tiling.Validate();
            var landmark = new Location(
                Guid.NewGuid(), "Sealed archive", "landmark",
                tiling.Resolve(new PeriodicCellAddress(
                    tiling.Topology.MotifCells[0].Id,
                    new LatticeDisplacement(2, -1))).Center,
                LocationDiscoverability.Hidden, []);
            var world = new OverworldDefinition
            {
                Id = Guid.NewGuid(),
                Name = "Read-only cell contract",
                Tiling = tiling,
                Locations = [landmark]
            };
            var store = new PostgresHexCrawlStore(database);
            var now = DateTimeOffset.UtcNow;
            await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));

            var original = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
            var modules = original.Modules
                .Where(m => m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.HexProgressPolicy)
                .Select(m =>
                {
                    if (m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                        return m;
                    var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                    parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                    parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                    parameters["tracksIntraHexProgress"] = "false";
                    return m with { Mechanic = m.Mechanic with { Version = 2 }, Parameters = parameters };
                }).ToArray();
            var procedure = original with
            {
                ProcedureId = Guid.NewGuid(),
                TilingDsSymbol = tiling.Topology.QuotientDsSymbol,
                Modules = modules
            };
            procedure.Validate();
            var cell = tiling.Resolve(new PeriodicCellAddress(
                tiling.Topology.MotifCells[0].Id,
                new LatticeDisplacement(2, -1)));
            var boundary = tiling.Boundaries(cell.Id.Address)[0];
            var record = new CrawlRuntimeEvent(
                1, 1, CrawlRuntimeEventKind.CellEntered,
                TimeSpan.Zero, null, "Qualified cell transition")
            {
                Cell = cell.Id,
                BoundaryInterfaceIndex = boundary.InterfaceIndex,
                ReciprocalInterfaceIndex = boundary.ReciprocalInterfaceIndex
            };
            var runtime = new CellExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = new PeriodicCellTraversal
                {
                    CurrentCell = cell.Id,
                    Position = cell.Center,
                    SelectedExitInterfaceIndex = boundary.InterfaceIndex
                },
                History = [record]
            };
            runtime.Validate(tiling);
            await store.CreateExpeditionAsync(new StoredExpedition(
                "Qualified cell expedition",
                runtime,
                new WorldBoundCrawlSessionContext(world.Id),
                new PlayerKnowledgeState
                {
                    ScopeId = Guid.NewGuid(), OverworldId = world.Id,
                    PresentationPolicy = MapPresentationPolicy.DmControlled()
                },
                procedure, null, TimeSpan.Zero,
                "alice", 1, now, now));

            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                using var response = await client.GetAsync($"/api/expeditions/{runtime.Id:D}");
                Assert.True(response.IsSuccessStatusCode,
                    $"Cell expedition read returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                var materialized = body.GetProperty("procedure");
                Assert.True(materialized.GetProperty("isExecutable").GetBoolean());
                var procedureRuntime = materialized.GetProperty("runtime");
                Assert.Equal(2, procedureRuntime.GetProperty("movementMechanicVersion").GetInt32());
                if (procedureRuntime.TryGetProperty("farExitProgressFactor", out var factor))
                    Assert.Equal(JsonValueKind.Null, factor.ValueKind);
                var state = body.GetProperty("expedition");
                Assert.True(state.GetProperty("isSpatial").GetBoolean());
                Assert.Equal(JsonValueKind.Null, state.GetProperty("currentHex").ValueKind);
                Assert.Equal(JsonValueKind.Null, state.GetProperty("hexProgress").ValueKind);
                Assert.Equal(JsonValueKind.Null, state.GetProperty("distanceTraveled").ValueKind);
                Assert.Equal(cell.Id.TilingId,
                    state.GetProperty("currentCell").GetProperty("tilingId").GetGuid());
                Assert.Equal(cell.Id.TilingId,
                    state.GetProperty("cellTraversal").GetProperty("currentCell").GetProperty("tilingId").GetGuid());
                Assert.Equal(cell.Center.X, state.GetProperty("position").GetProperty("x").GetDouble(), 8);
                Assert.Equal(JsonValueKind.Null, body.GetProperty("context").GetProperty("hexCenterDistance").ValueKind);
                Assert.Equal(JsonValueKind.Null, body.GetProperty("context").GetProperty("orientation").ValueKind);
                var ev = Assert.Single(body.GetProperty("history").EnumerateArray());
                Assert.Equal("CellEntered", ev.GetProperty("kind").GetString());
                Assert.Equal(cell.Id.TilingId, ev.GetProperty("cell").GetProperty("tilingId").GetGuid());
                Assert.Equal(JsonValueKind.Null, ev.GetProperty("hex").ValueKind);
                Assert.Equal(boundary.InterfaceIndex, ev.GetProperty("boundaryInterfaceIndex").GetInt32());
                Assert.Equal(boundary.ReciprocalInterfaceIndex, ev.GetProperty("reciprocalInterfaceIndex").GetInt32());

                using var topologyResponse = await client.GetAsync($"/api/expeditions/{runtime.Id:D}/cell-topology");
                topologyResponse.EnsureSuccessStatusCode();
                var topology = await topologyResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(PeriodicCellTraversal.CurrentFormatVersion, topology.GetProperty("formatVersion").GetInt32());
                Assert.Equal(cell.Id.TilingId, topology.GetProperty("currentCell").GetProperty("tilingId").GetGuid());
                Assert.False(topology.GetProperty("hasPhysicalCalibration").GetBoolean());
                Assert.False(topology.TryGetProperty("physicalDistancePerWorldUnit", out _));
                Assert.Equal(cell.Polygon.Count, topology.GetProperty("polygon").GetArrayLength());
                var interfaces = topology.GetProperty("interfaces").EnumerateArray().ToArray();
                Assert.Equal(tiling.Boundaries(cell.Id.Address).Count, interfaces.Length);
                var first = interfaces.Single(value =>
                    value.GetProperty("interfaceIndex").GetInt32() == boundary.InterfaceIndex);
                Assert.Equal(boundary.ReciprocalInterfaceIndex,
                    first.GetProperty("reciprocalInterfaceIndex").GetInt32());
                Assert.Equal(boundary.To.TilingId,
                    first.GetProperty("neighborCell").GetProperty("tilingId").GetGuid());

                using var discover = await client.PostAsJsonAsync(
                    $"/api/expeditions/{runtime.Id:D}/discover",
                    new
                    {
                        expectedVersion = body.GetProperty("version").GetInt64(),
                        subjectId = landmark.Id,
                        subjectType = "Location",
                        source = "dm:cell-http-test"
                    });
                discover.EnsureSuccessStatusCode();
                var discovered = await discover.Content.ReadFromJsonAsync<JsonElement>();
                var entry = Assert.Single(discovered.GetProperty("knowledge").EnumerateArray());
                Assert.Equal(landmark.Id, entry.GetProperty("subjectId").GetGuid());
                Assert.Equal("Discovered", entry.GetProperty("state").GetString());
                var discoveredEvent = discovered.GetProperty("history").EnumerateArray()
                    .Single(item => item.GetProperty("kind").GetString() == "LocationDiscovered");
                Assert.Equal(cell.Id.TilingId,
                    discoveredEvent.GetProperty("cell").GetProperty("tilingId").GetGuid());
                Assert.Equal(JsonValueKind.Null, discoveredEvent.GetProperty("hex").ValueKind);
                Assert.Equal(0, discovered.GetProperty("knownHexes").GetArrayLength());

                var heading = new WorldPoint(
                    (boundary.Start.X + boundary.End.X) / 2 - cell.Center.X,
                    (boundary.Start.Y + boundary.End.Y) / 2 - cell.Center.Y);
                var plan = new CellWatchTravelPlan(
                    heading, false, false,
                    TravelModeSelection.Normal, NavigationAidSelection.None);
                var provenance = new ResolutionProvenance(ResolutionSource.ManualRoll);
                var resolvedInputs = new CellWatchAdvanceInputs(
                    ResolvedTravelAmount.CellTransitions(1, provenance),
                    new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, provenance),
                    ResolvedEncounter.None);
                var advanceRequest = new AdvanceCellWatchRequest(
                    discovered.GetProperty("version").GetInt64(), plan, resolvedInputs);
                using var forged = await client.PostAsJsonAsync(
                    $"/api/expeditions/{runtime.Id:D}/cells/advance",
                    advanceRequest with
                    {
                        Inputs = resolvedInputs with
                        {
                            Travel = ResolvedTravelAmount.CellTransitions(1,
                                new ResolutionProvenance(ResolutionSource.AutomaticRoll))
                        }
                    });
                Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

                using var advancedResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{runtime.Id:D}/cells/advance", advanceRequest);
                advancedResponse.EnsureSuccessStatusCode();
                var advanced = await advancedResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(1, advanced.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
                Assert.Equal(0, advanced.GetProperty("knownHexes").GetArrayLength());
                Assert.Equal(boundary.To.TilingId,
                    advanced.GetProperty("expedition").GetProperty("currentCell").GetProperty("tilingId").GetGuid());
                Assert.Contains(advanced.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "CellEntered");
                using var stale = await client.PostAsJsonAsync(
                    $"/api/expeditions/{runtime.Id:D}/cells/advance", advanceRequest);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            using (var blocked = await other.GetAsync($"/api/expeditions/{runtime.Id:D}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);
                using var hiddenTopology = await other.GetAsync($"/api/expeditions/{runtime.Id:D}/cell-topology");
                Assert.Equal(HttpStatusCode.NotFound, hiddenTopology.StatusCode);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
