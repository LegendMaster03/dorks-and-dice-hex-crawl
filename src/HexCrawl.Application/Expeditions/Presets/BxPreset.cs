using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application;

internal static class BxPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            BxPresetKey,
            "B/X",
            "Daily wilderness travel where terrain, getting lost, encounters, foraging, and supplies matter.",
            "daily-wilderness-travel",
            "Daily wilderness travel",
            [
                NativeTime(TimeSpan.FromDays(1)),
                NativeEncounterCadence(EncounterCheckCadence.PerDay),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "fixed-per-day"), ("baseBudget", "1"), ("budgetUnit", "travel-day"), ("limitingScope", "party-limiting")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "multiplier"), ("terrainAdjustments", "clear=1;broken=0.75;difficult=0.5"), ("routeAdjustmentModel", "route-improves-cost"), ("weatherAdjustmentModel", "manual")),
                Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                    ("checkTriggerModel", "daily-terrain-or-context"), ("failureStateModel", "lost-until-recognized"), ("directionalErrorModel", "random-direction"), ("recognitionModel", "procedure-check"), ("reorientationModel", "procedure-check")),
                Structural(GenericProcedureCatalog.EncounterScheduleModule, GenericProcedureCatalog.EncounterSchedulePolicyMechanic,
                    ("scheduleModel", "daily-terrain-sensitive"), ("travelChecksPerInterval", "1"), ("campCheck", "false"), ("terrainProbabilityModel", "terrain-tagged")),
                Structural(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                    ("resourceKinds", "food;water"), ("inventoryModel", "counted"), ("consumptionModel", "fixed-per-person"), ("consumptionInterval", "travel-day")),
                Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                    ("resolutionModel", "procedure-check"), ("timeCost", "0"), ("timeUnit", "travel-day"), ("movementTradeoff", "none"))
            ],
            attribution: "B/X procedure research; executable configuration is an original generic representation.");
}
