using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedurePresetFactory;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application;

internal static class Dnd2014Preset
{
    internal static CrawlProcedurePresetDefinition Create() =>
        Preset(
            Dnd2014PresetKey,
            "D&D 5e / 2014",
            "Hourly travel with 2014 pace distances, difficult-terrain slowdown, travel activities, getting lost, foraging, and forced-march exhaustion.",
            "hourly-2014-pace-travel",
            "2014 hourly pace travel",
            [
                NativeTime(TimeSpan.FromHours(1)),
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "speed-and-pace"), ("baseBudget", "1"), ("budgetUnit", "hour"),
                    ("limitingScope", "party-limiting"), ("travelModeKeys", "fast;normal;slow"),
                    ("paceDistanceUnit", "mi"), ("paceRatesPerHour", "fast=4;normal=3;slow=2"),
                    ("paceRatesPerDay", "fast=30;normal=24;slow=18"),
                    ("paceEffects", "fast=passive-perception-minus-5;normal=none;slow=stealth-available")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("adjustmentModel", "multiplier"), ("terrainAdjustments", "normal=1;difficult=0.5"),
                    ("routeAdjustmentModel", "manual"), ("weatherAdjustmentModel", "manual")),
                Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                    ("assignmentScope", "participant"), ("activityBudgetModel", "travel-compatible"),
                    ("activityKeys", "watch;navigate;map;track;forage;stealth"),
                    ("roleKeys", "navigator;lookout")),
                Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                    ("checkTriggerModel", "dm-called-survival-check"), ("failureStateModel", "lost-state"),
                    ("directionalErrorModel", "dm-adjudicated"), ("recognitionModel", "map-or-dm-check"),
                    ("reorientationModel", "dm-adjudicated")),
                Structural(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                    ("resourceKinds", "food;water"), ("inventoryModel", "counted"),
                    ("consumptionModel", "daily-per-character"), ("consumptionInterval", "day")),
                Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                    ("resolutionModel", "wisdom-survival-check"), ("timeCost", "0"),
                    ("timeUnit", "hour"), ("movementTradeoff", "travel-compatible")),
                Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                    ("normalTravelLimit", "8"), ("limitUnit", "hours"),
                    ("checkModel", "escalating-constitution-save"), ("failureConsequence", "exhaustion")),
                Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                    ("effectKinds", "exhaustion"), ("accumulationModel", "levels"),
                    ("recoveryModel", "long-rest-with-food-and-drink"), ("scope", "participant"))
            ],
            attribution: "D&D Basic Rules (2014) / SRD 5.1 travel, adventuring, and exhaustion rules; source: https://www.dndbeyond.com/sources/dnd/basic-rules-2014/adventuring. Pace rates and effects are declarative, not automatic character modifiers.");
}
