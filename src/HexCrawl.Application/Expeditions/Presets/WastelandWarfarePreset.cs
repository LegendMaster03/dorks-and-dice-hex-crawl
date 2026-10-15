using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.Runtime;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class WastelandWarfarePreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "<1:1,1,1:4,4>",
            "fallout-wasteland-warfare-2e",
            "Fallout: Wasteland Warfare 2e",
            "Campaign exploration with Explore, Travel, and Rest actions on a centered 9 by 9 square map. Regions and encounters remain manual.",
            "fallout-wasteland-warfare-2e",
            "Wasteland Warfare campaign travel",
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
                    ["choose-campaign-action", "resolve-campaign-action"],
                    ExplorationDiscoveryTrigger.OnExplore,
                    ExplorationEncounterTrigger.OnExploreOrOptionalTravel,
                    firstVisitAction: "explore",
                    repeatVisitAction: "travel",
                    restSiteAction: "rest")]).ToArray(),
            attribution: "Adapted square-map starting point inspired by Modiphius Fallout exploration.",
            disclaimer: "Campaign action eligibility and encounter opportunities are configured; region generation, encounter tables, resting recovery, and opposition remain manual. The interval is a placeholder.",
            category: "Fallout square-map starting points",
            playableBounds: new PeriodicMapBounds(9, 9, -4, -4));
}
