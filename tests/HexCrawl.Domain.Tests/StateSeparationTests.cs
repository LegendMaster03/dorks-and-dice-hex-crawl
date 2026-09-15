using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class StateSeparationTests
{
    [Fact]
    public void GeneratedRuntimeFeaturesDoNotBecomeBaseWorldTruth()
    {
        var world = CreateWorld();
        var generatedLair = new PointFeature(Guid.NewGuid(), "Generated lair", "lair", new WorldPoint(2, 1));
        var runtime = new WorldRuntimeState
        {
            Id = Guid.NewGuid(),
            OverworldId = world.Id,
            GeneratedFeatures = [generatedLair]
        };

        Assert.Empty(world.Features);
        Assert.Single(runtime.GeneratedFeatures);
    }

    [Fact]
    public void PlayerKnowledgeAndAnnotationsDoNotMutateWorldDefinition()
    {
        var world = CreateWorld();
        var hiddenSite = new Location(
            Guid.NewGuid(),
            "Hidden site",
            "site",
            new WorldPoint(0, 0),
            LocationDiscoverability.Hidden,
            []);
        world = world with { Locations = [hiddenSite] };

        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(),
            OverworldId = world.Id,
            Entries = new Dictionary<Guid, KnowledgeEntry>
            {
                [hiddenSite.Id] = new(hiddenSite.Id, KnowledgeSubjectType.Location, KnowledgeState.Discovered, DateTimeOffset.UtcNow, "exploration")
            },
            Annotations = [new PlayerAnnotation(Guid.NewGuid(), hiddenSite.Position, "Found tracks here", DateTimeOffset.UtcNow)]
        };

        Assert.Equal(LocationDiscoverability.Hidden, world.Locations.Single().Discoverability);
        Assert.Equal(KnowledgeState.Discovered, knowledge.Entries[hiddenSite.Id].State);
        Assert.Single(knowledge.Annotations);
    }

    private static OverworldDefinition CreateWorld() => new()
    {
        Id = Guid.NewGuid(),
        Name = "State separation",
        Grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            NeighborCenterDistance = new DistanceMeasure(6, DistanceUnit.Miles)
        }
    };
}
