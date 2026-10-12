using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application;

internal static class Adnd2ePreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            Adnd2ePresetKey,
            "AD&D 2e",
            "Daily overland travel with terrain movement costs and getting lost; encounter scheduling remains manual where source detail is uncertain.",
            "daily-movement-point-travel",
            "Daily movement-point travel",
            [
                NativeTime(TimeSpan.FromDays(1)),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "movement-points"), ("baseBudget", "1"), ("budgetUnit", "daily-movement-budget"), ("limitingScope", "party-limiting")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "movement-points-per-distance"), ("terrainAdjustments", "open=1;rough=2;severe=3"), ("routeAdjustmentModel", "route-improves-cost"), ("weatherAdjustmentModel", "manual")),
                Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                    ("checkTriggerModel", "daily-terrain-or-context"), ("failureStateModel", "lost-state"), ("directionalErrorModel", "manual-off-course"), ("recognitionModel", "procedure-check"), ("reorientationModel", "procedure-check")),
                Structural(GenericProcedureCatalog.EncounterScheduleModule, GenericProcedureCatalog.ContextualEncounterSchedulePolicyMechanic,
                    ("scheduleModel", "manual-contextual"), ("travelChecksPerInterval", "0"), ("campCheck", "false"), ("terrainProbabilityModel", "manual"))
            ],
            attribution: "AD&D 2e procedure research; uncertain exact encounter cadence is not asserted by the preset.");
}
