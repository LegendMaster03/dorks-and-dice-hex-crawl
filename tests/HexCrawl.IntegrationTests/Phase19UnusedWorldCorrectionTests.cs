using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class Phase19UnusedWorldCorrectionTests
{
    [Fact]
    public async Task EmptyWorldCorrectionIsVersionedAtomicAndRetainsImages()
    {
        var db = TestWebHost.NewDatabasePath();
        try
        {
            var store = new PostgresHexCrawlStore(db);
            var (original, sourceId) = await CreateWorldAsync(store);
            var oldId = original.World.SpatialTiling.Id;
            var newTiling = BuildTiling("<1:1,1,1:3,6>", revision: 2);
            var alignment = MapRegistrationTransform.Affine(.01, 0, 0, .01, 10, 20);
            var sourceMaps = new SourceMapApplicationService(store);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                sourceMaps.ReplaceUnusedPeriodicWorldAsync(
                    original.World.Id, sourceId, "alice",
                    newTiling with { PlayableBounds = null }, alignment, 1));
            var beforeCorrection = await store.GetOverworldAsync(original.World.Id, "alice");
            Assert.NotNull(beforeCorrection);
            Assert.Equal(1L, beforeCorrection!.Version);
            Assert.Equal(new PeriodicMapBounds(5, 5, -2, -2),
                beforeCorrection.World.SpatialTiling.PlayableBounds);
            var corrected = await sourceMaps.ReplaceUnusedPeriodicWorldAsync(
                original.World.Id, sourceId, "alice", newTiling, alignment, 1);
            Assert.Equal(2L, corrected.Version);
            Assert.Equal(original.World.Id, corrected.World.Id);
            Assert.NotEqual(oldId, corrected.World.SpatialTiling.Id);
            Assert.Equal(newTiling.Id, corrected.World.SpatialTiling.Id);
            Assert.Equal(new PeriodicMapBounds(5, 5, -2, -2),
                corrected.World.SpatialTiling.PlayableBounds);
            var map = Assert.Single(corrected.World.SourceMaps);
            Assert.Equal(sourceId, map.Id);
            Assert.Equal("existing-image-binary", map.AssetKey);
            Assert.Equal("Reference image", map.Name);
            Assert.Equal(alignment, map.Alignment);
            Assert.Equal(4, map.WorldCoverageBoundary.Count);

            var restarted = new PostgresHexCrawlStore(db);
            await restarted.InitializeAsync();
            var persisted = await restarted.GetOverworldAsync(original.World.Id, "alice");
            Assert.NotNull(persisted);
            Assert.Equal(newTiling.Id, persisted!.World.SpatialTiling.Id);
            Assert.Equal(new PeriodicMapBounds(5, 5, -2, -2),
                persisted.World.SpatialTiling.PlayableBounds);
            Assert.Equal(alignment, Assert.Single(persisted.World.SourceMaps).Alignment);
            await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() =>
                sourceMaps.ReplaceUnusedPeriodicWorldAsync(
                    original.World.Id, sourceId, "alice",
                    BuildTiling("<1:1,1,1:4,4>", 3), alignment, 1));
            Assert.Null(await restarted.GetOverworldAsync(original.World.Id, "bob"));
        }
        finally { TestWebHost.DeleteDatabase(db); }
    }

    [Fact]
    public async Task ExistingExpeditionBlocksReplacementWithoutPartialWrites()
    {
        var db = TestWebHost.NewDatabasePath();
        try
        {
            var store = new PostgresHexCrawlStore(db);
            var (world, sourceId) = await CreateWorldAsync(store);
            using (var host = TestWebHost.Create(db, "alice"))
            using (var client = host.CreateClient())
            {
                using var prepared = await client.PostAsJsonAsync(
                    $"/api/overworlds/{world.World.Id:D}/cell-procedures", new { });
                prepared.EnsureSuccessStatusCode();
                var rules = await prepared.Content.ReadFromJsonAsync<JsonElement>();
                using var started = await client.PostAsJsonAsync(
                    $"/api/overworlds/{world.World.Id:D}/cell-expeditions", new
                    {
                        name = "Do not lose this journey",
                        procedureId = rules.GetProperty("procedureId").GetGuid(),
                        procedureRevision = rules.GetProperty("revision").GetInt32(),
                        presentationKey = "dm-controlled",
                        startCell = new {
                            motifCellId = world.World.SpatialTiling.Topology.MotifCells[0].Id,
                            translation = new { u = 0, v = 0 }
                        }
                    });
                Assert.True(started.IsSuccessStatusCode,
                    await started.Content.ReadAsStringAsync());
            }
            var sourceMaps = new SourceMapApplicationService(store);
            await Assert.ThrowsAsync<HexCrawlConflictException>(() =>
                sourceMaps.ReplaceUnusedPeriodicWorldAsync(
                    world.World.Id, sourceId, "alice",
                    BuildTiling("<1:1,1,1:3,6>", 2),
                    MapRegistrationTransform.Affine(1, 0, 0, 1, 0, 0), 1));
            var retained = await store.GetOverworldAsync(world.World.Id, "alice");
            Assert.NotNull(retained);
            Assert.Equal(1, retained!.Version);
            Assert.Equal(world.World.SpatialTiling.Id, retained.World.SpatialTiling.Id);
            Assert.Null(Assert.Single(retained.World.SourceMaps).Alignment);
            Assert.Single(await store.ListExpeditionsAsync(world.World.Id, "alice"));
        }
        finally { TestWebHost.DeleteDatabase(db); }
    }

    private static async Task<(StoredOverworld World, Guid SourceId)> CreateWorldAsync(
        PostgresHexCrawlStore store)
    {
        var sourceId = Guid.NewGuid();
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Existing unused world",
            Tiling = BuildTiling("<1:1,1,1:4,4>", 1),
            SourceMaps =
            [
                new SourceMapRepresentation(
                    sourceId, "world", "Reference image", SourceMapRole.Gm,
                    "existing-image-binary", true, null, [], 512, 512, "image/png")
            ]
        };
        var now = DateTimeOffset.UtcNow;
        var saved = await store.CreateOverworldAsync(
            new StoredOverworld(world, "alice", 1, now, now));
        return (saved, sourceId);
    }

    private static PeriodicWorldTiling BuildTiling(string symbol, int revision)
    {
        var built = DelaneyDressHarmonicMetricRealization.Construct(
            symbol, 2, "world-unit");
        Assert.Equal("realized", built.Status);
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(), built.Topology!,
            built.Realization!, new WorldPoint(0, 0), revision)
        {
            PlayableBounds = new PeriodicMapBounds(5, 5, -2, -2)
        };
        tiling.Validate();
        return tiling;
    }
}
