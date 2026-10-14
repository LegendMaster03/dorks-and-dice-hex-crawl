using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;
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
            "Campaign map exploration starting point on a bounded 9 by 9 square map. Regions and encounters remain manual.",
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
                supportsDeliberateDoubleBack: false),
            attribution: "Adapted square-map starting point inspired by Modiphius Fallout exploration.",
            disclaimer: "This is a functional generalized movement scaffold, not a full transcription of the published encounter, location, and activity rules. Travel interval is a configurable starting value.",
            category: "Fallout square-map starting points",
            playableBounds: new PeriodicMapBounds(9, 9));
}
