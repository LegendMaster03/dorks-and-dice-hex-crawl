using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class SimpleHexStepPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "simple-hex-step",
            "Simple Hex Step",
            "Four-hour whole-cell travel with no partial progress, navigation, or encounter checks.",
            "simple-hex-step",
            "Simple Hex Step",
            ExecutableCore(
                TimeSpan.FromHours(4),
                TravelResolutionMode.HexSteps,
                ActualDistanceResolutionMode.Fixed,
                EncounterCheckCadence.None,
                usesNavigationChecks: false,
                usesPersistentVeer: false,
                tracksIntraHexProgress: false,
                directionChangesCostProgress: false,
                supportsDeliberateDoubleBack: false),
            category: "Generic starting points");
}
