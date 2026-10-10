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
            var world = new OverworldDefinition
            {
                Id = Guid.NewGuid(),
                Name = "Read-only cell contract",
                Tiling = tiling
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
                TimeSpan.FromHours(1), null, "Qualified cell transition")
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
                    Position = cell.Center
                },
                History = [record]
            };
            runtime.Validate(tiling);
            await store.CreateExpeditionAsync(new StoredExpedition(
                "Qualified cell expedition",
                runtime,
                new WorldBoundCrawlSessionContext(world.Id),
                null, procedure, null, TimeSpan.Zero,
                "alice", 1, now, now));

            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                using var response = await client.GetAsync($"/api/expeditions/{runtime.Id:D}");
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
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
