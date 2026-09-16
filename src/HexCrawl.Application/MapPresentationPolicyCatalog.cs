using HexCrawl.Domain.Presentation;

namespace HexCrawl.Application;

public static class MapPresentationPolicyCatalog
{
    public static IReadOnlyList<MapPresentationPolicy> All { get; } =
    [
        MapPresentationPolicy.TraditionalHiddenHexcrawl(),
        MapPresentationPolicy.ExplorationMap(),
        MapPresentationPolicy.OpenRegionalMap(),
        MapPresentationPolicy.DmControlled()
    ];

    public static MapPresentationPolicy Resolve(string? key)
    {
        var resolved = string.IsNullOrWhiteSpace(key)
            ? All[1]
            : All.FirstOrDefault(item => string.Equals(item.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        return resolved ?? throw new ArgumentException($"Unknown map presentation policy '{key}'.", nameof(key));
    }
}
