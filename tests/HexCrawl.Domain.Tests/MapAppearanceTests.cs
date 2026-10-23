using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class MapAppearanceTests
{
    [Fact]
    public void NormalizesCategoryKeysAndColorValues()
    {
        var appearance = new MapAppearance
        {
            TerrainColors = new Dictionary<string, string> { [" Forest "] = "#a0c099" },
            FeatureColors = new Dictionary<string, string> { ["River"] = "#123abc" },
            LabelColors = new Dictionary<string, string> { ["Town"] = "#0a1b2c" }
        }.NormalizeAndValidate();

        Assert.Equal("#A0C099", appearance.TerrainColors["forest"]);
        Assert.Equal("#123ABC", appearance.FeatureColors["river"]);
        Assert.Equal("#0A1B2C", appearance.LabelColors["town"]);
        Assert.Single(appearance.TerrainColors);
    }

    [Theory]
    [InlineData("forest", "red")]
    [InlineData("forest", "#ABC")]
    [InlineData("forest", "#FFFFFF;")]
    [InlineData("bad?", "#123456")]
    public void RejectsInvalidCategoryColors(string category, string color)
    {
        var appearance = new MapAppearance
        {
            TerrainColors = new Dictionary<string, string> { [category] = color }
        };
        Assert.Throws<ArgumentException>(() => appearance.NormalizeAndValidate());
    }

    [Fact]
    public void RejectsDuplicateCategoriesAfterCaseFolding()
    {
        var appearance = new MapAppearance
        {
            TerrainColors = new Dictionary<string, string>
            {
                ["forest"] = "#123456",
                ["Forest"] = "#654321"
            }
        };
        Assert.Throws<ArgumentException>(() => appearance.NormalizeAndValidate());
    }

    [Fact]
    public void SavedAppearanceSurvivesWorldJsonRoundTrip()
    {
        var source = new MapAppearance
        {
            TerrainColors = new Dictionary<string, string> { ["water"] = "#438BB5" }
        }.NormalizeAndValidate();

        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var restored = System.Text.Json.JsonSerializer.Deserialize<MapAppearance>(json);
        Assert.NotNull(restored);
        Assert.Equal("#438BB5", restored.TerrainColors["water"]);
    }
}
