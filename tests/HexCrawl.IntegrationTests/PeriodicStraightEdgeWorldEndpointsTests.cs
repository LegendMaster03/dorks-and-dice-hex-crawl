using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Api;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class PeriodicStraightEdgeWorldEndpointsTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private const string Pattern =
        "<12:2 4 6 8 10 12,12 3 5 7 9 11,8 7 10 9 12 11:6,3 3>";

    private static readonly PeriodicStraightEdgeSubdivision[] Splits =
    [
        new("square", 1, .5), new("square", 4, .5)
    ];

    private static PeriodicTopologyWitness Topology()
    {
        var canonical = DelaneyDressTopology.Inspect(Pattern).Symbol!.Canonical;
        return new(PeriodicTopologyContractVersion.Current, canonical, canonical,
            [new PeriodicMotifCell("square",
            [
                new(0, 0, "square", new(0, -1), 3),
                new(1, 1, "square", new(1, -1), 4),
                new(2, 2, "square", new(1, 0), 5),
                new(3, 3, "square", new(0, 1), 0),
                new(4, 4, "square", new(-1, 1), 1),
                new(5, 5, "square", new(-1, 0), 2)
            ])], "test:explicit-atomic-periodic-cover");
    }

    private static PeriodicMetricRealization Geometry() =>
        new("world-unit", new(1, 0), new(.5, 1),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["square"] =
                [
                    new(0, 0), new(.5, 0), new(1, 0),
                    new(1, 1), new(.5, 1), new(0, 1)
                ]
            }, []);

    [Fact]
    public async Task OffsetSquareCreationAndReloadPreserveSixAtomicExitsFourSidesAndPinnedRules()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId, expeditionId;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Offset squares",
                    tilingDsSymbol = Pattern,
                    worldUnitsPerAbstractPeriod = 1.0,
                    origin = new { x = 0.0, y = 0.0 },
                    straightEdgeSubdivisions = Splits,
                    topologyWitness = Topology(),
                    metricRealization = Geometry()
                });
                Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
                worldId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

                using var viewport = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/viewport?minX=-2&minY=-2&maxX=2&maxY=2");
                viewport.EnsureSuccessStatusCode();
                var cells = (await viewport.Content.ReadFromJsonAsync<JsonElement>())
                    .EnumerateArray().ToArray();
                Assert.NotEmpty(cells);
                Assert.All(cells, cell => Assert.Equal(6, cell.GetProperty("polygon").GetArrayLength()));

                using var generated = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures", new { });
                Assert.True(generated.IsSuccessStatusCode, await generated.Content.ReadAsStringAsync());
                var pinned = await generated.Content.ReadFromJsonAsync<JsonElement>();
                var first = cells.First(cell => cell.GetProperty("center").GetProperty("x").GetDouble() == .5
                    && cell.GetProperty("center").GetProperty("y").GetDouble() == .5);
                using var started = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions", new
                    {
                        name = "Atomic exit navigator",
                        procedureId = pinned.GetProperty("procedureId").GetGuid(),
                        procedureRevision = pinned.GetProperty("revision").GetInt32(),
                        presentationKey = "dm-controlled",
                        startCell = first.GetProperty("id").GetProperty("address")
                    });
                Assert.True(started.IsSuccessStatusCode, await started.Content.ReadAsStringAsync());
                var startedBody = await started.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = startedBody.GetProperty("id").GetGuid();
                using var navigation = await client.GetAsync(
                    $"/api/expeditions/{expeditionId:D}/cell-topology");
                navigation.EnsureSuccessStatusCode();
                var navigator = await navigation.Content.ReadFromJsonAsync<JsonElement>();
                var interfaces = navigator.GetProperty("interfaces").EnumerateArray().ToArray();
                Assert.Equal(6, interfaces.Length);
                Assert.Equal(new[] { 0, 0, 1, 2, 2, 3 },
                    interfaces.Select(e => e.GetProperty("boundarySideIndex").GetInt32()));
                Assert.Equal(6, interfaces.Select(e => e.GetProperty("interfaceIndex").GetInt32())
                    .Distinct().Count());
                Assert.Equal("CellSteps", startedBody
                    .GetProperty("procedure").GetProperty("runtime").GetProperty("travelResolution").GetString());

                // Start a watch toward the T-junction. With no unique
                // destination interior, the watch pauses instead of guessing.
                var startPosition = navigator.GetProperty("position");
                var vertex = navigator.GetProperty("polygon")[1];
                var center = new WorldPoint(
                    startPosition.GetProperty("x").GetDouble(),
                    startPosition.GetProperty("y").GetDouble());
                var junction = new WorldPoint(
                    vertex.GetProperty("x").GetDouble(),
                    vertex.GetProperty("y").GetDouble());
                var ambiguousPlan = new CellWatchTravelPlan(junction - center,
                    false, false, TravelModeSelection.Normal, NavigationAidSelection.None);
                var inputs = new CellWatchAdvanceInputs(
                    ResolvedTravelAmount.CellTransitions(1,
                        new ResolutionProvenance(ResolutionSource.DmOverride,
                            "DM-confirmed uncalibrated cell step")));
                var advanceRoute = $"/api/expeditions/{expeditionId:D}/cells/advance";
                using var paused = await client.PostAsJsonAsync(advanceRoute,
                    new AdvanceCellWatchRequest(
                        startedBody.GetProperty("version").GetInt64(), ambiguousPlan, inputs),
                    ApiJson);
                Assert.True(paused.IsSuccessStatusCode, await paused.Content.ReadAsStringAsync());
                var pausedState = await paused.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("CellCourseAdjudicationRequired", pausedState.GetProperty("expedition")
                    .GetProperty("activeWatchPendingDecision").GetString());

                // Resume that same watch using the second atomic half of the
                // split edge, not a fabricated geometric-side destination.
                var chosen = interfaces.Single(i => i.GetProperty("interfaceIndex").GetInt32() == 1);
                var begin = chosen.GetProperty("start");
                var end = chosen.GetProperty("end");
                var heading = new WorldPoint(
                    (begin.GetProperty("x").GetDouble() + end.GetProperty("x").GetDouble()) / 2 - center.X,
                    (begin.GetProperty("y").GetDouble() + end.GetProperty("y").GetDouble()) / 2 - center.Y);
                var choice = new ResolveCellCourseRequest(
                    pausedState.GetProperty("version").GetInt64(),
                    new CellCourseDecision(heading, 1,
                        new ResolutionProvenance(ResolutionSource.DmOverride, "DM selected split interface")));
                using var resolved = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/course", choice, ApiJson);
                Assert.True(resolved.IsSuccessStatusCode, await resolved.Content.ReadAsStringAsync());
                var resolvedState = await resolved.Content.ReadFromJsonAsync<JsonElement>();

                using var resumed = await client.PostAsJsonAsync(advanceRoute,
                    new AdvanceCellWatchRequest(resolvedState.GetProperty("version").GetInt64(),
                        new CellWatchTravelPlan(heading, false, false,
                            TravelModeSelection.Normal, NavigationAidSelection.None),
                        inputs), ApiJson);
                Assert.True(resumed.IsSuccessStatusCode, await resumed.Content.ReadAsStringAsync());
                var completed = await resumed.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(1, completed.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
                Assert.Equal(chosen.GetProperty("neighborCell").GetRawText(),
                    completed.GetProperty("expedition").GetProperty("currentCell").GetRawText());
            }

            var persisted = await new PostgresHexCrawlStore(database).GetOverworldAsync(worldId, "alice");
            Assert.NotNull(persisted);
            var tiling = persisted!.World.SpatialTiling;
            tiling.Validate();
            Assert.Equal(2, tiling.Realization.Constraints.Count);
            Assert.Equal(new[] { 0, 0, 1, 2, 2, 3 },
                tiling.Topology.MotifCells[0].Boundary.Select(e => e.BoundarySideIndex));
            Assert.Null(tiling.PhysicalDistancePerWorldUnit);
            Assert.All(tiling.Boundaries(new("square", new(0, 0))), edge =>
                Assert.Equal(edge.InterfaceIndex, tiling.Reciprocal(edge).ReciprocalInterfaceIndex));
            var savedExpedition = await new PostgresHexCrawlStore(database)
                .GetExpeditionAsync(expeditionId, "alice");
            Assert.NotNull(savedExpedition);
            var savedRuntime = Assert.IsType<CellExpeditionState>(savedExpedition!.Runtime);
            Assert.Equal(1, savedRuntime.CompletedWatches);
            Assert.Equal(4, savedRuntime.Traversal.EntryInterfaceIndex);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task ContradictoryExplicitGeometryNeverCreatesWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var rejected = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "Bent split",
                tilingDsSymbol = Pattern,
                worldUnitsPerAbstractPeriod = 1.0,
                origin = new { x = 0.0, y = 0.0 },
                straightEdgeSubdivisions = new[] { new PeriodicStraightEdgeSubdivision("square", 1, .25) },
                topologyWitness = Topology(),
                metricRealization = Geometry()
            });
            Assert.False(rejected.IsSuccessStatusCode);
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/overworlds")).EnumerateArray());
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
