using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.Runtime;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class WastelandWandererPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "<1:1,1,1:4,4>",
            "fallout-wasteland-wanderer",
            "Fallout: Wasteland Wanderer",
            "Solo wasteland travel with a 5 by 5 square map and ordered travel, encounter, action, and journal stages. Narrative resolution remains manual.",
            "fallout-wasteland-wanderer",
            "Wasteland Wanderer exploration",
            ExecutableCore(
                TimeSpan.FromHours(4),
                TravelResolutionMode.CellSteps,
                ActualDistanceResolutionMode.Fixed,
                EncounterCheckCadence.None,
                usesNavigationChecks: false,
                usesPersistentVeer: false,
                neighborCenterDistanceFactor: 0d,
                supportsDeliberateDoubleBack: false)
                .Concat([NativeExplorationFlow(
                    ["travel", "encounter", "take-action", "journal"],
                    ExplorationDiscoveryTrigger.OnFirstVisit,
                    ExplorationEncounterTrigger.EveryTravel)]).ToArray(),
            attribution: "Adapted square-map starting point inspired by Modiphius Fallout exploration.",
            disclaimer: "Exploration stages and encounter timing are configured; encounter tables, supplies, health and narrative journaling are not automated. The interval is a placeholder, not a quoted rule.",
            category: "Fallout square-map starting points",
            playableBounds: new PeriodicMapBounds(5, 5, -2, -2));
}
