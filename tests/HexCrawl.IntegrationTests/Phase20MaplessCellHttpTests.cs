using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

/// <summary>
/// HTTP checks for the opt-in V2 routes. These tests deliberately keep the
/// Phase 15 legacy routes on their existing runtime until the UI cutover.
/// </summary>
public sealed class Phase20MaplessCellHttpTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private static object MaplessStart(string presetKey = "simple-fixed-distance") => new
    {
        name = "Mapless V2 HTTP",
        procedureKey = presetKey,
        context = new
        {
            kind = "AbstractHex",
            name = "Persistent abstract geometry",
            orientation = "PointyTop",
            hexCenterDistance = 12,
            distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
        },
        startHex = new { q = 0, r = 0 }
    };

    [Fact]
    public async Task MaplessStartTopologyAndAdvanceRemainOwnerScopedVersionedAndGeometric()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid id;
            long version;
            using (var ownerFactory = TestWebHost.Create(database, "alice"))
            using (var client = ownerFactory.CreateClient())
            {
                using var start = await client.PostAsJsonAsync(
                    "/api/expeditions/cells", MaplessStart(), ApiJson);
                Assert.Equal(HttpStatusCode.Created, start.StatusCode);
                var created = await start.Content.ReadFromJsonAsync<JsonElement>();
                id = created.GetProperty("id").GetGuid();
                version = created.GetProperty("version").GetInt64();
                var initial = created.GetProperty("expedition");
                Assert.False(initial.TryGetProperty("currentHex", out _));
                Assert.False(initial.TryGetProperty("hexProgress", out _));
                Assert.True(initial.GetProperty("isSpatial").GetBoolean());
                Assert.NotEqual(Guid.Empty,
                    initial.GetProperty("currentCell").GetProperty("tilingId").GetGuid());

                // Nonspatial encounter APIs must never reinterpret a spatial
                // cell runtime as the old axial assistant state.
                using (var nonspatialCheck = await client.PostAsJsonAsync(
                    $"/api/expeditions/{id:D}/nonspatial/encounters",
                    new
                    {
                        expectedVersion = version,
                        outcome = "WanderingEncounter",
                        resolutionSource = "ManualRoll"
                    }, ApiJson))
                    Assert.Equal(HttpStatusCode.BadRequest, nonspatialCheck.StatusCode);

                using var topologyResponse = await client.GetAsync(
                    $"/api/expeditions/{id:D}/cell-topology");
                topologyResponse.EnsureSuccessStatusCode();
                var topology = await topologyResponse.Content.ReadFromJsonAsync<JsonElement>();
                var center = JsonSerializer.Deserialize<WorldPoint>(
                    topology.GetProperty("center").GetRawText(), ApiJson);
                var edges = topology.GetProperty("interfaces").EnumerateArray().ToArray();
                Assert.NotEmpty(edges);
                var edge = edges[0];
                var a = JsonSerializer.Deserialize<WorldPoint>(
                    edge.GetProperty("start").GetRawText(), ApiJson);
                var b = JsonSerializer.Deserialize<WorldPoint>(
                    edge.GetProperty("end").GetRawText(), ApiJson);
                var midpoint = new WorldPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var heading = midpoint - center;
                var provenance = new ResolutionProvenance(ResolutionSource.ManualRoll);
                var plan = new CellWatchTravelPlan(heading, false, false,
                    TravelModeSelection.Normal, NavigationAidSelection.None);
                var accepted = new AdvanceCellWatchRequest(version, plan,
                    new CellWatchAdvanceInputs(
                        ResolvedTravelAmount.Distance(
                            new DistanceMeasure(0.25, DistanceUnit.Miles),
                            new DistanceMeasure(0.25, DistanceUnit.Miles), provenance),
                        new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, provenance),
                        ResolvedEncounter.None));

                using (var forged = await client.PostAsJsonAsync(
                    $"/api/expeditions/{id:D}/cells/advance",
                    accepted with
                    {
                        Inputs = accepted.Inputs with
                        {
                            Travel = ResolvedTravelAmount.Distance(
                                new DistanceMeasure(0.25, DistanceUnit.Miles),
                                new DistanceMeasure(0.25, DistanceUnit.Miles),
                                new ResolutionProvenance(ResolutionSource.AutomaticRoll))
                        }
                    }, ApiJson))
                {
                    Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
                }

                // The obsolete V1 helper route is no longer registered.
                // It cannot mutate a V2 session.
                using (var helper = await client.PostAsJsonAsync(
                    $"/api/expeditions/{id:D}/resolution-helper",
                    new { expectedVersion = version }, ApiJson))
                {
                    Assert.Equal(HttpStatusCode.NotFound, helper.StatusCode);
                }
                using (var unchanged = await client.GetAsync($"/api/expeditions/{id:D}"))
                {
                    var snapshot = await unchanged.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal(version, snapshot.GetProperty("version").GetInt64());
                    Assert.Empty(snapshot.GetProperty("history").EnumerateArray());
                }

                using var advancedResponse = await client.PostAsJsonAsync(
                    $"/api/expeditions/{id:D}/cells/advance", accepted, ApiJson);
                Assert.Equal(HttpStatusCode.OK, advancedResponse.StatusCode);
                var advanced = await advancedResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(advanced.GetProperty("version").GetInt64() > version);
                Assert.False(advanced.GetProperty("expedition").TryGetProperty("currentHex", out _));
                Assert.Equal(1, advanced.GetProperty("expedition").GetProperty("completedWatches").GetInt32());

                using var stale = await client.PostAsJsonAsync(
                    $"/api/expeditions/{id:D}/cells/advance", accepted, ApiJson);
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            {
                using var inaccessible = await other.GetAsync($"/api/expeditions/{id:D}");
                Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);
                using var hiddenTopology = await other.GetAsync(
                    $"/api/expeditions/{id:D}/cell-topology");
                Assert.Equal(HttpStatusCode.NotFound, hiddenTopology.StatusCode);
            }

            using (var anonymousFactory = TestWebHost.Create(database, null))
            using (var anonymous = anonymousFactory.CreateClient())
            {
                using var anonymousTopology = await anonymous.GetAsync(
                    $"/api/expeditions/{id:D}/cell-topology");
                Assert.Equal(HttpStatusCode.Unauthorized, anonymousTopology.StatusCode);
            }

            var restarted = new PostgresHexCrawlStore(database);
            await restarted.InitializeAsync();
            var saved = await restarted.GetExpeditionAsync(id, "alice");
            Assert.NotNull(saved);
            var state = Assert.IsType<CellExpeditionState>(saved!.Runtime);
            Assert.Equal(1, state.CompletedWatches);
            var geometry = await new HexCrawlService(restarted).ResolveCellGeometryAsync(saved, "alice");
            Assert.Null(geometry.World);
            state.Validate(geometry.Tiling);
            Assert.All(state.History, item => Assert.NotNull(item.Cell));
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task ExactCellRepositionIsAuditedAndRejectsOffCellOrStaleInputs()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync(
                "/api/expeditions/cells", MaplessStart(), ApiJson);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("id").GetGuid();
            var version = created.GetProperty("version").GetInt64();
            var store = new PostgresHexCrawlStore(database);
            var saved = (await store.GetExpeditionAsync(id, "alice"))!;
            var geometry = (await new HexCrawlService(store)
                .ResolveCellGeometryAsync(saved, "alice")).Tiling;
            var state = Assert.IsType<CellExpeditionState>(saved.Runtime);
            var targetCell = geometry.Boundaries(state.Traversal.CurrentCell.Address)[0].To;
            var targetPosition = geometry.Resolve(targetCell.Address).Center;
            var request = new RepositionCellExpeditionRequest(
                version, targetCell, targetPosition, "Map annotation correction");

            // An omitted position must not silently become the origin (0,0).
            using (var omitted = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/cells/reposition",
                request with { Position = null }, ApiJson))
            {
                Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
            }
            // A point in the old cell cannot be misrepresented as a position
            // inside a different qualified destination cell.
            using (var invalid = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/cells/reposition",
                request with { Position = state.Traversal.Position }, ApiJson))
            {
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            }
            using var accepted = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/cells/reposition", request, ApiJson);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var updated = await accepted.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(version + 1, updated.GetProperty("version").GetInt64());
            var spatial = updated.GetProperty("expedition");
            Assert.False(spatial.TryGetProperty("currentHex", out _));
            Assert.Equal(targetCell, JsonSerializer.Deserialize<WorldCellId>(
                spatial.GetProperty("currentCell").GetRawText(), ApiJson));
            var ev = Assert.Single(updated.GetProperty("history").EnumerateArray());
            Assert.Equal("DmOverrideApplied", ev.GetProperty("kind").GetString());
            Assert.False(ev.TryGetProperty("hex", out _));
            Assert.Equal(targetCell, JsonSerializer.Deserialize<WorldCellId>(
                ev.GetProperty("cell").GetRawText(), ApiJson));

            using var stale = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/cells/reposition", request, ApiJson);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var readBack = (await new PostgresHexCrawlStore(database)
                .GetExpeditionAsync(id, "alice"))!;
            var savedCell = Assert.IsType<CellExpeditionState>(readBack.Runtime);
            Assert.Equal(targetCell, savedCell.Traversal.CurrentCell);
            Assert.Equal(targetPosition, savedCell.Traversal.Position);
            Assert.Single(savedCell.History);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task GeometricHexWorldPresetStartIsOptInAndRejectsConflictingSelection()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var store = new PostgresHexCrawlStore(database);
            await store.InitializeAsync();
            var world = new OverworldDefinition
            {
                Id = Guid.NewGuid(),
                Name = "Legacy hex grid with generalized opt-in",
                Grid = new HexGridDefinition
                {
                    Id = Guid.NewGuid(),
                    HexRadiusWorldUnits = 2,
                    NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
                }
            };
            var now = DateTimeOffset.UtcNow;
            await store.CreateOverworldAsync(new(
                world, "alice", 1, now, now));
            var startCell = LegacyHexTilingCompatibility.ToAddress(new HexCoordinate(0, 0));
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var route = $"/api/overworlds/{world.Id:D}/cell-expeditions";
            var request = new StartCellExpeditionRequest(
                "Generalized hex", null, null, startCell, "dm-controlled",
                "simple-hex-step");

            using (var invalid = await client.PostAsJsonAsync(route,
                request with { ProcedureId = Guid.NewGuid(), ProcedureRevision = 1 }, ApiJson))
            {
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            }
            using (var declarative = await client.PostAsJsonAsync(route,
                request with { PresetKey = "bx" }, ApiJson))
            {
                Assert.Equal(HttpStatusCode.BadRequest, declarative.StatusCode);
            }
            using var started = await client.PostAsJsonAsync(route, request, ApiJson);
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
            var geometric = await started.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(geometric.GetProperty("expedition").TryGetProperty("currentHex", out _));
            Assert.NotEqual(Guid.Empty,
                geometric.GetProperty("expedition").GetProperty("currentCell")
                    .GetProperty("tilingId").GetGuid());

            var worldExpeditionId = geometric.GetProperty("id").GetGuid();
            var worldTiling = world.SpatialTiling;
            var neighbor = worldTiling.Boundaries(startCell)[0].To;
            var neighborCenter = worldTiling.Resolve(neighbor.Address).Center;
            using var reposition = await client.PostAsJsonAsync(
                $"/api/expeditions/{worldExpeditionId:D}/cells/reposition",
                new RepositionCellExpeditionRequest(
                    geometric.GetProperty("version").GetInt64(),
                    neighbor, neighborCenter, "Explicit DM map correction"), ApiJson);
            Assert.Equal(HttpStatusCode.OK, reposition.StatusCode);
            var corrected = await reposition.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(neighbor, JsonSerializer.Deserialize<WorldCellId>(
                corrected.GetProperty("expedition").GetProperty("currentCell").GetRawText(), ApiJson));
            Assert.False(corrected.GetProperty("expedition").TryGetProperty("currentHex", out _));

            // Removed prototype routes do not remain in the new API surface.
            using var oldClient = await client.PostAsJsonAsync(
                $"/api/overworlds/{world.Id:D}/expeditions",
                new
                {
                    name = "Legacy UI compatibility",
                    procedureKey = "simple-fixed-distance",
                    startHex = new { q = 0, r = 0 }
                }, ApiJson);
            Assert.Equal(HttpStatusCode.NotFound, oldClient.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
