using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class FalloutClassicPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "<1:1,1,1:4,4>",
            FalloutClassicPresetKey,
            "Fallout (1997)",
            "Continuous travel across classic Fallout square world-map sectors; terrain rates, encounter checks, and map revelation are GM-adjudicated.",
            "fallout-classic-world-map",
            "Classic Fallout world-map travel",
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
                .Concat([
                    NativeExplorationFlow(
                        ["travel", "check-encounter", "reveal-map"],
                        ExplorationDiscoveryTrigger.OnFirstVisit,
                        ExplorationEncounterTrigger.Manual),
                    Structural(GenericProcedureCatalog.TerrainMovementModule,
                        GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("adjustmentModel", "multiplier"),
                        ("terrainAdjustments", "desert=1;coast=1;city=1.6666666666666667;mountain=0.5"),
                        ("routeAdjustmentModel", "manual"),
                        ("weatherAdjustmentModel", "manual"))
                ]).ToArray(),
            attribution: "Fallout (1997) classic world-map terrain and travel behavior.",
            disclaimer: "The one-hour interval is for bookkeeping, not a per-sector rule. Relative terrain rates use desert/coast as baseline (10 h/sector), city 6 h, and mountain 20 h. Without a movement budget and map-distance calibration the rates are reference values, not automatic travel speeds. Encounter rolls, vehicle/perk effects, and fog of war remain manual.",
            category: "Other square-grid starting points");
}
