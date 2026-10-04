using HexCrawl.Web.Framework;

namespace HexCrawl.IntegrationTests;

public sealed class ModuleArchitectureTests
{
    [Fact]
    public void InstalledModulesExposeStableUniqueFeatureIdentities()
    {
        var manifests = HexCrawlModuleCatalog.All;

        Assert.Equal(
            ["worlds", "expeditions", "environment-context", "survival-resources", "journey-processes", "travel-rules", "source-maps", "reference-data", "procedure-composer"],
            manifests.Select(manifest => manifest.Id).ToArray());

        Assert.Equal(
            manifests.Count,
            manifests.Select(manifest => manifest.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Equal(
            ["worlds"],
            manifests.Single(manifest => manifest.Id == "expeditions").Dependencies);

        Assert.Equal(
            ["worlds", "expeditions"],
            manifests.Single(manifest => manifest.Id == "environment-context").Dependencies);

        Assert.Equal(
            ["expeditions", "environment-context"],
            manifests.Single(manifest => manifest.Id == "survival-resources").Dependencies);

        Assert.Equal(
            ["expeditions", "environment-context", "survival-resources"],
            manifests.Single(manifest => manifest.Id == "journey-processes").Dependencies);

        Assert.Equal(
            ["expeditions"],
            manifests.Single(manifest => manifest.Id == "travel-rules").Dependencies);

        Assert.Equal(
            ["worlds"],
            manifests.Single(manifest => manifest.Id == "source-maps").Dependencies);
    }
}
