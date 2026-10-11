using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class PeriodicStraightEdgeWorldEndpointsTests
{
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
            Guid worldId;
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
                var expeditionId = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
                Assert.Equal("CellSteps", (await started.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("procedure").GetProperty("runtime").GetProperty("travelResolution").GetString());
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
