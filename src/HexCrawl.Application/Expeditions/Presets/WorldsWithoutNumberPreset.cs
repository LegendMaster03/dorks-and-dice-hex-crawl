using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application.Presets;

internal static class WorldsWithoutNumberPreset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            WorldsWithoutNumberPresetKey,
            "Worlds Without Number",
            "Ten-hour expedition days with terrain speed, travel/camp encounter checks, supplies, foraging, and camping.",
            "ten-hour-expedition-day",
            "Ten-hour expedition day",
            [
                NativeTime(TimeSpan.FromHours(10)),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "distance-per-hour"), ("baseBudget", "10"), ("budgetUnit", "travel-hours"), ("limitingScope", "party-limiting")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "distance-per-hour-multiplier"), ("terrainAdjustments", "easy=1;rough=0.5;severe=0.25"), ("routeAdjustmentModel", "road-multiplier"), ("weatherAdjustmentModel", "weather-multiplier")),
                Structural(GenericProcedureCatalog.EncounterScheduleModule, GenericProcedureCatalog.ContextualEncounterSchedulePolicyMechanic,
                    ("scheduleModel", "travel-and-camp"), ("travelChecksPerInterval", "1"), ("campCheck", "true"), ("terrainProbabilityModel", "terrain-tagged")),
                Structural(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                    ("resourceKinds", "food;water;shelter;fire"), ("inventoryModel", "counted"), ("consumptionModel", "daily-supplies"), ("consumptionInterval", "expedition-day")),
                Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                    ("resolutionModel", "skill-check"), ("timeCost", "0.5"), ("timeUnit", "travel-day"), ("movementTradeoff", "half-or-full-day")),
                Structural(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.CampingPolicyMechanic,
                    ("resolutionModel", "manual-camp"), ("timeCost", "1"), ("timeUnit", "night"), ("watchModel", "camp-encounter-check"))
            ],
            attribution: "Worlds Without Number SRD Wilderness Exploration and Overland Travel procedure research.");
}
