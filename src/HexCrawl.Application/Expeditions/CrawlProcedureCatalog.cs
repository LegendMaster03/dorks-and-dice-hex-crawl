using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public static class CrawlProcedureCatalog
{
    public const string BxPresetKey = "bx";
    public const string OseClassicPresetKey = "ose-classic-fantasy";
    public const string Adnd2ePresetKey = "adnd-2e";
    public const string Dnd35PresetKey = "dnd-3-5e";
    public const string Dnd2014PresetKey = "dnd-2014";
    public const string Dnd2024PresetKey = "dnd-2024";
    public const string Pathfinder2eHexplorationPresetKey = "pathfinder-2e-hexploration";
    public const string ForbiddenLandsPresetKey = "forbidden-lands";
    public const string WorldsWithoutNumberPresetKey = "worlds-without-number";
    public const string OneRing2ePresetKey = "the-one-ring-2e";

    private static IReadOnlyList<CrawlProcedurePresetDefinition> FullCatalog { get; } = BuildCatalog();

    public static IReadOnlyList<CrawlProcedurePresetDefinition> All => FullCatalog;
    public static IReadOnlyList<CrawlProcedurePresetDefinition> Catalog => FullCatalog;

    public static CrawlProcedurePresetDefinition Resolve(string? key)
    {
        var resolved = string.IsNullOrWhiteSpace(key)
            ? FullCatalog[0]
            : FullCatalog.FirstOrDefault(item => string.Equals(item.PresetKey, key.Trim(), StringComparison.OrdinalIgnoreCase));
        return resolved ?? throw new ArgumentException($"Unknown crawl procedure preset '{key}'.", nameof(key));
    }

    private static IReadOnlyList<CrawlProcedurePresetDefinition> BuildCatalog()
    {
        var bx = BxPreset.Create();
        var ose = OseClassicFantasyPreset.Create(bx);

        return
        [
            AlexandrianAdvancedPreset.Create(),
            SimpleFixedDistancePreset.Create(),
            SimpleHexStepPreset.Create(),
            bx,
            ose,
            Adnd2ePreset.Create(),
            Dnd35Preset.Create(),
            Dnd2014Preset.Create(),
            Dnd2024Preset.Create(),
            Pathfinder2eHexplorationPreset.Create(),
            ForbiddenLandsPreset.Create(),
            WorldsWithoutNumberPreset.Create(),
            OneRing2ePreset.Create(),        ];
    }
}
