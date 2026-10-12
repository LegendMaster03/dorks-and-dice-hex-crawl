using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application;

internal static class Pathfinder2eHexplorationPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            Pathfinder2eHexplorationPresetKey,
            "Pathfinder 2e Hexploration",
            "Daily Hexploration with speed-based activity budgets, terrain costs, reconnoitering, camping, and subsistence.",
            "activity-budget-hexploration",
            "Activity-budget hexploration",
            [
                NativeTime(TimeSpan.FromDays(1)),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "speed-derived-activities"), ("baseBudget", "1"), ("budgetUnit", "hexploration-activity"), ("limitingScope", "party-limiting")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "activity-cost"), ("terrainAdjustments", "open=1;difficult=2;greater-difficult=3"), ("routeAdjustmentModel", "road-improves-one-step"), ("weatherAdjustmentModel", "movement-mode-specific")),
                Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                    ("assignmentScope", "participant"), ("activityBudgetModel", "daily-activity-budget"), ("activityKeys", "travel;reconnoiter;fortify-camp;map-area;subsist"), ("roleKeys", "none")),
                Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                    ("checkTriggerModel", "contextual-getting-lost"), ("failureStateModel", "lost-state"), ("directionalErrorModel", "rules-defined"), ("recognitionModel", "mapping-support"), ("reorientationModel", "rules-defined")),
                Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ActivityForagingPolicyMechanic,
                    ("resolutionModel", "subsist-activity"), ("timeCost", "1"), ("timeUnit", "hexploration-activity"), ("movementTradeoff", "replaces-activity")),
                Structural(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.ActivityCampingPolicyMechanic,
                    ("resolutionModel", "fortify-camp-activity"), ("timeCost", "1"), ("timeUnit", "hexploration-activity"), ("watchModel", "campaign-defined"))
            ],
            attribution: "Pathfinder 2e GM Core Hexploration procedure research via Archives of Nethys.");
}
