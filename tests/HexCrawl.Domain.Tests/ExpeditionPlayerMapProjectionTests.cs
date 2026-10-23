using System.Text.Json;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class ExpeditionPlayerMapProjectionTests
{
    private static (OverworldDefinition World, WorldCellId First, WorldCellId Second,
        Guid PublicFeature, Guid SecretFeature, Guid PublicLocation, Guid SecretLocation) Fixture()
    {
        var generated = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1, "world-unit");
        Assert.Equal("realized", generated.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), generated.Topology!, generated.Realization!, new WorldPoint(0, 0));
        var motif = tiling.Topology.MotifCells[0].Id;
        var first = tiling.Resolve(new PeriodicCellAddress(motif, new LatticeDisplacement(0, 0)));
        var second = tiling.Resolve(new PeriodicCellAddress(motif, new LatticeDisplacement(1, 0)));
        var publicFeature = new PointFeature(Guid.NewGuid(), "Known road", "road", first.Center);
        var secretFeature = new PointFeature(Guid.NewGuid(), "Hidden shrine", "shrine", second.Center);
        var publicLocation = new Location(Guid.NewGuid(), "Known town", "town",
            first.Center, LocationDiscoverability.Obvious, []);
        var secretLocation = new Location(Guid.NewGuid(), "Secret fortress", "fortress",
            second.Center, LocationDiscoverability.Hidden, []);

        EnvironmentAnnotation Terrain(WorldCellId cell, string tag) => new()
        {
            Id = Guid.NewGuid(),
            Scope = new EnvironmentAnnotationScope
            {
                Kind = EnvironmentAnnotationScopeKind.Cell,
                Cell = cell
            },
            Facts =
            [
                new EnvironmentFact
                {
                    Id = Guid.NewGuid(), Dimension = EnvironmentDimensions.Terrain,
                    ValueKind = EnvironmentValueKind.Tag, Tag = tag,
                    Note = "GM secret", Provenance = "GM-only source"
                },
                new EnvironmentFact
                {
                    Id = Guid.NewGuid(), Dimension = EnvironmentDimensions.Hazard,
                    ValueKind = EnvironmentValueKind.Tag, Tag = "deadly trap"
                }
            ]
        };
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Knowledge projection",
            Tiling = tiling,
            Features = [publicFeature, secretFeature],
            Locations = [publicLocation, secretLocation],
            EnvironmentAnnotations = [Terrain(first.Id, "forest"), Terrain(second.Id, "swamp")],
            MapAppearance = new MapAppearance
            {
                TerrainColors = new Dictionary<string, string>
                {
                    ["forest"] = "#123456", ["swamp"] = "#ABCDEF"
                }
            }
        };
        return (world, first.Id, second.Id, publicFeature.Id, secretFeature.Id,
            publicLocation.Id, secretLocation.Id);
    }

    [Fact]
    public void DiscoveryAndEnteredCellsRevealOnlyLearnedSubjectsAndTerrain()
    {
        var setup = Fixture();
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(), OverworldId = setup.World.Id,
            PresentationPolicy = MapPresentationPolicy.ExplorationMap(),
            KnownCells = [setup.First],
            Entries = new Dictionary<Guid, KnowledgeEntry>
            {
                [setup.PublicFeature] = new(setup.PublicFeature, KnowledgeSubjectType.Feature,
                    KnowledgeState.Discovered, DateTimeOffset.UtcNow, "test"),
                [setup.PublicLocation] = new(setup.PublicLocation, KnowledgeSubjectType.Location,
                    KnowledgeState.Observed, DateTimeOffset.UtcNow, "test")
            }
        };
        var result = ExpeditionPlayerMapProjection.Create(setup.World, knowledge);
        Assert.Equal(setup.PublicFeature, Assert.Single(result.World.Features).Id);
        Assert.Equal(setup.PublicLocation, Assert.Single(result.World.Locations).Id);
        Assert.Equal(setup.PublicFeature, Assert.Single(result.FeatureLabels));
        Assert.Equal(setup.PublicLocation, Assert.Single(result.LocationLabels));
        Assert.Equal(setup.First, Assert.Single(result.KnownCells));
        Assert.True(result.ShowGrid);
        var terrain = Assert.Single(result.World.EnvironmentAnnotations);
        Assert.Equal(setup.First, terrain.Scope.Cell);
        var fact = Assert.Single(terrain.Facts);
        Assert.Equal("forest", fact.Tag);
        Assert.Null(fact.Note);
        Assert.Null(fact.Provenance);
        Assert.DoesNotContain("Hidden shrine", JsonSerializer.Serialize(result.World.Features));
        Assert.DoesNotContain("Secret fortress", JsonSerializer.Serialize(result.World.Locations));
        Assert.DoesNotContain("swamp", JsonSerializer.Serialize(result.World.MapAppearance));
    }

    [Fact]
    public void DmControlledDoesNotRevealWithoutExplicitKnowledgeOrRelease()
    {
        var setup = Fixture();
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(), OverworldId = setup.World.Id,
            PresentationPolicy = MapPresentationPolicy.DmControlled()
        };
        var blank = ExpeditionPlayerMapProjection.Create(setup.World, knowledge);
        Assert.Empty(blank.World.Features);
        Assert.Empty(blank.World.Locations);
        Assert.Empty(blank.World.EnvironmentAnnotations);
        Assert.False(blank.ShowGrid);
        var released = setup.World with
        {
            PlayerMapDisclosure = new PlayerMapDisclosure
            {
                VisibleFeatureIds = [setup.SecretFeature],
                VisibleFeatureLabelIds = [],
                VisibleLocationIds = [setup.SecretLocation],
                ShowTerrain = true
            }
        };
        var result = ExpeditionPlayerMapProjection.Create(released, knowledge);
        Assert.Equal("Unlabeled", Assert.Single(result.World.Features).Name);
        Assert.Equal("Unlabeled", Assert.Single(result.World.Locations).Name);
        Assert.Empty(result.FeatureLabels);
        Assert.Equal(2, result.World.EnvironmentAnnotations.Count);
        Assert.False(result.ShowGrid);
    }

    [Fact]
    public void PlayerKnowledgeMustBelongToWorld()
    {
        var setup = Fixture();
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(), OverworldId = Guid.NewGuid()
        };
        Assert.Throws<ArgumentException>(() =>
            ExpeditionPlayerMapProjection.Create(setup.World, knowledge));
    }

    [Fact]
    public void AlwaysVisibleTerrainPreservesCellContentsButNotOtherFacts()
    {
        var setup = Fixture();
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(), OverworldId = setup.World.Id,
            PresentationPolicy = MapPresentationPolicy.OpenRegionalMap()
        };
        var result = ExpeditionPlayerMapProjection.Create(setup.World, knowledge);
        Assert.Equal(2, result.World.EnvironmentAnnotations.Count);
        Assert.All(result.World.EnvironmentAnnotations, a => Assert.Single(a.Facts));
        Assert.Empty(result.World.Features);
        Assert.Empty(result.World.Locations);
    }
}
