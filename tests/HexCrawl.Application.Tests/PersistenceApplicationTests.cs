using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class PersistenceApplicationTests
{
    [Fact]
    public async Task MultipleOverworldsAreOwnerScoped()
    {
        await using var database = await TestDatabase.CreateAsync();
        var alice = await database.ServiceAsync();
        var bob = await database.ServiceAsync();
        var first = await alice.CreateOverworldAsync("alice", WorldCommand("First"));
        _ = await alice.CreateOverworldAsync("alice", WorldCommand("Second"));
        _ = await bob.CreateOverworldAsync("bob", WorldCommand("Bob world"));

        Assert.Equal(2, (await alice.ListOverworldsAsync("alice")).Count);
        Assert.Single(await bob.ListOverworldsAsync("bob"));
        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() => bob.GetOverworldAsync(first.World.Id, "bob"));
    }

    [Fact]
    public async Task GridAndCustomScaleRoundTripAcrossStoreRestart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var custom = DistanceUnit.Custom("league", 4828.032);
        var created = await service.CreateOverworldAsync("alice", new CreateOverworldCommand(
            "Custom grid",
            HexOrientation.FlatTop,
            new WorldPoint(4.5, -2),
            12.5,
            2.25,
            3,
            custom));
        var changedGrid = created.World.Grid with
        {
            Origin = new WorldPoint(8, 9),
            RotationDegrees = 20,
            NeighborCenterDistance = new DistanceMeasure(4, custom)
        };
        _ = await service.UpdateOverworldAsync(
            created.World.Id,
            "alice",
            new UpdateOverworldCommand("Renamed", changedGrid, created.Version));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetOverworldAsync(created.World.Id, "alice");
        Assert.Equal("Renamed", loaded.World.Name);
        Assert.Equal(HexOrientation.FlatTop, loaded.World.Grid.Orientation);
        Assert.Equal(new WorldPoint(8, 9), loaded.World.Grid.Origin);
        Assert.Equal(4, loaded.World.Grid.NeighborCenterDistance.Value);
        Assert.Equal("league", loaded.World.Grid.NeighborCenterDistance.Unit.Symbol);
        Assert.Equal(4828.032, loaded.World.Grid.NeighborCenterDistance.Unit.MetersPerUnit);
    }

    [Fact]
    public async Task LocationCrudPersists()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        world = await service.CreateLocationAsync(world.World.Id, "alice", new CreateLocationCommand(
            "Ruin", "ruin", new WorldPoint(.25, .75), LocationDiscoverability.Hidden, world.Version));
        var location = Assert.Single(world.World.Locations);
        world = await service.UpdateLocationAsync(world.World.Id, location.Id, "alice", new UpdateLocationCommand(
            "Old ruin", "ancient-ruin", new WorldPoint(.5, .6), LocationDiscoverability.Conditional, world.Version));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetOverworldAsync(world.World.Id, "alice");
        var persisted = Assert.Single(loaded.World.Locations);
        Assert.Equal("Old ruin", persisted.Name);
        Assert.Equal(new WorldPoint(.5, .6), persisted.Position);

        loaded = await restarted.DeleteLocationAsync(loaded.World.Id, persisted.Id, "alice", loaded.Version);
        Assert.Empty(loaded.World.Locations);
    }

    [Fact]
    public async Task PointFeatureCrudPersists()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        world = await service.CreateFeatureAsync(world.World.Id, "alice", new CreateFeatureCommand(
            "Spring", "spring", SpatialFeatureKind.Point, new WorldPoint(1.2, 2.3), null, null, world.Version));
        var feature = Assert.IsType<PointFeature>(Assert.Single(world.World.Features));
        world = await service.UpdateFeatureAsync(world.World.Id, feature.Id, "alice", new UpdateFeatureCommand(
            "Sacred spring", "water", SpatialFeatureKind.Point, new WorldPoint(2, 3), null, null, world.Version));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetOverworldAsync(world.World.Id, "alice");
        var persisted = Assert.IsType<PointFeature>(Assert.Single(loaded.World.Features));
        Assert.Equal(new WorldPoint(2, 3), persisted.Position);
        loaded = await restarted.DeleteFeatureAsync(loaded.World.Id, persisted.Id, "alice", loaded.Version);
        Assert.Empty(loaded.World.Features);
    }

    [Fact]
    public async Task LinearGeometryPersistsInWorldCoordinates()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        WorldPoint[] path = [new(0, 0), new(.25, 1.75), new(4.5, 3)];
        world = await service.CreateFeatureAsync(world.World.Id, "alice", new CreateFeatureCommand(
            "North road", "road", SpatialFeatureKind.Line, null, path, null, world.Version));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetOverworldAsync(world.World.Id, "alice");
        Assert.Equal(path, Assert.IsType<LinearFeature>(Assert.Single(loaded.World.Features)).Path);
    }

    [Fact]
    public async Task PolygonGeometryAndCustomTerrainCategoryPersist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        WorldPoint[] boundary = [new(0, 0), new(4, 0), new(2, 3)];
        world = await service.CreateFeatureAsync(world.World.Id, "alice", new CreateFeatureCommand(
            "Crystal marsh", "crystal-marsh", SpatialFeatureKind.Region, null, null, boundary, world.Version));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetOverworldAsync(world.World.Id, "alice");
        var region = Assert.IsType<RegionFeature>(Assert.Single(loaded.World.Features));
        Assert.Equal("crystal-marsh", region.Category);
        Assert.Equal(boundary, region.Boundary);
    }

    [Fact]
    public async Task BatchImportPersistsSemanticObjectsAtomicallyWithOneVersionIncrement()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var originalVersion = world.Version;

        world = await service.ImportWorldObjectsAsync(
            world.World.Id,
            "alice",
            new ImportWorldObjectsCommand(
                [new ImportedLocationDefinition(
                    "Old Harbor",
                    "settlement",
                    new WorldPoint(12, 23),
                    LocationDiscoverability.Obvious)],
                [new ImportedFeatureDefinition(
                    "Trade Road",
                    "road",
                    SpatialFeatureKind.Line,
                    null,
                    [new WorldPoint(10, 20), new WorldPoint(14, 26)],
                    null)],
                originalVersion));

        Assert.Equal(originalVersion + 1, world.Version);
        Assert.Equal("Old Harbor", Assert.Single(world.World.Locations).Name);
        Assert.Equal("Trade Road", Assert.IsType<LinearFeature>(Assert.Single(world.World.Features)).Name);

        var beforeInvalid = world;
        await Assert.ThrowsAsync<ArgumentException>(() => service.ImportWorldObjectsAsync(
            world.World.Id,
            "alice",
            new ImportWorldObjectsCommand(
                [new ImportedLocationDefinition(
                    "Should not persist",
                    "site",
                    new WorldPoint(1, 1),
                    LocationDiscoverability.Hidden)],
                [new ImportedFeatureDefinition(
                    "Invalid region",
                    "region",
                    SpatialFeatureKind.Region,
                    null,
                    null,
                    [new WorldPoint(0, 0), new WorldPoint(1, 1), new WorldPoint(2, 2)])],
                world.Version)));

        var reopened = await service.GetOverworldAsync(world.World.Id, "alice");
        Assert.Equal(beforeInvalid.Version, reopened.Version);
        Assert.Single(reopened.World.Locations);
        Assert.Single(reopened.World.Features);
    }

    [Fact]
    public async Task SourceMapMetadataPersistsLogicalAssetReference()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        world = await service.CreateSourceMapAsync(world.World.Id, "alice", new CreateSourceMapCommand(
            "northlands",
            "Northlands GM gridless",
            SourceMapRole.Gm,
            "maps/northlands/gm-gridless/v1",
            false,
            MapRegistrationTransform.Affine(1, 0, 0, 1, 4, 5),
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            world.Version));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetOverworldAsync(world.World.Id, "alice");
        var map = Assert.Single(loaded.World.SourceMaps);
        Assert.Equal("maps/northlands/gm-gridless/v1", map.AssetKey);
        Assert.Equal(new WorldPoint(5, 6), map.Alignment!.ToWorld(new WorldPoint(1, 1)));
    }

    [Fact]
    public async Task LegacySourceMapMetadataUpdatePreservesImportedSourceState()
    {
        await using var database = await TestDatabase.CreateAsync();
        var legacy = await database.ServiceAsync();
        var world = await legacy.CreateOverworldAsync("alice", WorldCommand());

        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var sourceMaps = new SourceMapApplicationService(store);
        world = await sourceMaps.CreateUploadedAsync(
            world.World.Id,
            "alice",
            new UploadSourceMapCommand(
                "northlands",
                "Imported map",
                SourceMapRole.Gm,
                "maps/northlands/imported/v1",
                false,
                100,
                100,
                "image/png",
                "map.png",
                world.Version));
        var mapId = Assert.Single(world.World.SourceMaps).Id;

        world = await sourceMaps.ImportContentAsync(
            world.World.Id,
            mapId,
            "alice",
            new ImportSourceMapContentCommand(
                [new SourceMapContentElement(
                    "label:0",
                    SourceMapContentKind.Label,
                    "Old Harbor",
                    null,
                    new WorldPoint(5, 5),
                    [],
                    new Dictionary<string, string> { ["size"] = "24" })],
                new SourceMapImportProvenance(
                    "wonderdraft",
                    "abc123",
                    DateTimeOffset.UtcNow,
                    1),
                new SourceMapSourceArchive(
                    "maps/source/archive/v1",
                    1234,
                    "application/octet-stream",
                    "campaign.wonderdraft_map"),
                null,
                world.Version));

        var imported = Assert.Single(world.World.SourceMaps);
        world = await legacy.UpdateSourceMapAsync(
            world.World.Id,
            mapId,
            "alice",
            new UpdateSourceMapCommand(
                "northlands",
                "Renamed imported map",
                SourceMapRole.Neutral,
                imported.AssetKey,
                true,
                imported.Alignment,
                imported.WorldCoverageBoundary,
                world.Version));

        var updated = Assert.Single(world.World.SourceMaps);
        Assert.Equal("Renamed imported map", updated.Name);
        Assert.Equal(SourceMapRole.Neutral, updated.Role);
        Assert.True(updated.ContainsBakedGrid);
        Assert.Equal(100, updated.PixelWidth);
        Assert.Equal(100, updated.PixelHeight);
        Assert.Equal("image/png", updated.MediaType);
        Assert.Equal("map.png", updated.OriginalFileName);
        Assert.Single(updated.ImportedContent!);
        Assert.Equal("abc123", updated.ImportProvenance!.SourceFingerprint);
        Assert.Equal("maps/source/archive/v1", updated.SourceArchive!.AssetKey);
        Assert.Equal("campaign.wonderdraft_map", updated.SourceArchive.OriginalFileName);
    }

    [Fact]
    public async Task OptimisticConcurrencyRejectsStaleWorldMutation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        _ = await service.CreateLocationAsync(world.World.Id, "alice", new CreateLocationCommand(
            "A", "site", new WorldPoint(0, 0), LocationDiscoverability.Obvious, world.Version));

        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() => service.CreateLocationAsync(
            world.World.Id,
            "alice",
            new CreateLocationCommand("B", "site", new WorldPoint(1, 1), LocationDiscoverability.Obvious, world.Version)));
    }

    [Fact]
    public async Task ExpeditionAndProcedureSnapshotSurviveRestart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await StartCellAsync(
            database, service, world.World.Id, "Survey", "alexandrian-advanced", new HexCoordinate(2, -1));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.Id, "alice");
        var loadedRuntime = GenericProcedureRuntime.Bind(loaded.CampaignProcedure);
        var cells = Assert.IsType<CellExpeditionState>(loaded.Runtime);
        Assert.Equal(world.World.SpatialTiling.Resolve(
            LegacyHexTilingCompatibility.ToAddress(new HexCoordinate(2, -1))).Id,
            cells.Traversal.CurrentCell);
        Assert.Equal("alexandrian-advanced", loaded.CampaignProcedure.Key);
        Assert.True(loadedRuntime.Navigation.UsesPersistentVeer);
        Assert.Equal(expedition.CampaignProcedure, loaded.CampaignProcedure);
    }

    [Fact]
    public async Task PartiallyCompletedWatchSurvivesReload()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var created = await StartCellAsync(
            database, service, world.World.Id, "Paused watch",
            "simple-fixed-distance", new HexCoordinate(0, 0));
        var original = Assert.IsType<CellExpeditionState>(created.Runtime);
        var heading = SpatialCourseSelection.FromLegacyHexDirection(
            world.World.SpatialTiling, new HexCoordinate(0, 0), new HexDirection(0));
        var plan = new CellWatchTravelPlan(
            heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None);
        // A paused watch can have elapsed time without spending any physical
        // distance. Never invent a partial geometric position from V1 progress.
        var partiallyElapsed = original with
        {
            IntendedHeading = heading,
            ElapsedTravelTime = TimeSpan.FromHours(2),
            ActiveWatch = new CellActiveWatchState(
                1, TimeSpan.FromHours(4), TimeSpan.FromHours(2),
                plan, ResolvedEncounter.None, true, RuntimePauseReason.ConditionsReviewRequired)
        };
        partiallyElapsed.Validate(world.World.SpatialTiling);
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        var stored = await store.SaveExpeditionAsync(
            created with
            {
                Runtime = partiallyElapsed,
                PauseReason = RuntimePauseReason.ConditionsReviewRequired,
                RemainingWatchTime = TimeSpan.FromHours(2)
            }, created.Version);
        Assert.Equal(SaveOutcome.Saved, stored.Outcome);

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(created.Id, "alice");
        var actual = Assert.IsType<CellExpeditionState>(loaded.Runtime);
        Assert.NotNull(actual.ActiveWatch);
        Assert.Equal(TimeSpan.FromHours(2), actual.ActiveWatch!.Elapsed);
        Assert.Equal(TimeSpan.FromHours(2), loaded.RemainingWatchTime);
        Assert.Equal(partiallyElapsed.Traversal, actual.Traversal);
        Assert.Equal(0, actual.DistanceTraveled!.Value.Value);
    }

    [Fact]
    public async Task RuntimeHistorySurvivesReloadWithoutDuplication()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var start = new HexCoordinate(0, 0);
        var expedition = await StartCellAsync(
            database, service, world.World.Id, "History", "simple-fixed-distance", start);
        var heading = SpatialCourseSelection.FromLegacyHexDirection(
            world.World.SpatialTiling, start, new HexDirection(0));
        var plan = new CellWatchTravelPlan(
            heading, false, true, TravelModeSelection.Normal, NavigationAidSelection.None);
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        var inputs = new CellWatchAdvanceInputs(
            ResolvedTravelAmount.Distance(
                new DistanceMeasure(1, DistanceUnit.Miles),
                new DistanceMeasure(1, DistanceUnit.Miles), source),
            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, source),
            ResolvedEncounter.None);
        expedition = await service.AdvanceCellExpeditionAsync(
            expedition.Id, "alice", expedition.Version, plan, inputs);
        expedition = await service.AdvanceCellExpeditionAsync(
            expedition.Id, "alice", expedition.Version, plan, inputs);

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.Id, "alice");
        var cell = Assert.IsType<CellExpeditionState>(loaded.Runtime);
        Assert.Equal(2, cell.CompletedWatches);
        Assert.NotEmpty(cell.History);
        Assert.All(cell.History, item => Assert.Null(item.Hex));
        Assert.Equal(cell.History.Count, cell.History.Select(item => item.Sequence).Distinct().Count());
    }

    [Fact]
    public async Task PlayerKnowledgeSurvivesReloadAndRemainsSubjectSpecific()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        world = await service.CreateLocationAsync(world.World.Id, "alice", new CreateLocationCommand(
            "Hidden ruin", "ruin", new WorldPoint(.1, .1), LocationDiscoverability.Hidden, world.Version));
        var location = Assert.Single(world.World.Locations);
        world = await service.CreateFeatureAsync(world.World.Id, "alice", new CreateFeatureCommand(
            "Spring", "spring", SpatialFeatureKind.Point, new WorldPoint(.2, .2), null, null, world.Version));
        var feature = Assert.Single(world.World.Features);
        var expedition = await StartCellAsync(
            database, service, world.World.Id, "Knowledge", "simple-fixed-distance", new HexCoordinate(0, 0));
        expedition = await service.DiscoverAsync(expedition.Id, "alice", new DiscoverSubjectCommand(
            expedition.Version, location.Id, KnowledgeSubjectType.Location, "test"));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.Id, "alice");
        Assert.Equal(KnowledgeState.Discovered, loaded.RequireKnowledge().Entries[location.Id].State);
        Assert.False(loaded.RequireKnowledge().Entries.ContainsKey(feature.Id));
        Assert.Single(loaded.RequireKnowledge().Entries);
    }

    [Fact]
    public async Task ReloadedExpeditionProducesDeterministicNextTransition()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var start = new HexCoordinate(0, 0);
        var expedition = await StartCellAsync(
            database, service, world.World.Id, "Replay", "simple-fixed-distance", start);
        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.Id, "alice");
        var loadedWorld = await restarted.GetOverworldAsync(world.World.Id, "alice");
        var cell = Assert.IsType<CellExpeditionState>(loaded.Runtime);
        var heading = SpatialCourseSelection.FromLegacyHexDirection(
            loadedWorld.World.SpatialTiling, start, new HexDirection(0));
        var plan = new CellWatchTravelPlan(
            heading, false, true, TravelModeSelection.Normal, NavigationAidSelection.None);
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        var inputs = new CellWatchAdvanceInputs(
            ResolvedTravelAmount.Distance(
                new DistanceMeasure(3, DistanceUnit.Miles),
                new DistanceMeasure(3, DistanceUnit.Miles), source),
            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, source),
            ResolvedEncounter.None);
        var engine = new CrawlRuntimeEngine();
        var first = engine.AdvanceCellWatch(
            loadedWorld.World.SpatialTiling, loaded.CampaignProcedure, cell, plan, inputs);
        var second = engine.AdvanceCellWatch(
            loadedWorld.World.SpatialTiling, loaded.CampaignProcedure, cell, plan, inputs);
        var sharedEmptyHistory = Array.Empty<CrawlRuntimeEvent>();
        Assert.Equal(
            first.Expedition with { History = sharedEmptyHistory },
            second.Expedition with { History = sharedEmptyHistory });
        Assert.True(first.Expedition.History.SequenceEqual(second.Expedition.History));
        Assert.True(first.Events.SequenceEqual(second.Events));
    }

    [Fact]
    public async Task ActiveExpeditionProtectsGridAndStableSemanticReferences()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        world = await service.CreateLocationAsync(world.World.Id, "alice", new CreateLocationCommand(
            "Keep", "site", new WorldPoint(0, 0), LocationDiscoverability.Obvious, world.Version));
        var location = Assert.Single(world.World.Locations);
        world = await service.CreateFeatureAsync(world.World.Id, "alice", new CreateFeatureCommand(
            "Keep feature", "road", SpatialFeatureKind.Line, null, [new(0, 0), new(1, 1)], null, world.Version));
        var feature = Assert.Single(world.World.Features);
        _ = await StartCellAsync(
            database, service, world.World.Id, "Active", "simple-fixed-distance", new HexCoordinate(0, 0));

        await Assert.ThrowsAsync<HexCrawlConflictException>(() => service.DeleteLocationAsync(
            world.World.Id, location.Id, "alice", world.Version));
        await Assert.ThrowsAsync<HexCrawlConflictException>(() => service.DeleteFeatureAsync(
            world.World.Id, feature.Id, "alice", world.Version));
        await Assert.ThrowsAsync<HexCrawlConflictException>(() => service.UpdateOverworldAsync(
            world.World.Id,
            "alice",
            new UpdateOverworldCommand(world.World.Name, world.World.Grid with { Origin = new WorldPoint(5, 5) }, world.Version)));
    }

    [Fact]
    public async Task EmptySchemaMigrationIsIdempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        await store.InitializeAsync();
        Assert.Empty(await store.ListOverworldsAsync("nobody"));
    }

    private static CreateOverworldCommand WorldCommand(string name = "World") => new(
        name,
        HexOrientation.PointyTop,
        new WorldPoint(0, 0),
        0,
        1,
        12,
        DistanceUnit.Miles);

    private static Task<StoredExpedition> StartCellAsync(
        TestDatabase database, HexCrawlService core, Guid worldId,
        string name, string presetKey, HexCoordinate start)
    {
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        return new ExpeditionStartService(store, core, new CampaignProcedureService(store))
            .StartWorldBoundCellsAsync(
                worldId, "alice", name,
                new ProcedureStartSelection(PresetKey: presetKey),
                LegacyHexTilingCompatibility.ToAddress(start));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        public string ConnectionString => _database.ConnectionString;

        private TestDatabase(PostgresTestDatabase database)
        {
            _database = database;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var store = new PostgresHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return new TestDatabase(database);
        }

        public async Task<HexCrawlService> ServiceAsync()
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            return new HexCrawlService(store);
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
