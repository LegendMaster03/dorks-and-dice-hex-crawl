using System.Text.Json;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class PlayerMapProjectionTests
{
    private static OverworldDefinition World()
    {
        var publicFeature = new PointFeature(Guid.NewGuid(), "Public road", "road", new WorldPoint(1, 2));
        var secretFeature = new PointFeature(Guid.NewGuid(), "Secret lair", "lair", new WorldPoint(3, 4));
        var publicLocation = new Location(Guid.NewGuid(), "Town", "settlement",
            new WorldPoint(1, 1), LocationDiscoverability.Obvious, []);
        var secretLocation = new Location(Guid.NewGuid(), "Hidden fortress", "fortress",
            new WorldPoint(2, 2), LocationDiscoverability.Hidden, []);
        return new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "World",
            Features = [publicFeature, secretFeature],
            Locations = [publicLocation, secretLocation],
            MapAppearance = new MapAppearance
            {
                FeatureColors = new Dictionary<string, string> { ["road"] = "#AABBCC", ["lair"] = "#112233" },
                LabelColors = new Dictionary<string, string> { ["settlement"] = "#FFFFFF", ["fortress"] = "#123456" }
            }
        };
    }

    [Fact]
    public void NoObjectsOrLabelsAreReleasedByDefault()
    {
        var source = World();
        var projection = PlayerMapProjection.Create(source);
        Assert.Empty(projection.World.Features);
        Assert.Empty(projection.World.Locations);
        Assert.Empty(projection.World.SourceMaps);
        Assert.Empty(projection.World.EnvironmentAnnotations);
        Assert.Empty(projection.FeatureLabelIds);
        Assert.Empty(projection.LocationLabelIds);
        Assert.Empty(projection.World.MapAppearance.FeatureColors);
        Assert.Empty(projection.World.MapAppearance.LabelColors);
    }

    [Fact]
    public void ExplicitReleaseDoesNotTransmitHiddenNamesOrGeometry()
    {
        var source = World();
        var feature = source.Features[0];
        var location = source.Locations[0];
        source = source with
        {
            PlayerMapDisclosure = new PlayerMapDisclosure
            {
                VisibleFeatureIds = [feature.Id],
                VisibleLocationIds = [location.Id],
                VisibleLocationLabelIds = [location.Id]
            }
        };
        var projected = PlayerMapProjection.Create(source);
        Assert.Single(projected.World.Features);
        Assert.Single(projected.World.Locations);
        Assert.Empty(projected.FeatureLabelIds);
        Assert.Equal(location.Id, Assert.Single(projected.LocationLabelIds));
        Assert.Empty(projected.World.PlayerMapDisclosure.VisibleFeatureIds);
        Assert.Empty(projected.World.MapAppearance.FeatureColors);
        Assert.Single(projected.World.MapAppearance.LabelColors);
        var json = JsonSerializer.Serialize(new
        {
            projected.World.Features,
            projected.World.Locations,
            projected.World.MapAppearance
        });
        Assert.DoesNotContain("Public road", json);
        Assert.DoesNotContain("road", json);
        Assert.DoesNotContain("Secret lair", json);
        Assert.DoesNotContain("Hidden fortress", json);
        Assert.DoesNotContain("fortress", json);
        Assert.DoesNotContain("lair", json);
    }

    [Fact]
    public void LabelMayNotBeReleasedWithoutGeometry()
    {
        var source = World();
        source = source with
        {
            PlayerMapDisclosure = new PlayerMapDisclosure
            {
                VisibleFeatureLabelIds = [source.Features[0].Id]
            }
        };
        Assert.Throws<ArgumentException>(() => PlayerMapProjection.Create(source));
    }

    [Fact]
    public void OnlyExplicitlyReleasedPlayerRoleImagesCanAppear()
    {
        var source = World();
        var gm = new SourceMapRepresentation(Guid.NewGuid(), "gm", "Secret GM map",
            SourceMapRole.Gm, "secret-asset", false, null, []);
        var player = new SourceMapRepresentation(Guid.NewGuid(), "player", "Player map",
            SourceMapRole.Player, "player-asset", false, null, []);
        source = source with { SourceMaps = [gm, player] };
        Assert.Empty(PlayerMapProjection.Create(source).World.SourceMaps);

        var invalid = source with
        {
            PlayerMapDisclosure = new PlayerMapDisclosure
            {
                VisibleSourceMapIds = [gm.Id]
            }
        };
        Assert.Throws<ArgumentException>(() => PlayerMapProjection.Create(invalid));

        var approved = source with
        {
            PlayerMapDisclosure = new PlayerMapDisclosure
            {
                VisibleSourceMapIds = [player.Id]
            }
        };
        var visible = PlayerMapProjection.Create(approved).World.SourceMaps;
        Assert.Equal(player.Id, Assert.Single(visible).Id);
        Assert.DoesNotContain("secret-asset", JsonSerializer.Serialize(visible));
    }

    [Fact]
    public void UnknownIdentifiersAreRejected()
    {
        var source = World();
        source = source with
        {
            PlayerMapDisclosure = new PlayerMapDisclosure
            {
                VisibleLocationIds = [Guid.NewGuid()]
            }
        };
        Assert.Throws<ArgumentException>(() => PlayerMapProjection.Create(source));
    }
}
