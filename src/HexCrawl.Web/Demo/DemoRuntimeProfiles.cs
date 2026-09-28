using HexCrawl.Application;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Web.Demo;

public static class DemoRuntimeProfiles
{
    public static IReadOnlyList<CrawlProcedureProfile> All { get; } =
        CrawlProcedureCatalog.All.Select(preset => preset.Materialize()).ToArray();

    public static CrawlProcedureProfile Resolve(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "alexandrian-advanced" : key.Trim();
        var preset = CrawlProcedureCatalog.All.FirstOrDefault(item =>
  string.Equals(item.PresetKey, normalized, StringComparison.OrdinalIgnoreCase));
        return preset?.Materialize()
  ?? throw new InvalidOperationException($"Unknown crawl procedure profile '{normalized}'.");
    }
}
