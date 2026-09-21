using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Framework;

/// <summary>
/// Stable metadata for a Hex Crawl feature module. Module identity is independent from
/// its current routes, UI shape, or hosting mechanism.
/// </summary>
public sealed record HexCrawlModuleManifest(
    string Id,
    string DisplayName)
{
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}

/// <summary>
/// A cohesive Hex Crawl capability. Modules register their application services and map
/// their API surface while the framework remains responsible for shared hosting concerns.
/// </summary>
public interface IHexCrawlModule
{
    HexCrawlModuleManifest Manifest { get; }

    void RegisterServices(IServiceCollection services);

    void MapEndpoints(RouteGroupBuilder api);
}
