using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.IntegrationTests;

public sealed class Phase19WorldGeometryEndpointsTests
{
    [Fact]
    public async Task NewDefaultHexWorldHasRegularEuclideanCellGeometry()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "Hex default geometry",
                tilingDsSymbol = "<1:1,1,1:6,3>",
                worldUnitsPerAbstractPeriod = 1.0,
                origin = new { x = 0.0, y = 0.0 }
            });
            created.EnsureSuccessStatusCode();
            var world = await created.Content.ReadFromJsonAsync<JsonElement>();
            using var viewport = await client.GetAsync(
                $"/api/overworlds/{world.GetProperty("id").GetGuid():D}/cells/viewport?minX=-3&minY=-3&maxX=3&maxY=3");
            viewport.EnsureSuccessStatusCode();
            var cells = await viewport.Content.ReadFromJsonAsync<JsonElement>();
            var polygon = cells.EnumerateArray().First().GetProperty("polygon").EnumerateArray()
                .Select(point => (X: point.GetProperty("x").GetDouble(),
                    Y: point.GetProperty("y").GetDouble())).ToArray();
            Assert.Equal(6, polygon.Length);
            var lengths = Enumerable.Range(0, polygon.Length).Select(index =>
            {
                var from = polygon[index];
                var to = polygon[(index + 1) % polygon.Length];
                return Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
            }).ToArray();
            Assert.All(lengths, length => Assert.InRange(length / lengths[0], 0.9999, 1.0001));
            for (var index = 0; index < polygon.Length; index++)
            {
                var previous = polygon[(index + polygon.Length - 1) % polygon.Length];
                var current = polygon[index];
                var next = polygon[(index + 1) % polygon.Length];
                var ax = previous.X - current.X;
                var ay = previous.Y - current.Y;
                var bx = next.X - current.X;
                var by = next.Y - current.Y;
                var angle = Math.Acos(Math.Clamp(
                    (ax * bx + ay * by) / (lengths[(index + polygon.Length - 1) % polygon.Length] * lengths[index]),
                    -1, 1)) * 180 / Math.PI;
                Assert.InRange(angle, 119.99, 120.01);
            }
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task OptionalSquareMapExtentClampsViewportAndRejectsOutsideCells()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "Five period square map",
                tilingDsSymbol = "<1:1,1,1:4,4>",
                worldUnitsPerAbstractPeriod = 1.0,
                origin = new { x = 0.0, y = 0.0 },
                playableBounds = new { width = 5, height = 5 }
            });
            created.EnsureSuccessStatusCode();
            var world = await created.Content.ReadFromJsonAsync<JsonElement>();
            var id = world.GetProperty("id").GetGuid();
            Assert.Equal(5, world.GetProperty("tiling")
                .GetProperty("playableBounds").GetProperty("width").GetInt32());
            var motif = world.GetProperty("tiling").GetProperty("topology")
                .GetProperty("motifCells").EnumerateArray().First().GetProperty("id").GetString()!;

            using var huge = await client.GetAsync(
                $"/api/overworlds/{id:D}/cells/viewport?minX=-100000&minY=-100000&maxX=100000&maxY=100000");
            huge.EnsureSuccessStatusCode();
            var cells = await huge.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(25, cells.GetArrayLength());

            using var outside = await client.GetAsync(
                $"/api/overworlds/{id:D}/cells/resolve?motifCellId={Uri.EscapeDataString(motif)}&u=5&v=0");
            Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task PeriodicWorldWithoutImageCanBeCreatedEnumeratedAndSelected()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            JsonElement cell;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Generated square world",
                    tilingDsSymbol = "<1:1,1,1:4,4>",
                    worldUnitsPerAbstractPeriod = 3.0,
                    origin = new { x = 12.0, y = -4.0 }
                });
                created.EnsureSuccessStatusCode();
                var world = await created.Content.ReadFromJsonAsync<JsonElement>();
                worldId = world.GetProperty("id").GetGuid();
                Assert.False(world.TryGetProperty("grid", out _));
                Assert.NotEqual(JsonValueKind.Null, world.GetProperty("tiling").ValueKind);
                Assert.Equal(0, world.GetProperty("sourceMaps").GetArrayLength());
                using var viewport = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/viewport?minX=8&minY=-8&maxX=16&maxY=0");
                viewport.EnsureSuccessStatusCode();
                var cells = await viewport.Content.ReadFromJsonAsync<JsonElement>();
                Assert.NotEmpty(cells.EnumerateArray());
                cell = cells.EnumerateArray().First();
                var center = cell.GetProperty("center");
                using var containing = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/containing?x={center.GetProperty("x").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture)}&y={center.GetProperty("y").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                containing.EnsureSuccessStatusCode();
                var lookup = await containing.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Unique", lookup.GetProperty("status").GetString());
                Assert.Single(lookup.GetProperty("candidates").EnumerateArray());
                var address = cell.GetProperty("id").GetProperty("address");
                using var resolved = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/resolve?motifCellId={Uri.EscapeDataString(address.GetProperty("motifCellId").GetString()!)}&u={address.GetProperty("translation").GetProperty("u").GetInt64()}&v={address.GetProperty("translation").GetProperty("v").GetInt64()}");
                resolved.EnsureSuccessStatusCode();
                var result = await resolved.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(
                    cell.GetProperty("id").GetProperty("tilingId").GetGuid(),
                    result.GetProperty("id").GetProperty("tilingId").GetGuid());
                Assert.True(result.GetProperty("polygon").GetArrayLength() >= 3);
                // Complete the image-free workflow without a custom procedure
                // JSON editor or a reconfigured preset template.
                using var createdProcedure = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures", new { });
                createdProcedure.EnsureSuccessStatusCode();
                var pinned = await createdProcedure.Content.ReadFromJsonAsync<JsonElement>();
                using var compatible = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures/compatibility?procedureId={pinned.GetProperty("procedureId").GetGuid()}&revision={pinned.GetProperty("revision").GetInt32()}");
                compatible.EnsureSuccessStatusCode();
                var assessedProcedure = await compatible.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("ready", assessedProcedure.GetProperty("status").GetString());
                Assert.Equal(world.GetProperty("version").GetInt64(),
                    assessedProcedure.GetProperty("worldVersion").GetInt64());
                using var start = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions", new
                    {
                        name = "First cell journey",
                        procedureId = pinned.GetProperty("procedureId").GetGuid(),
                        procedureRevision = pinned.GetProperty("revision").GetInt32(),
                        presentationKey = "dm-controlled",
                        startCell = address
                    });
                start.EnsureSuccessStatusCode();
                var started = await start.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(started.GetProperty("expedition").TryGetProperty("currentHex", out _));
                Assert.Equal("CellSteps", started.GetProperty("procedure")
                    .GetProperty("runtime").GetProperty("travelResolution").GetString());
                Assert.Equal(cell.GetProperty("id").GetProperty("tilingId").GetGuid(),
                    started.GetProperty("expedition").GetProperty("currentCell")
                        .GetProperty("tilingId").GetGuid());
                // Metadata-only rename must preserve stored traversal and
                // the underlying world geometry after an expedition exists.
                using var renamed = await client.PutAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/name", new
                    {
                        name = "Renamed periodic world",
                        expectedVersion = world.GetProperty("version").GetInt64()
                    });
                renamed.EnsureSuccessStatusCode();
                var renamedWorld = await renamed.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Renamed periodic world", renamedWorld.GetProperty("name").GetString());
                Assert.False(renamedWorld.TryGetProperty("grid", out _));
                Assert.Equal(cell.GetProperty("id").GetProperty("tilingId").GetGuid(),
                    renamedWorld.GetProperty("tiling").GetProperty("id").GetGuid());
            }

            using (var otherHost = TestWebHost.Create(database, "bob"))
            using (var other = otherHost.CreateClient())
            {
                Assert.Equal(HttpStatusCode.NotFound,
                    (await other.GetAsync($"/api/overworlds/{worldId:D}/cells/viewport?minX=0&minY=0&maxX=1&maxY=1")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound,
                    (await other.GetAsync($"/api/overworlds/{worldId:D}/cells/containing?x=0&y=0")).StatusCode);
                using var deniedProcedure = await other.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures", new { });
                Assert.Equal(HttpStatusCode.NotFound, deniedProcedure.StatusCode);
                using var deniedCompatibility = await other.GetAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures/compatibility?procedureId={Guid.NewGuid():D}&revision=1");
                Assert.Equal(HttpStatusCode.NotFound, deniedCompatibility.StatusCode);
            }
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task GuidedNonuniformMetricParametersAreValidatedAndPersisted()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid id;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Skewed repeating cells",
                    tilingDsSymbol = "<2:1 2,1 2,2:4 4,4>",
                    worldUnitsPerAbstractPeriod = 2.0,
                    origin = new { x = 12.0, y = -4.0 },
                    rotationDegrees = 17.0,
                    periodULengthWorldUnits = 3.5,
                    periodVLengthWorldUnits = 2.25,
                    periodAngleDegrees = 73.0
                });
                Assert.True(created.IsSuccessStatusCode,
                    await created.Content.ReadAsStringAsync());
                var body = await created.Content.ReadFromJsonAsync<JsonElement>();
                id = body.GetProperty("id").GetGuid();

                using var invalid = await client.PostAsJsonAsync("/api/overworlds/periodic", new
                {
                    name = "Rejected angle",
                    tilingDsSymbol = "<1:1,1,1:4,4>",
                    worldUnitsPerAbstractPeriod = 2.0,
                    origin = new { x = 0.0, y = 0.0 },
                    periodAngleDegrees = 180.0
                });
                Assert.False(invalid.IsSuccessStatusCode);
            }
            var store = new HexCrawl.Infrastructure.Persistence.PostgresHexCrawlStore(database);
            var stored = await store.GetOverworldAsync(id, "alice");
            Assert.NotNull(stored);
            var tiling = stored!.World.SpatialTiling;
            tiling.Validate();
            var u = tiling.Realization.TranslationU;
            var v = tiling.Realization.TranslationV;
            Assert.InRange(Math.Sqrt(u.X * u.X + u.Y * u.Y), 3.5 - 1e-6, 3.5 + 1e-6);
            Assert.InRange(Math.Sqrt(v.X * v.X + v.Y * v.Y), 2.25 - 1e-6, 2.25 + 1e-6);
            double cosine = (u.X * v.X + u.Y * v.Y)
                / (Math.Sqrt(u.X * u.X + u.Y * u.Y) * Math.Sqrt(v.X * v.X + v.Y * v.Y));
            Assert.InRange(Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI,
                72.999, 73.001);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task InvalidPatternDoesNotPersistAWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "Invalid metric world",
                tilingDsSymbol = "<1:1,1,1:7,3>",
                worldUnitsPerAbstractPeriod = 0,
                origin = new { x = 0, y = 0 }
            });
            Assert.False(created.IsSuccessStatusCode);
            var worlds = await client.GetFromJsonAsync<JsonElement>("/api/overworlds");
            Assert.Empty(worlds.EnumerateArray());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
