using HexCrawl.Web.Modules.Expeditions;
using HexCrawl.Web.Modules.ReferenceData;
using HexCrawl.Web.Modules.SourceMaps;
using HexCrawl.Web.Modules.Worlds;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Framework;

/// <summary>
/// Composition root for installed Hex Crawl capabilities. Adding a feature module should
/// normally require one catalog entry rather than edits throughout Program.cs.
/// </summary>
public static class HexCrawlModuleCatalog
{
    private static readonly IReadOnlyList<IHexCrawlModule> Installed = Validate(
    [
        new WorldModule(),
        new ExpeditionModule(),
        new SourceMapModule(),
        new ReferenceDataModule()
    ]);

    public static IReadOnlyList<HexCrawlModuleManifest> All { get; } =
        Installed.Select(module => module.Manifest).ToArray();

    public static void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var module in Installed)
        {
            module.RegisterServices(services);
        }
    }

    public static void MapEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.MapGroup("/api");
        foreach (var module in Installed)
        {
            module.MapEndpoints(api);
        }
    }

    private static IReadOnlyList<IHexCrawlModule> Validate(IReadOnlyList<IHexCrawlModule> modules)
    {
        var byId = new Dictionary<string, HexCrawlModuleManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            var manifest = module.Manifest
                ?? throw new InvalidOperationException("A Hex Crawl module returned no manifest.");

            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                throw new InvalidOperationException("Hex Crawl module IDs can not be blank.");
            }

            if (string.IsNullOrWhiteSpace(manifest.DisplayName))
            {
                throw new InvalidOperationException($"Hex Crawl module '{manifest.Id}' has no display name.");
            }

            if (!byId.TryAdd(manifest.Id, manifest))
            {
                throw new InvalidOperationException($"Duplicate Hex Crawl module ID '{manifest.Id}'.");
            }
        }

        foreach (var manifest in byId.Values)
        {
            foreach (var dependency in manifest.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency))
                {
                    throw new InvalidOperationException(
                        $"Hex Crawl module '{manifest.Id}' contains a blank dependency ID.");
                }

                if (!byId.ContainsKey(dependency))
                {
                    throw new InvalidOperationException(
                        $"Hex Crawl module '{manifest.Id}' requires missing module '{dependency}'.");
                }

                if (string.Equals(manifest.Id, dependency, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Hex Crawl module '{manifest.Id}' can not depend on itself.");
                }
            }
        }

        return modules.ToArray();
    }
}
