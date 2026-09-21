using HexCrawl.Application;
using HexCrawl.Web.Api;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.SourceMaps;

public sealed class SourceMapModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "source-maps",
        DisplayName: "Source maps and importers")
    {
        Dependencies = ["worlds"]
    };

    public void RegisterServices(IServiceCollection services) =>
        services.AddScoped<SourceMapApplicationService>();

    public void MapEndpoints(RouteGroupBuilder api) =>
        SourceMapApiEndpoints.Map(api.MapGroup("/overworlds/{overworldId:guid}/source-maps"));
}
