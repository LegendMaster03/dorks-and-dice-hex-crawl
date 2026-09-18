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
        var expedition = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "Survey", "alexandrian-advanced", new HexCoordinate(2, -1)));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.Equal(new HexCoordinate(2, -1), loaded.State.CurrentHex);
        Assert.Equal("alexandrian-advanced", loaded.Procedure.Key);
        Assert.True(loaded.Procedure.UsesPersistentVeer);
        Assert.Equal(expedition.Procedure, loaded.Procedure);
    }

    [Fact]
    public async Task PartiallyCompletedWatchSurvivesReload()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "Survey", "simple-fixed-distance", new HexCoordinate(0, 0)));
        expedition = await service.AdvanceExpeditionAsync(expedition.State.Id, "alice", FixedAdvance(expedition.Version, 12, false));
        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, expedition.PauseReason);

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.NotNull(loaded.State.ActiveWatch);
        Assert.Equal(TimeSpan.FromHours(2), loaded.RemainingWatchTime);
        Assert.Equal(6, loaded.State.DistanceTraveled.Value);
        Assert.Equal(new HexCoordinate(1, 0), loaded.State.CurrentHex);
    }

    [Fact]
    public async Task LostAndVeerStateSurviveReload()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "Lost survey", "alexandrian-advanced", new HexCoordinate(0, 0)));
        expedition = await service.AdvanceExpeditionAsync(expedition.State.Id, "alice", new AdvanceExpeditionCommand
        {
            ExpectedVersion = expedition.Version,
            IntendedDirection = 0,
            ExpectedDistance = 2,
            ActualDistance = 2,
            ResolutionSource = ResolutionSource.ManualRoll,
            NavigationOutcome = NavigationCheckOutcome.Failed,
            VeerSteps = 1,
            EncounterOutcome = EncounterOutcomeKind.None
        });

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.True(loaded.State.Navigation.IsLost);
        Assert.Equal(1, loaded.State.Navigation.VeerSteps);
        Assert.Equal(new HexDirection(1), loaded.State.ActualDirection);
    }

    [Fact]
    public async Task RuntimeHistorySurvivesReloadWithoutDuplication()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "History", "simple-fixed-distance", new HexCoordinate(0, 0)));
        expedition = await service.AdvanceExpeditionAsync(expedition.State.Id, "alice", FixedAdvance(expedition.Version, 1, true));
        expedition = await service.AdvanceExpeditionAsync(expedition.State.Id, "alice", FixedAdvance(expedition.Version, 1, true));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.NotEmpty(loaded.State.History);
        Assert.Equal(loaded.State.History.Count, loaded.State.History.Select(item => item.Sequence).Distinct().Count());
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
        var expedition = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "Knowledge", "simple-fixed-distance", new HexCoordinate(0, 0)));
        expedition = await service.DiscoverAsync(expedition.State.Id, "alice", new DiscoverSubjectCommand(
            expedition.Version, location.Id, KnowledgeSubjectType.Location, "test"));

        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.State.Id, "alice");
        Assert.Equal(KnowledgeState.Discovered, loaded.Knowledge.Entries[location.Id].State);
        Assert.False(loaded.Knowledge.Entries.ContainsKey(feature.Id));
        Assert.Single(loaded.Knowledge.Entries);
    }

    [Fact]
    public async Task ReloadedExpeditionProducesDeterministicNextTransition()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = await database.ServiceAsync();
        var world = await service.CreateOverworldAsync("alice", WorldCommand());
        var expedition = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "Replay", "simple-fixed-distance", new HexCoordinate(0, 0)));
        var restarted = await database.ServiceAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.State.Id, "alice");
        var loadedWorld = await restarted.GetOverworldAsync(world.World.Id, "alice");
        var engine = new CrawlRuntimeEngine();
        var plan = new WatchTravelPlan(new HexDirection(0), TravelModeSelection.Normal, NavigationAidSelection.None, false, true);
        var inputs = new WatchAdvanceInputs(TravelDistanceResolver.Fixed(new DistanceMeasure(3, DistanceUnit.Miles)));

        var context = ExpeditionWorldComposition.RuntimeContext(loadedWorld.World);
        var first = engine.Advance(context, loaded.Procedure, loaded.State, plan, inputs);
        var second = engine.Advance(context, loaded.Procedure, loaded.State, plan, inputs);
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
        _ = await service.StartExpeditionAsync(world.World.Id, "alice", new StartExpeditionCommand(
            "Active", "simple-fixed-distance", new HexCoordinate(0, 0)));

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
        var store = new SqliteHexCrawlStore(database.ConnectionString);
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

    private static AdvanceExpeditionCommand FixedAdvance(long version, double miles, bool continueAcrossBoundaries) => new()
    {
        ExpectedVersion = version,
        IntendedDirection = 0,
        ExpectedDistance = miles,
        ActualDistance = miles,
        ResolutionSource = ResolutionSource.ManualRoll,
        ContinueAcrossBoundaries = continueAcrossBoundaries
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _path;
        public string ConnectionString { get; }

        private TestDatabase(string path)
        {
            _path = path;
            ConnectionString = $"Data Source={path}";
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = new TestDatabase(Path.Combine(Path.GetTempPath(), $"hex-crawl-{Guid.NewGuid():N}.db"));
            var store = new SqliteHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return database;
        }

        public async Task<HexCrawlService> ServiceAsync()
        {
            var store = new SqliteHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            return new HexCrawlService(store);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = _path + suffix;
                if (File.Exists(file)) File.Delete(file);
            }
            return ValueTask.CompletedTask;
        }
    }
}
