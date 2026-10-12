using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class ForbiddenLandsPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            CampaignProcedureSchema.CurrentHexTilingDsSymbol,
            ForbiddenLandsPresetKey,
            "Forbidden Lands",
            "Quarter-day journeys with assigned travel roles, resource dice, foraging, camping, forced travel, and mishaps.",
            "quarter-day-expedition",
            "Quarter-day expedition",
            [
                NativeTime(TimeSpan.FromHours(6)),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "quarter-day-activities"), ("baseBudget", "1"), ("budgetUnit", "quarter-day"), ("limitingScope", "party")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "hexes-per-quarter-day"), ("terrainAdjustments", "open=1;difficult=2"), ("routeAdjustmentModel", "manual"), ("weatherAdjustmentModel", "manual")),
                Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                    ("assignmentScope", "participant"), ("activityBudgetModel", "quarter-day"), ("activityKeys", "hike;lead-way;keep-watch;forage;hunt;fish;make-camp;rest;sleep"), ("roleKeys", "leader;lookout")),
                Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                    ("checkTriggerModel", "lead-way-per-travel-activity"), ("failureStateModel", "mishap"), ("directionalErrorModel", "mishap-defined"), ("recognitionModel", "procedure-defined"), ("reorientationModel", "procedure-defined")),
                Structural(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                    ("resourceKinds", "food;water;arrows;torches"), ("inventoryModel", "supply-die"), ("consumptionModel", "usage-roll"), ("consumptionInterval", "quarter-day-or-use")),
                Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ActivityForagingPolicyMechanic,
                    ("resolutionModel", "activity-check"), ("timeCost", "1"), ("timeUnit", "quarter-day"), ("movementTradeoff", "replaces-activity")),
                Structural(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.ActivityCampingPolicyMechanic,
                    ("resolutionModel", "activity-check"), ("timeCost", "1"), ("timeUnit", "quarter-day"), ("watchModel", "keep-watch-activity")),
                Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                    ("normalTravelLimit", "2"), ("limitUnit", "quarter-days"), ("checkModel", "endurance-check"), ("failureConsequence", "fatigue-or-mishap")),
                Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                    ("effectKinds", "fatigue;mishap"), ("accumulationModel", "procedure-defined"), ("recoveryModel", "rest-and-sleep"), ("scope", "participant"))
            ],
            attribution: "Forbidden Lands journey procedure research; exact mishap tables are intentionally not reproduced.",
            disclaimer: "Some exact numerical and table-driven details were not available from a primary legally accessible rules source during Phase 3 and remain manual.");
}
