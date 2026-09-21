using HexCrawl.Application;
using HexCrawl.Web.Api;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.ReferenceData;

public sealed class ReferenceDataModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "reference-data",
        DisplayName: "Procedure and presentation catalogs");

    public void RegisterServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/runtime/profiles", () => Results.Ok(
            CrawlProcedureCatalog.All.Select(RuntimeProfileContract.From).ToArray()));

        api.MapGet("/presentation/presets", () => Results.Ok(
            MapPresentationPolicyCatalog.All.Select(PresentationProfileContract.From).ToArray()));
    }
}
