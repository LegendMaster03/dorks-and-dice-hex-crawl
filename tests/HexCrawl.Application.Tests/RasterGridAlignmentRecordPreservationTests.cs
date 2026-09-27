using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class RasterGridAlignmentRecordPreservationTests
{
    [Fact]
    public async Task SameProjectReimportDoesNotDuplicateOrRepairAndExplicitRepairPreservesAll2616Records()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hex-crawl-grid-records-{Guid.NewGuid():N}.db");
        try
        {
            var store = await OpenStoreAsync(path);
            var worlds = new HexCrawlService(store);
            var sourceMaps = new SourceMapApplicationService(store);

            var world = await worlds.CreateOverworldAsync(
                "alice",
                new CreateOverworldCommand(
                    "Humblewood record preservation",
                    HexOrientation.PointyTop,
                    new WorldPoint(0, 0),
                    0,
                    1,
                    12,
                    DistanceUnit.Miles));

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
            var repairedGrid = world.World.Grid with
            {
                Orientation = HexOrientation.FlatTop,
                Origin = new WorldPoint(41.25, -17.5),
                RotationDegrees = 0,
                HexRadiusWorldUnits = 2.75
            };

            world = await sourceMaps.ApplyRasterGridAlignmentAsync(
                world.World.Id,
                mapId,
                "alice",
                new ApplyRasterGridAlignmentCommand(
                    repairedGrid,
                    repairedAlignment,
                    world.Version));
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

    private static async Task<SqliteHexCrawlStore> OpenStoreAsync(string path)
    {
        var store = new SqliteHexCrawlStore($"Data Source={path}");
        await store.InitializeAsync();
        return store;
    }

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}
