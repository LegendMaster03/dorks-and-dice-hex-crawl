using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class PeriodicMapRegistrationTests
{
    [Fact]
    public async Task ComputedAffineRegistrationPreservesGeometryAndIndependentSourceMaps()
    {
        var path = TemporaryDatabasePath();
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
                "alice", "Registered periodic world", tiling);

            world = await worlds.CreateLocationAsync(
                world.World.Id,
                "alice",
                new CreateLocationCommand(
                    "Old Harbor",
                    "settlement",
                    new WorldPoint(11, 13),
                    LocationDiscoverability.Obvious,
                    world.Version));
            var location = Assert.Single(world.World.Locations);

            world = await worlds.CreateFeatureAsync(
                world.World.Id,
                "alice",
                new CreateFeatureCommand(
                    "Trade road",
                    "road",
                    SpatialFeatureKind.Line,
                    null,
                    [new WorldPoint(1, 2), new WorldPoint(7, 9)],
                    null,
                    world.Version));
            var feature = Assert.IsType<LinearFeature>(Assert.Single(world.World.Features));

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
            var repairedMapId = Assert.Single(world.World.SourceMaps).Id;

            var oldAlignment = MapRegistrationTransform.Affine(
                0.20,
                0.025,
                -0.015,
                0.19,
                7,
                -3);
            var archive = new SourceMapSourceArchive(
                "maps/humblewood/archive/v1",
                123_456,
                "application/octet-stream",
                "Humblewood_Expanded_v0.3.wonderdraft_map");
            var provenance = new SourceMapImportProvenance(
                "wonderdraft",
                "fixture-fingerprint",
                DateTimeOffset.Parse("2026-09-26T12:00:00Z"),
                2616);
            var content = new[]
            {
                new SourceMapContentElement(
                    "label:0",
                    SourceMapContentKind.Label,
                    "Old Harbor",
                    null,
                    new WorldPoint(1024, 768),
                    [],
                    new Dictionary<string, string>()),
                new SourceMapContentElement(
                    "symbol:0",
                    SourceMapContentKind.Symbol,
                    "Castle",
                    "town",
                    new WorldPoint(500, 600),
                    [],
                    new Dictionary<string, string> { ["texture"] = "castle" })
            };
            world = await sourceMaps.ImportContentAsync(
                world.World.Id,
                repairedMapId,
                "alice",
                new ImportSourceMapContentCommand(
                    content,
                    provenance,
                    archive,
                    oldAlignment,
                    world.Version));

            world = await sourceMaps.CreateUploadedAsync(
                world.World.Id,
                "alice",
                new UploadSourceMapCommand(
                    "humblewood",
                    "Player reference",
                    SourceMapRole.Player,
                    "maps/humblewood/player/v1",
                    false,
                    1000,
                    750,
                    "image/png",
                    "player.png",
                    world.Version));
            var otherMapId = world.World.SourceMaps.Single(map => map.Id != repairedMapId).Id;
            world = await sourceMaps.RegisterAsync(
                world.World.Id,
                otherMapId,
                "alice",
                new RegisterSourceMapCommand(
                    [
                        new MapRegistrationControlPoint(new WorldPoint(0, 0), new WorldPoint(100, 200)),
                        new MapRegistrationControlPoint(new WorldPoint(1000, 0), new WorldPoint(200, 200)),
                        new MapRegistrationControlPoint(new WorldPoint(0, 750), new WorldPoint(100, 275))
                    ],
                    world.Version));
            var otherMapBefore = world.World.SourceMaps.Single(map => map.Id == otherMapId);
            var versionBeforeRepair = world.Version;

            var replacementAlignment = MapRegistrationTransform.Affine(
                0.24,
                0,
                0,
                0.24,
                -205,
                -161);

            world = await sourceMaps.RegisterComputedAffineAsync(
                world.World.Id, repairedMapId, "alice", replacementAlignment, world.Version);

            Assert.Equal(versionBeforeRepair + 1, world.Version);
            Assert.Equal(tiling.Id, world.World.SpatialTiling.Id);
            Assert.Equal(tiling.Topology.TranslationDsSymbol,
                world.World.SpatialTiling.Topology.TranslationDsSymbol);

            var retainedLocation = Assert.Single(world.World.Locations);
            Assert.Equal(location.Id, retainedLocation.Id);
            Assert.Equal(location.Name, retainedLocation.Name);
            Assert.Equal(location.Category, retainedLocation.Category);
            Assert.Equal(location.Position, retainedLocation.Position);
            Assert.Equal(location.Discoverability, retainedLocation.Discoverability);

            var retainedFeature = Assert.IsType<LinearFeature>(Assert.Single(world.World.Features));
            Assert.Equal(feature.Id, retainedFeature.Id);
            Assert.Equal(feature.Name, retainedFeature.Name);
            Assert.Equal(feature.Category, retainedFeature.Category);
            Assert.Equal(feature.Path, retainedFeature.Path);

            var repaired = world.World.SourceMaps.Single(map => map.Id == repairedMapId);
            Assert.Equal(replacementAlignment, repaired.Alignment);
            Assert.Equal(4, repaired.WorldCoverageBoundary.Count);
            Assert.Equal(2, repaired.ImportedContent!.Count);
            Assert.Equal("label:0", repaired.ImportedContent[0].SourceRecordKey);
            Assert.Equal("symbol:0", repaired.ImportedContent[1].SourceRecordKey);
            Assert.Equal(provenance, repaired.ImportProvenance);
            Assert.Equal(archive, repaired.SourceArchive);
            Assert.Equal("maps/humblewood/raster/v1", repaired.AssetKey);

            var otherMapAfter = world.World.SourceMaps.Single(map => map.Id == otherMapId);
            AssertSourceMapEquivalent(otherMapBefore, otherMapAfter);

            var restartedStore = await OpenStoreAsync(path);
            var restarted = new SourceMapApplicationService(restartedStore);
            var reloaded = await restarted.GetOverworldAsync(world.World.Id, "alice");
            Assert.Equal(world.Version, reloaded.Version);
            Assert.Equal(tiling.Id, reloaded.World.SpatialTiling.Id);
            Assert.Equal(tiling.Topology.TranslationDsSymbol,
                reloaded.World.SpatialTiling.Topology.TranslationDsSymbol);
            var reloadedMap = reloaded.World.SourceMaps.Single(map => map.Id == repairedMapId);
            Assert.Equal(replacementAlignment, reloadedMap.Alignment);
            Assert.Collection(
                reloadedMap.ImportedContent!,
                item => Assert.Equal("label:0", item.SourceRecordKey),
                item => Assert.Equal("symbol:0", item.SourceRecordKey));
            Assert.Equal(provenance, reloadedMap.ImportProvenance);
            Assert.Equal(archive, reloadedMap.SourceArchive);
            AssertSourceMapEquivalent(
                otherMapBefore,
                reloaded.World.SourceMaps.Single(map => map.Id == otherMapId));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task ActiveExpeditionCanRegisterImagesWithoutChangingItsWorldGeometry()
    {
        var path = TemporaryDatabasePath();
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
                "alice", "Active crawl", tiling);
            world = await sourceMaps.CreateUploadedAsync(
                world.World.Id,
                "alice",
                new UploadSourceMapCommand(
                    "test",
                    "Baked grid",
                    SourceMapRole.Gm,
                    "maps/test/v1",
                    true,
                    800,
                    600,
                    "image/png",
                    "map.png",
                    world.Version));
            var map = Assert.Single(world.World.SourceMaps);
            var versionBeforeExpedition = world.Version;
            _ = await new ExpeditionStartService(
                store, worlds, new CampaignProcedureService(store))
                .StartWorldBoundCellsAsync(
                    world.World.Id, "alice", "Test crawl",
                    new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                    new PeriodicCellAddress(tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));

            var proposedAlignment = MapRegistrationTransform.Affine(0.1, 0, 0, 0.1, 5, 6);
            world = await sourceMaps.RegisterComputedAffineAsync(
                world.World.Id, map.Id, "alice", proposedAlignment, versionBeforeExpedition);

            Assert.Equal(versionBeforeExpedition + 1, world.Version);
            Assert.Equal(tiling.Id, world.World.SpatialTiling.Id);
            Assert.Equal(tiling.Topology.TranslationDsSymbol,
                world.World.SpatialTiling.Topology.TranslationDsSymbol);
            Assert.Equal(proposedAlignment, Assert.Single(world.World.SourceMaps).Alignment);
            var reopened = await sourceMaps.GetOverworldAsync(world.World.Id, "alice");
            Assert.Equal(tiling.Id, reopened.World.SpatialTiling.Id);
            Assert.Equal(tiling.Topology.TranslationDsSymbol,
                reopened.World.SpatialTiling.Topology.TranslationDsSymbol);
            Assert.Equal(proposedAlignment, Assert.Single(reopened.World.SourceMaps).Alignment);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static void AssertSourceMapEquivalent(
        SourceMapRepresentation expected,
        SourceMapRepresentation actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.GeographyKey, actual.GeographyKey);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Role, actual.Role);
        Assert.Equal(expected.AssetKey, actual.AssetKey);
        Assert.Equal(expected.ContainsBakedGrid, actual.ContainsBakedGrid);
        Assert.Equal(expected.Alignment, actual.Alignment);
        Assert.Equal(expected.WorldCoverageBoundary, actual.WorldCoverageBoundary);
        Assert.Equal(expected.PixelWidth, actual.PixelWidth);
        Assert.Equal(expected.PixelHeight, actual.PixelHeight);
        Assert.Equal(expected.MediaType, actual.MediaType);
        Assert.Equal(expected.OriginalFileName, actual.OriginalFileName);
        Assert.Equal(expected.ImportProvenance, actual.ImportProvenance);
        Assert.Equal(expected.SourceArchive, actual.SourceArchive);

        if (expected.ImportedContent is null)
        {
            Assert.Null(actual.ImportedContent);
        }
        else
        {
            Assert.NotNull(actual.ImportedContent);
            Assert.Equal(expected.ImportedContent.Count, actual.ImportedContent!.Count);
            for (var index = 0; index < expected.ImportedContent.Count; index++)
            {
                Assert.Equal(expected.ImportedContent[index].SourceRecordKey, actual.ImportedContent[index].SourceRecordKey);
            }
        }
    }

    private static string TemporaryDatabasePath() => PostgresTestDatabase.CreateConnectionString();

    private static async Task<PostgresHexCrawlStore> OpenStoreAsync(string path)
    {
        var store = new PostgresHexCrawlStore(path);
        await store.InitializeAsync();
        return store;
    }

    private static void DeleteDatabase(string path) => PostgresTestDatabase.Delete(path);
}
