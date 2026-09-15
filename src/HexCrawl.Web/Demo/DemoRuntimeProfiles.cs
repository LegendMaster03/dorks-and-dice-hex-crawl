using HexCrawl.Domain.Procedure;

namespace HexCrawl.Web.Demo;

public static class DemoRuntimeProfiles
{
    public static IReadOnlyList<CrawlProcedureProfile> All { get; } =
    [
        CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
        CrawlProcedureProfile.SimplifiedFixedDistance(),
        CrawlProcedureProfile.SimplifiedHexStep()
    ];

    public static CrawlProcedureProfile Resolve(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "alexandrian-advanced" : key.Trim();
        return All.FirstOrDefault(profile => string.Equals(profile.Key, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown crawl procedure profile '{normalized}'.");
    }
}
