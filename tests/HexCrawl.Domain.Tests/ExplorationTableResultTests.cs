using HexCrawl.Domain.Runtime;

namespace HexCrawl.Domain.Tests;

public sealed class ExplorationTableResultTests
{
    [Fact]
    public void ManualResultsRequireSourceKeysAndDistinctTables()
    {
        ExplorationTableResult.ValidateCollection(
        [
            new("region-type", "Rolling hills", "10", "DM provided source"),
            new("opposition", "No opposition", null, "DM provided source")
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            ExplorationTableResult.ValidateCollection(
            [
                new("site", "Ruins"),
                new("SITE", "Second site")
            ]));
        Assert.Throws<InvalidOperationException>(() =>
            new ExplorationTableResult(" ", "Ruins").Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new ExplorationTableResult("site", " ").Validate());
    }

    [Fact]
    public void RegionKeysAndSourceAttributionAreValidatedWithoutGeometryAssumptions()
    {
        var valid = new ExplorationRegionRecord
        {
            Key = "zone-1", Name = "North district",
            LastOperationId = Guid.NewGuid(),
            TableResults = [new("terrain", "Open ground", "5", "Manual source")]
        };
        valid.Validate();
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { Key = " untrimmed" }).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            (valid with { LastOperationId = Guid.Empty }).Validate());
    }
}
