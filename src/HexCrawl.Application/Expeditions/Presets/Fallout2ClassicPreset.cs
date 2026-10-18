using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class Fallout2ClassicPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "<1:1,1,1:4,4>",
            Fallout2ClassicPresetKey,
            "Fallout 2 (1998)",
            "Continuous travel across Fallout 2 square world-map sectors; mountain slowdown, encounters, Outdoorsman avoidance, and map revelation are GM-adjudicated.",
            "fallout-2-classic-world-map",
            "Fallout 2 world-map travel",
            ExecutableCore(
                TimeSpan.FromHours(1),
                TravelResolutionMode.ContinuousDistance,
                ActualDistanceResolutionMode.Fixed,
                EncounterCheckCadence.None,
                usesNavigationChecks: false,
                usesPersistentVeer: false,
                neighborCenterDistanceFactor: 0d,
                supportsDeliberateDoubleBack: false,
                adjacency: CellAdjacencyMode.EdgeAndVertex)
                .Concat([NativeExplorationFlow(
                    ["travel", "check-encounter", "reveal-map"],
                    ExplorationDiscoveryTrigger.OnFirstVisit,
                    ExplorationEncounterTrigger.Manual)]).ToArray(),
            attribution: "Fallout 2 (1998) world-map terrain and travel behavior.",
            disclaimer: "The one-hour interval is for bookkeeping, not a per-sector rule. Relative terrain rates use desert/city/coast as baseline (25 h/sector) and mountain 32.5 h. Without a movement budget and map-distance calibration the rates are reference values, not automatic speeds. Encounter rolls, Outdoorsman, Highwayman fuel/speed, and fog of war remain manual.",
            category: "Other square-grid starting points");
}
