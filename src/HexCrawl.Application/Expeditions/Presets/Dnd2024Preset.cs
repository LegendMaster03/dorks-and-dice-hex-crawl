using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class Dnd2024Preset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            Dnd2024PresetKey,
            "D&D 5.5e / 2024",
            "Hourly travel with pace and terrain limits, party activities, and extended-travel exhaustion.",
            "hourly-pace-travel",
            "Hourly pace travel",
            [
                NativeTime(TimeSpan.FromHours(1)),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "speed-and-pace"), ("baseBudget", "1"), ("budgetUnit", "hour"), ("limitingScope", "slowest-traveler"), ("travelModeKeys", "normal;fast;slow")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "maximum-pace"),
                    ("terrainAdjustments", "arctic=fast-if-appropriately-equipped;coastal=normal;desert=normal;forest=normal;grassland=fast;hill=normal;mountain=slow;swamp=slow;underdark=normal;urban=normal;waterborne=special"),
                    ("routeAdjustmentModel", "good-road-improves-one-step"),
                    ("weatherAdjustmentModel", "environment-specific")),
                Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                    ("assignmentScope", "participant"), ("activityBudgetModel", "travel-compatible"), ("activityKeys", "navigate;forage;search;watch;stealth"), ("roleKeys", "navigator;lookout")),
                Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                    ("normalTravelLimit", "8"), ("limitUnit", "hours"), ("checkModel", "escalating-constitution-save"), ("failureConsequence", "exhaustion")),
                Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                    ("effectKinds", "exhaustion"), ("accumulationModel", "levels"), ("recoveryModel", "rules-defined-rest"), ("scope", "participant"))
            ],
            attribution: "D&D Free Rules (2024) travel terrain and extended travel procedure research.");
}
