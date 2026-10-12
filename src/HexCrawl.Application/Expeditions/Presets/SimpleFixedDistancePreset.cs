using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class SimpleFixedDistancePreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            "simple-fixed-distance",
            "Simple Fixed Distance",
            "Four-hour fixed-distance travel with partial cell progress and no navigation or encounter checks.",
            "simple-fixed-distance",
            "Simple Fixed Distance",
            ExecutableCore(
                TimeSpan.FromHours(4),
                TravelResolutionMode.ContinuousDistance,
                ActualDistanceResolutionMode.Fixed,
                EncounterCheckCadence.None,
                usesNavigationChecks: false,
                usesPersistentVeer: false,
                neighborCenterDistanceFactor: 0d,
                supportsDeliberateDoubleBack: false),
            category: "Generic starting points");
}
