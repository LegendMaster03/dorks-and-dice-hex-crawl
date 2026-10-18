using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class WalkingDeadUniversePreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "<1:1,1,1:4,4>",
            WalkingDeadUniversePresetKey,
            "The Walking Dead Universe RPG",
            "Square 10-km sectors, cardinal-only travel, a one-sector walking day, and optional scouting of newly entered sectors.",
            "walking-dead-sector-travel",
            "Walking Dead sector travel",
            ExecutableCore(
                TimeSpan.FromDays(1),
                TravelResolutionMode.CellSteps,
                ActualDistanceResolutionMode.Fixed,
                EncounterCheckCadence.None,
                usesNavigationChecks: false,
                usesPersistentVeer: false,
                neighborCenterDistanceFactor: 0d,
                supportsDeliberateDoubleBack: false)
                .Concat([NativeExplorationFlow(
                    ["enter-sector", "scout-optional", "resolve-sector"],
                    ExplorationDiscoveryTrigger.OnFirstVisit,
                    ExplorationEncounterTrigger.Manual)]).ToArray(),
            attribution: "The Walking Dead Universe Roleplaying Game sector and travel rules (Free League).",
            disclaimer: "One day is the on-foot sector rate. Vehicle ranges, Scout rolls and discoveries, hazards, and encounters require GM adjudication. Scouting is optional, not an automatic roll. No official tables are bundled.",
            category: "Other square-grid starting points");
}
