using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class PeriodicMapRegistrationRecordPreservationTests
{
    [Fact]
    public async Task SameProjectReimportAndExplicitPeriodicRegistrationPreserveAll2616Records()
    {
        var path = PostgresTestDatabase.CreateConnectionString();
        try
        {
            var store = await OpenStoreAsync(path);
            var worlds = new HexCrawlService(store);
            var sourceMaps = new SourceMapApplicationService(store);

            var realized = DelaneyDressHarmonicMetricRealization.Construct(
                "<1:1,1,1:6,3>", 1, "world-unit");
            Assert.Equal("realized", realized.Status);
            var tiling = new PeriodicWorldTiling(
                Guid.NewGuid(), realized.Topology!, realized.Realization!,
                new WorldPoint(0, 0),
                PhysicalDistancePerWorldUnit: new DistanceMeasure(12, DistanceUnit.Miles));
            var world = await worlds.CreatePeriodicOverworldAsync(
                "alice", "Humblewood record preservation", tiling);

            world = await sourceMaps.CreateUploadedAsync(
                world.World.Id,
                "alice",
                new UploadSourceMapCommand(
                    "humblewood",
                    "Humblewood baked grid",
                    SourceMapRole.Gm,
                    "maps/humblewood/raster/v1",
                    true,
                    2048,
                    1536,
                    "image/png",
                    "Humblewood_Hex.png",
                    world.Version));
            var mapId = Assert.Single(world.World.SourceMaps).Id;

            var records = Enumerable.Range(0, 2616)
                .Select(index => new SourceMapContentElement(
                    $"record:{index}",
                    (index % 4) switch
                    {
                        0 => SourceMapContentKind.Label,
                        1 => SourceMapContentKind.Symbol,
                        2 => SourceMapContentKind.Line,
                        _ => SourceMapContentKind.Region
                    },
                    $"Record {index}",
                    null,
                    new WorldPoint(index % 2048, (index * 7) % 1536),
                    [],
                    new Dictionary<string, string> { ["fixture.index"] = index.ToString() }))
                .ToArray();
            var provenance = new SourceMapImportProvenance(
                "wonderdraft",
                "same-project-fingerprint",
                DateTimeOffset.Parse("2026-09-26T12:00:00Z"),
                records.Length);
            var archive = new SourceMapSourceArchive(
                "maps/humblewood/archive/v1",
                16_199_110,
                "application/octet-stream",
                "Humblewood_Expanded_v0.3.wonderdraft_map");
            var badAlignment = MapRegistrationTransform.Affine(
                0.20,
                0.025,
                -0.015,
                0.19,
                7,
                -3);

            world = await sourceMaps.ImportContentAsync(
                world.World.Id,
                mapId,
                "alice",
                new ImportSourceMapContentCommand(
                    records,
                    provenance,
                    archive,
                    badAlignment,
                    world.Version));
            AssertImportedState(world, mapId, records.Length, badAlignment, provenance, archive);

            // Re-importing the same project is content refresh, not alignment repair. A null
            // incoming alignment deliberately preserves the already-saved registration.
            world = await sourceMaps.ImportContentAsync(
                world.World.Id,
                mapId,
                "alice",
                new ImportSourceMapContentCommand(
                    records,
                    provenance,
                    archive,
                    null,
                    world.Version));
            AssertImportedState(world, mapId, records.Length, badAlignment, provenance, archive);

            var repairedAlignment = MapRegistrationTransform.Affine(
                0.24,
                0,
                0,
                0.24,
                -205,
                -161);
            world = await sourceMaps.RegisterComputedAffineAsync(
                world.World.Id, mapId, "alice", repairedAlignment, world.Version);
            Assert.Equal(tiling.Id, world.World.SpatialTiling.Id);
            Assert.Equal(tiling.Topology.TranslationDsSymbol,
                world.World.SpatialTiling.Topology.TranslationDsSymbol);
            AssertImportedState(world, mapId, records.Length, repairedAlignment, provenance, archive);

            var reloadedStore = await OpenStoreAsync(path);
            var reloadedService = new SourceMapApplicationService(reloadedStore);
            var reloaded = await reloadedService.GetOverworldAsync(world.World.Id, "alice");
            AssertImportedState(reloaded, mapId, records.Length, repairedAlignment, provenance, archive);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static void AssertImportedState(
        StoredOverworld world,
        Guid mapId,
        int expectedCount,
        MapRegistrationTransform expectedAlignment,
        SourceMapImportProvenance expectedProvenance,
        SourceMapSourceArchive expectedArchive)
    {
        var map = world.World.SourceMaps.Single(item => item.Id == mapId);
        Assert.Equal(expectedAlignment, map.Alignment);
        Assert.Equal(expectedCount, map.ImportedContent!.Count);
        Assert.Equal(
            expectedCount,
            map.ImportedContent.Select(item => item.SourceRecordKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("record:0", map.ImportedContent[0].SourceRecordKey);
        Assert.Equal($"record:{expectedCount - 1}", map.ImportedContent[^1].SourceRecordKey);
        Assert.Equal(expectedProvenance, map.ImportProvenance);
        Assert.Equal(expectedArchive, map.SourceArchive);
    }

    private static async Task<PostgresHexCrawlStore> OpenStoreAsync(string path)
    {
        var store = new PostgresHexCrawlStore(path);
        await store.InitializeAsync();
        return store;
    }

    private static void DeleteDatabase(string path) => PostgresTestDatabase.Delete(path);
}
