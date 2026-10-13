using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureAndPresentationCatalogTests
{
    [Fact]
    public void BuiltInProcedureAndPresentationPresetsAreValid()
    {
        Assert.NotEmpty(CrawlProcedureCatalog.All);
        foreach (var preset in CrawlProcedureCatalog.All)
        {
            preset.Validate();
            preset.MaterializeGeneric().Procedure.Validate();
        }

        Assert.Equal(4, MapPresentationPolicyCatalog.All.Count);
        foreach (var policy in MapPresentationPolicyCatalog.All)
            policy.Validate();

        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "traditional-hidden-hexcrawl");
        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "exploration-map");
        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "open-regional-map");
        Assert.Contains(MapPresentationPolicyCatalog.All, item => item.Key == "dm-controlled");
    }
}
