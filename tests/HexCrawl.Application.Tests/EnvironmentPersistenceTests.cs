using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class EnvironmentPersistenceTests
{
    [Fact]
    public async Task StaticWorldAndExpeditionEnvironmentRoundTripAcrossStoreRestart()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var firstStore = new PostgresHexCrawlStore(database.ConnectionString);
        await firstStore.InitializeAsync();
        var world = World();
        var annotation = new EnvironmentAnnotation
        {
            Id = Guid.NewGuid(),
            Scope = new EnvironmentAnnotationScope
            {
                Kind = EnvironmentAnnotationScopeKind.Hex,
                Hex = new HexCoordinate(0, 0)
            },
            Facts = [Tag("terrain", "forest", "authored world truth")]
        };
        var now = DateTimeOffset.UtcNow;
        await firstStore.CreateOverworldAsync(new StoredOverworld(
            world with { EnvironmentAnnotations = [annotation] }, "owner", 1, now, now));
        var expedition = Expedition(world.Id) with
        {
            Environment = new ExpeditionEnvironmentState
            {
                CurrentFacts = [Tag("weather", "rain", "current session")],
                Overrides = [Tag("visibility", "darkness", "DM override")]
            }
        };
        await firstStore.CreateExpeditionAsync(expedition);

        var restartedStore = new PostgresHexCrawlStore(database.ConnectionString);
        await restartedStore.InitializeAsync();
        var loadedWorld = await restartedStore.GetOverworldAsync(world.Id, "owner");
        var loadedExpedition = await restartedStore.GetExpeditionAsync(expedition.Id, "owner");

        Assert.NotNull(loadedWorld);
        Assert.NotNull(loadedExpedition);
        Assert.Equal("forest", Assert.Single(Assert.Single(loadedWorld!.World.EnvironmentAnnotations).Facts).Tag);
        Assert.Equal("rain", Assert.Single(loadedExpedition!.Environment.CurrentFacts).Tag);
        Assert.Equal("darkness", Assert.Single(loadedExpedition.Environment.Overrides).Tag);
        Assert.Equal("authored world truth", loadedWorld.World.EnvironmentAnnotations[0].Facts[0].Provenance);
        Assert.Equal("current session", loadedExpedition.Environment.CurrentFacts[0].Provenance);
        Assert.Equal("DM override", loadedExpedition.Environment.Overrides[0].Provenance);
    }

    [Fact]
    public async Task EnvironmentMutationsUseAggregateOptimisticConcurrency()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new HexCrawlService(store);
        var world = World();
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));
        var expedition = Expedition(world.Id);
        await store.CreateExpeditionAsync(expedition);

        var updatedWorld = await service.ReplaceWorldEnvironmentAsync(
            world.Id,
            "owner",
            new ReplaceWorldEnvironmentCommand(1,
            [
                new EnvironmentAnnotation
                {
                    Id = Guid.NewGuid(),
                    Scope = new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.World },
                    Facts = [Tag("weather", "clear")]
                }
            ]));
        var updatedExpedition = await service.UpdateExpeditionEnvironmentAsync(
            expedition.Id,
            "owner",
            new UpdateExpeditionEnvironmentCommand(1, [Tag("weather", "rain")], []));

        Assert.Equal(2, updatedWorld.Version);
        Assert.Equal(2, updatedExpedition.Version);
        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() => service.ReplaceWorldEnvironmentAsync(
            world.Id, "owner", new ReplaceWorldEnvironmentCommand(1, [])));
        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() => service.UpdateExpeditionEnvironmentAsync(
            expedition.Id, "owner", new UpdateExpeditionEnvironmentCommand(1, [], [])));
    }

    [Fact]
    public async Task InvalidFeatureEnvironmentReferenceIsRejectedBeforePersistence()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new HexCrawlService(store);
        var world = World();
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));

        var command = new ReplaceWorldEnvironmentCommand(1,
        [
            new EnvironmentAnnotation
            {
                Id = Guid.NewGuid(),
                Scope = new EnvironmentAnnotationScope
                {
                    Kind = EnvironmentAnnotationScopeKind.SpatialFeature,
                    FeatureId = Guid.NewGuid()
                },
                Facts = [Tag("route", "road")]
            }
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReplaceWorldEnvironmentAsync(world.Id, "owner", command));
        var loaded = await store.GetOverworldAsync(world.Id, "owner");
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Version);
        Assert.Empty(loaded.World.EnvironmentAnnotations);
    }

    private static EnvironmentFact Tag(string dimension, string value, string? provenance = null) => new()
    {
        Id = Guid.NewGuid(),
        Dimension = dimension,
        ValueKind = EnvironmentValueKind.Tag,
        Tag = value,
        Provenance = provenance
    };

    private static OverworldDefinition World() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Environment persistence world",
        Grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            Orientation = HexOrientation.PointyTop,
            Origin = new WorldPoint(0, 0),
            RotationDegrees = 0,
            HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        }
    };

    private static StoredExpedition Expedition(Guid worldId)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredExpedition(
            "Environment persistence expedition",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
            },
            new WorldBoundCrawlSessionContext(worldId),
            null,
            CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey).MaterializeGeneric().Procedure,
            null,
            TimeSpan.FromHours(1),
            "owner",
            1,
            now,
            now);
    }
}