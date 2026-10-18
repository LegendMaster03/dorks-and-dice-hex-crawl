using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class MutantYearZeroPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "<1:1,1,1:4,4>",
            MutantYearZeroPresetKey,
            "Mutant: Year Zero",
            "Square Zone sectors with diagonal travel, first-visit exploration, and repeat crossings. Variable sector times, scouting, threats, and resources are GM-adjudicated.",
            "mutant-year-zero-zone",
            "Zone sector exploration",
            ExecutableCore(
                TimeSpan.FromHours(4),
                TravelResolutionMode.CellSteps,
                ActualDistanceResolutionMode.Fixed,
                EncounterCheckCadence.None,
                usesNavigationChecks: false,
                usesPersistentVeer: false,
                neighborCenterDistanceFactor: 0d,
                supportsDeliberateDoubleBack: false,
                adjacency: CellAdjacencyMode.EdgeAndVertex)
                .Concat([NativeExplorationFlow(
                    ["choose-zone-action", "resolve-zone-action"],
                    ExplorationDiscoveryTrigger.OnExplore,
                    ExplorationEncounterTrigger.Manual,
                    firstVisitAction: "explore",
                    repeatVisitAction: "cross",
                    actionSelectionStageKey: "choose-zone-action",
                    actionResolutionStageKey: "resolve-zone-action")]).ToArray(),
            attribution: "Mutant: Year Zero Zone exploration rules (Free League).",
            disclaimer: "Four hours is the typical first exploration, not a fixed cost for every crossing. Explored-sector one-hour crossings, nighttime, vehicles, Stalker stunts, Find the Path, Rot, and resources require explicit GM time and outcome adjudication; no copyrighted tables are bundled.",
            category: "Other square-grid starting points");
}
