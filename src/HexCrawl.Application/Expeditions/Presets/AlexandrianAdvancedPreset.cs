using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class AlexandrianAdvancedPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            CampaignProcedureSchema.CurrentHexTilingDsSymbol,
            "alexandrian-advanced",
            "Alexandrian Advanced",
            "Four-hour distance travel with navigation, persistent veer, per-watch encounters, and built-in resolution helpers.",
            "alexandrian-advanced",
            "Alexandrian Advanced",
            ExecutableCore(
                TimeSpan.FromHours(4),
                TravelResolutionMode.ContinuousDistance,
                ActualDistanceResolutionMode.VariableResolved,
                EncounterCheckCadence.PerWatch,
                usesNavigationChecks: true,
                usesPersistentVeer: true,
                neighborCenterDistanceFactor: 1d / 6d,
                supportsDeliberateDoubleBack: true,
                resolutionHelpers: new ProcedureResolutionHelperProfile(
                    Travel: new TravelResolutionHelperProfile(new DiceRollFormula(2, 6, 3), 0.1d),
                    Navigation: new NavigationResolutionHelperProfile(new DiceRollFormula(1, 20)),
                    Encounter: new EncounterResolutionHelperProfile(
                        new DiceRollFormula(1, 8),
                        DiceRollResultSet.From([1]),
                        DiceRollResultSet.From([8]),
                        8))),
            attribution: "Procedure research based on The Alexandrian hexcrawl watch checklist.");
}
