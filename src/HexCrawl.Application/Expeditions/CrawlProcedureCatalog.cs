using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public static class CrawlProcedureCatalog
{
    public static IReadOnlyList<CrawlProcedureProfile> All { get; } =
    [
        CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
        CrawlProcedureProfile.SimplifiedFixedDistance(),
        CrawlProcedureProfile.SimplifiedHexStep()
    ];

    public static CrawlProcedureProfile Resolve(string? key)
    {
        var resolved = string.IsNullOrWhiteSpace(key)
            ? All[0]
            : All.FirstOrDefault(item => string.Equals(item.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        return resolved ?? throw new ArgumentException($"Unknown crawl procedure profile '{key}'.", nameof(key));
    }
}
