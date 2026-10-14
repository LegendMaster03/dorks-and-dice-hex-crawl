using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;
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
            "Solo wasteland exploration starting point on a bounded 5 by 5 square map. Encounters and journaling remain manual.",
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
                supportsDeliberateDoubleBack: false),
            attribution: "Adapted square-map starting point inspired by Modiphius Fallout exploration.",
            disclaimer: "This is a functional generalized movement scaffold, not a full transcription of the published encounter, location, and activity rules. Travel interval is a configurable starting value.",
            category: "Fallout square-map starting points",
            playableBounds: new PeriodicMapBounds(5, 5));
}
