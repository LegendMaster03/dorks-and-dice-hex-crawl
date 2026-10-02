using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase9WorldEnvironmentIntegrityTests
{
    [Fact]
    public async Task FeatureReferencedByStaticEnvironmentCanNotBeDeleted()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new HexCrawlService(store);

        var world = await service.CreateOverworldAsync(
            "owner",
            new CreateOverworldCommand(
                "Environment reference world",
                HexOrientation.PointyTop,
                new WorldPoint(0, 0),
                0,
                1,
                12,
                DistanceUnit.Miles));
        world = await service.CreateFeatureAsync(
            world.World.Id,
            "owner",
            new CreateFeatureCommand(
                "Forest",
                "forest",
                SpatialFeatureKind.Region,
                null,
                null,
                [new WorldPoint(-1, -1), new WorldPoint(1, -1), new WorldPoint(0, 1)],
                world.Version));
        var feature = Assert.Single(world.World.Features);
        var annotation = new EnvironmentAnnotation
        {
            Id = Guid.NewGuid(),
            Scope = new EnvironmentAnnotationScope
            {
                Kind = EnvironmentAnnotationScopeKind.SpatialFeature,
                FeatureId = feature.Id
            },
            Facts =
            [
                new EnvironmentFact
                {
                    Id = Guid.NewGuid(),
                    Dimension = EnvironmentDimensions.Terrain,
                    ValueKind = EnvironmentValueKind.Tag,
                    Tag = "forest"
                }
            ]
        };
        world = await service.ReplaceWorldEnvironmentAsync(
            world.World.Id,
            "owner",
            new ReplaceWorldEnvironmentCommand(world.Version, [annotation]));

        var exception = await Assert.ThrowsAsync<HexCrawlConflictException>(() =>
            service.DeleteFeatureAsync(world.World.Id, feature.Id, "owner", world.Version));

        Assert.Contains("environment annotations", exception.Message, StringComparison.OrdinalIgnoreCase);
        var reloaded = await service.GetOverworldAsync(world.World.Id, "owner");
        Assert.Equal(world.Version, reloaded.Version);
        Assert.Equal(feature.Id, Assert.Single(reloaded.World.Features).Id);
        Assert.Equal(annotation.Id, Assert.Single(reloaded.World.EnvironmentAnnotations).Id);
        reloaded.World.ValidateEnvironmentAnnotations();
    }
}
