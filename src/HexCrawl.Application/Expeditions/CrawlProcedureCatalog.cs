using System.Globalization;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public static class CrawlProcedureCatalog
{
    public const string BxPresetKey = "bx";
    public const string OseClassicPresetKey = "ose-classic-fantasy";
    public const string Adnd2ePresetKey = "adnd-2e";
    public const string Dnd35PresetKey = "dnd-3-5e";
    public const string Dnd2024PresetKey = "dnd-2024";
    public const string Pathfinder2eHexplorationPresetKey = "pathfinder-2e-hexploration";
    public const string ForbiddenLandsPresetKey = "forbidden-lands";
    public const string WorldsWithoutNumberPresetKey = "worlds-without-number";
    public const string OneRing2ePresetKey = "the-one-ring-2e";
    public const string MixedHouseRulePresetKey = "mixed-house-rule";

    private static IReadOnlyList<CrawlProcedurePresetDefinition> FullCatalog { get; } = BuildCatalog();

    /// <summary>
    /// Legacy runtime-profile compatibility view retained until the Phase 4 preset/composer surface replaces it.
    /// </summary>
    public static IReadOnlyList<CrawlProcedurePresetDefinition> All { get; } = FullCatalog.Take(3).ToArray();

    /// <summary>
    /// Authoritative creation-time preset catalog, including the Phase 3 architecture proof matrix.
    /// </summary>
    public static IReadOnlyList<CrawlProcedurePresetDefinition> Catalog => FullCatalog;

    public static CrawlProcedurePresetDefinition Resolve(string? key)
    {
        var resolved = string.IsNullOrWhiteSpace(key)
            ? All[0]
            : Catalog.FirstOrDefault(item => string.Equals(item.PresetKey, key.Trim(), StringComparison.OrdinalIgnoreCase));
        return resolved ?? throw new ArgumentException($"Unknown crawl procedure preset '{key}'.", nameof(key));
    }

    private static IReadOnlyList<CrawlProcedurePresetDefinition> BuildCatalog()
    {
        var bx = Preset(
            BxPresetKey,
            "B/X",
            "Daily wilderness travel with terrain-sensitive movement, getting-lost procedure, encounter scheduling, foraging, and ration/resource concepts.",
            "daily-wilderness-travel",
            "Daily wilderness travel",
            CoreProfile("daily-wilderness-travel", "Daily wilderness travel", TimeSpan.FromDays(1), TravelResolutionMode.ContinuousDistance, EncounterCheckCadence.PerDay, navigation: false),
            [
                Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                    ("budgetModel", "fixed-per-day"), ("baseBudget", "1"), ("budgetUnit", "travel-day"), ("limitingScope", "party-limiting")),
                Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                    ("costModel", "multipliers"), ("terrainCosts", "clear=1;broken=0.75;difficult=0.5"), ("routeAdjustmentModel", "route-improves-cost"), ("weatherAdjustmentModel", "manual")),
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

        var ose = new CrawlProcedurePresetDefinition(
            OseClassicPresetKey,
            "Old-School Essentials Classic Fantasy",
            "Aliases the B/X generic wilderness procedure because Phase 3 research identified no Hex Crawl-relevant procedural difference that warrants a separate runtime recipe.",
            1,
            bx.Recipe,
            "Old-School Essentials is referenced only as preset metadata; the materialized procedure is the shared generic B/X recipe.");

        return
        [
            new(
                "alexandrian-advanced",
                "Alexandrian Advanced",
                "Four-hour continuous-distance travel with navigation, persistent veer, per-watch encounters, and procedure helpers.",
                1,
                CampaignProcedureCompatibilityProjector.ToRecipe(AlexandrianAdvancedTemplate()),
                "Procedure research based on The Alexandrian hexcrawl watch checklist."),
            new(
                "simple-fixed-distance",
                "Simple Fixed Distance",
                "Four-hour continuous-distance travel using fixed resolved distance without navigation or encounter checks.",
                1,
                CampaignProcedureCompatibilityProjector.ToRecipe(SimplifiedFixedDistanceTemplate())),
            new(
                "simple-hex-step",
                "Simple Hex Step",
                "Four-hour travel resolved as whole hex steps without intra-hex progress, navigation, or encounter checks.",
                1,
                CampaignProcedureCompatibilityProjector.ToRecipe(SimplifiedHexStepTemplate())),

            bx,
            ose,

            Preset(
                Adnd2ePresetKey,
                "AD&D 2e",
                "Daily overland travel represented with terrain movement costs and an explicit getting-lost outcome contract. Exact encounter scheduling remains intentionally manual where Phase 3 evidence was incomplete.",
                "daily-movement-point-travel",
                "Daily movement-point travel",
                CoreProfile("daily-movement-point-travel", "Daily movement-point travel", TimeSpan.FromDays(1), TravelResolutionMode.ContinuousDistance, EncounterCheckCadence.None, navigation: false),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "movement-points"), ("baseBudget", "1"), ("budgetUnit", "daily-movement-budget"), ("limitingScope", "party-limiting")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "movement-points-per-distance"), ("terrainCosts", "open=1;rough=2;severe=3"), ("routeAdjustmentModel", "route-improves-cost"), ("weatherAdjustmentModel", "manual")),
                    Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                        ("checkTriggerModel", "daily-terrain-or-context"), ("failureStateModel", "lost-state"), ("directionalErrorModel", "manual-off-course"), ("recognitionModel", "procedure-check"), ("reorientationModel", "procedure-check")),
                    Structural(GenericProcedureCatalog.EncounterScheduleModule, GenericProcedureCatalog.EncounterSchedulePolicyMechanic,
                        ("scheduleModel", "manual-contextual"), ("travelChecksPerInterval", "0"), ("campCheck", "false"), ("terrainProbabilityModel", "manual"))
                ],
                attribution: "AD&D 2e procedure research; uncertain exact encounter cadence is not asserted by the preset."),

            Preset(
                Dnd35PresetKey,
                "D&D 3.5e",
                "Hourly overland travel with difficult-terrain effects, getting lost, foraging, forced-march checks, and persistent fatigue consequences represented generically.",
                "hourly-overland-travel",
                "Hourly overland travel",
                CoreProfile("hourly-overland-travel", "Hourly overland travel", TimeSpan.FromHours(1), TravelResolutionMode.ContinuousDistance, EncounterCheckCadence.None, navigation: false),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "speed-derived-distance"), ("baseBudget", "1"), ("budgetUnit", "hour"), ("limitingScope", "party-limiting")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "multipliers"), ("terrainCosts", "normal=1;difficult=0.5"), ("routeAdjustmentModel", "route-may-ignore-terrain"), ("weatherAdjustmentModel", "manual")),
                    Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                        ("checkTriggerModel", "hourly-poor-visibility-or-terrain"), ("failureStateModel", "lost-until-recognized"), ("directionalErrorModel", "random-direction"), ("recognitionModel", "periodic-check"), ("reorientationModel", "procedure-check")),
                    Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                        ("resolutionModel", "skill-check"), ("timeCost", "0.5"), ("timeUnit", "movement-rate"), ("movementTradeoff", "half-speed")),
                    Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                        ("normalTravelLimit", "8"), ("limitUnit", "hours"), ("checkModel", "escalating-check"), ("failureConsequence", "fatigue-and-nonlethal-effect")),
                    Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                        ("effectKinds", "fatigue;nonlethal-damage"), ("accumulationModel", "per-failed-check"), ("recoveryModel", "rest"), ("scope", "participant"))
                ],
                attribution: "D&D 3.5 SRD wilderness, movement, and Survival procedure research."),

            Preset(
                Dnd2024PresetKey,
                "D&D 5.5e / 2024",
                "Hourly travel with pace and terrain limits plus extended-travel exhaustion represented as generic movement, terrain, forced-travel, and persistent-effect contracts.",
                "hourly-pace-travel",
                "Hourly pace travel",
                CoreProfile("hourly-pace-travel", "Hourly pace travel", TimeSpan.FromHours(1), TravelResolutionMode.ContinuousDistance, EncounterCheckCadence.None, navigation: false),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "speed-and-pace"), ("baseBudget", "1"), ("budgetUnit", "hour"), ("limitingScope", "slowest-traveler")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "maximum-pace"), ("terrainCosts", "fast=1;normal=2;slow=3"), ("routeAdjustmentModel", "good-road-improves-one-step"), ("weatherAdjustmentModel", "environment-specific")),
                    Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                        ("assignmentScope", "participant"), ("activityBudgetModel", "travel-compatible"), ("activityKeys", "navigate;forage;search;watch;stealth"), ("roleKeys", "navigator;lookout")),
                    Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                        ("normalTravelLimit", "8"), ("limitUnit", "hours"), ("checkModel", "escalating-constitution-save"), ("failureConsequence", "exhaustion")),
                    Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                        ("effectKinds", "exhaustion"), ("accumulationModel", "levels"), ("recoveryModel", "rules-defined-rest"), ("scope", "participant"))
                ],
                attribution: "D&D Free Rules (2024) travel terrain and extended travel procedure research."),

            Preset(
                Pathfinder2eHexplorationPresetKey,
                "Pathfinder 2e Hexploration",
                "Daily hexploration with a speed-derived activity budget, group and individual activities, terrain activity costs, reconnoitering, camping, and subsistence represented generically.",
                "activity-budget-hexploration",
                "Activity-budget hexploration",
                CoreProfile("activity-budget-hexploration", "Activity-budget hexploration", TimeSpan.FromDays(1), TravelResolutionMode.HexSteps, EncounterCheckCadence.PerDay, navigation: false),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "speed-derived-activities"), ("baseBudget", "1"), ("budgetUnit", "hexploration-activity"), ("limitingScope", "party-limiting")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "activity-cost"), ("terrainCosts", "open=1;difficult=2;greater-difficult=3"), ("routeAdjustmentModel", "road-improves-one-step"), ("weatherAdjustmentModel", "movement-mode-specific")),
                    Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                        ("assignmentScope", "participant"), ("activityBudgetModel", "daily-activity-budget"), ("activityKeys", "travel;reconnoiter;fortify-camp;map-area;subsist"), ("roleKeys", "none")),
                    Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                        ("checkTriggerModel", "contextual-getting-lost"), ("failureStateModel", "lost-state"), ("directionalErrorModel", "rules-defined"), ("recognitionModel", "mapping-support"), ("reorientationModel", "rules-defined")),
                    Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                        ("resolutionModel", "subsist-activity"), ("timeCost", "1"), ("timeUnit", "hexploration-activity"), ("movementTradeoff", "replaces-activity")),
                    Structural(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.CampingPolicyMechanic,
                        ("resolutionModel", "fortify-camp-activity"), ("timeCost", "1"), ("timeUnit", "hexploration-activity"), ("watchModel", "campaign-defined"))
                ],
                attribution: "Pathfinder 2e GM Core Hexploration procedure research via Archives of Nethys."),

            Preset(
                ForbiddenLandsPresetKey,
                "Forbidden Lands",
                "Quarter-day journey structure with participant activities, leading the way, keeping watch, resource dice, foraging, camping, forced travel, and mishap/effect concepts represented generically.",
                "quarter-day-expedition",
                "Quarter-day expedition",
                CoreProfile("quarter-day-expedition", "Quarter-day expedition", TimeSpan.FromHours(6), TravelResolutionMode.HexSteps, EncounterCheckCadence.PerWatch, navigation: true),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "quarter-day-activities"), ("baseBudget", "1"), ("budgetUnit", "quarter-day"), ("limitingScope", "party")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "hexes-per-quarter-day"), ("terrainCosts", "open=1;difficult=2"), ("routeAdjustmentModel", "manual"), ("weatherAdjustmentModel", "manual")),
                    Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                        ("assignmentScope", "participant"), ("activityBudgetModel", "quarter-day"), ("activityKeys", "hike;lead-way;keep-watch;forage;hunt;fish;make-camp;rest;sleep"), ("roleKeys", "leader;lookout")),
                    Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                        ("checkTriggerModel", "lead-way-per-travel-activity"), ("failureStateModel", "mishap"), ("directionalErrorModel", "mishap-defined"), ("recognitionModel", "procedure-defined"), ("reorientationModel", "procedure-defined")),
                    Structural(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                        ("resourceKinds", "food;water;arrows;torches"), ("inventoryModel", "supply-die"), ("consumptionModel", "usage-roll"), ("consumptionInterval", "quarter-day-or-use")),
                    Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                        ("resolutionModel", "activity-check"), ("timeCost", "1"), ("timeUnit", "quarter-day"), ("movementTradeoff", "replaces-activity")),
                    Structural(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.CampingPolicyMechanic,
                        ("resolutionModel", "activity-check"), ("timeCost", "1"), ("timeUnit", "quarter-day"), ("watchModel", "keep-watch-activity")),
                    Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                        ("normalTravelLimit", "2"), ("limitUnit", "quarter-days"), ("checkModel", "endurance-check"), ("failureConsequence", "fatigue-or-mishap")),
                    Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                        ("effectKinds", "fatigue;mishap"), ("accumulationModel", "procedure-defined"), ("recoveryModel", "rest-and-sleep"), ("scope", "participant"))
                ],
                attribution: "Forbidden Lands journey procedure research; exact mishap tables are intentionally not reproduced.",
                disclaimer: "Some exact numerical and table-driven details were not available from a primary legally accessible rules source during Phase 3 and remain manual."),

            Preset(
                WorldsWithoutNumberPresetKey,
                "Worlds Without Number",
                "Ten-hour overland travel day with terrain speed, separate travel/camp encounter opportunities, supplies, foraging, camping, and expedition-day concepts represented generically.",
                "ten-hour-expedition-day",
                "Ten-hour expedition day",
                CoreProfile("ten-hour-expedition-day", "Ten-hour expedition day", TimeSpan.FromHours(10), TravelResolutionMode.ContinuousDistance, EncounterCheckCadence.PerWatch, navigation: false),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "distance-per-hour"), ("baseBudget", "10"), ("budgetUnit", "travel-hours"), ("limitingScope", "party-limiting")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "distance-per-hour"), ("terrainCosts", "easy=1;rough=0.5;severe=0.25"), ("routeAdjustmentModel", "road-multiplier"), ("weatherAdjustmentModel", "weather-multiplier")),
                    Structural(GenericProcedureCatalog.EncounterScheduleModule, GenericProcedureCatalog.EncounterSchedulePolicyMechanic,
                        ("scheduleModel", "travel-and-camp"), ("travelChecksPerInterval", "1"), ("campCheck", "true"), ("terrainProbabilityModel", "terrain-tagged")),
                    Structural(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                        ("resourceKinds", "food;water;shelter;fire"), ("inventoryModel", "counted"), ("consumptionModel", "daily-supplies"), ("consumptionInterval", "expedition-day")),
                    Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                        ("resolutionModel", "skill-check"), ("timeCost", "0.5"), ("timeUnit", "travel-day"), ("movementTradeoff", "half-or-full-day")),
                    Structural(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.CampingPolicyMechanic,
                        ("resolutionModel", "manual-camp"), ("timeCost", "1"), ("timeUnit", "night"), ("watchModel", "camp-encounter-check"))
                ],
                attribution: "Worlds Without Number SRD Wilderness Exploration and Overland Travel procedure research."),

            Preset(
                OneRing2ePresetKey,
                "The One Ring 2e",
                "Role-driven journey process with route planning, Guide progress tests, targeted journey events, terrain influence, and persistent fatigue represented as manual/assisted generic contracts.",
                "role-driven-journey",
                "Role-driven journey",
                CoreProfile("role-driven-journey", "Role-driven journey", TimeSpan.FromDays(1), TravelResolutionMode.ContinuousDistance, EncounterCheckCadence.None, navigation: false),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "journey-progress"), ("baseBudget", "1"), ("budgetUnit", "journey-leg"), ("limitingScope", "guide")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "terrain-difficulty"), ("terrainCosts", "open=1;hard=2;severe=3"), ("routeAdjustmentModel", "road-assistance"), ("weatherAdjustmentModel", "manual")),
                    Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                        ("assignmentScope", "role"), ("activityBudgetModel", "journey-role"), ("activityKeys", "guide;hunt;lookout;scout"), ("roleKeys", "guide;hunter;lookout;scout")),
                    Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                        ("effectKinds", "fatigue"), ("accumulationModel", "journey-events"), ("recoveryModel", "safe-prolonged-rest"), ("scope", "participant")),
                    Structural(GenericProcedureCatalog.JourneyEventsModule, GenericProcedureCatalog.JourneyEventPolicyMechanic,
                        ("triggerModel", "guide-progress-test"), ("targetingModel", "travel-role"), ("terrainInfluence", "difficulty-and-road"), ("consequenceModel", "event-and-fatigue")),
                    Structural(GenericProcedureCatalog.JourneyProcessModule, GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
                        ("stageModel", "route-then-events-then-arrival"), ("progressModel", "guide-marching-progress"), ("completionModel", "reach-destination"), ("roleDriven", "true"))
                ],
                attribution: "The One Ring 2e journey procedure research from publisher materials and secondary summaries.",
                disclaimer: "Exact event tables, distances, modifiers, and fatigue values are intentionally not encoded; full journey execution remains a later-phase subsystem."),

            Preset(
                MixedHouseRulePresetKey,
                "Mixed House Rule",
                "Deliberately mixes a four-hour native travel core with participant activity budgeting, supply-die resources, forced travel, assisted navigation outcomes, and role-targeted journey events.",
                "mixed-modular-expedition",
                "Mixed modular expedition",
                CoreProfile("mixed-modular-expedition", "Mixed modular expedition", TimeSpan.FromHours(4), TravelResolutionMode.ContinuousDistance, EncounterCheckCadence.None, navigation: false),
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "activity-and-distance"), ("baseBudget", "1"), ("budgetUnit", "watch"), ("limitingScope", "party-limiting")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("costModel", "activity-cost"), ("terrainCosts", "open=1;difficult=2;severe=3"), ("routeAdjustmentModel", "road-improves-one-step"), ("weatherAdjustmentModel", "manual")),
                    Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.ParticipantActivityPolicyMechanic,
                        ("assignmentScope", "participant"), ("activityBudgetModel", "per-watch"), ("activityKeys", "travel;reconnoiter;forage;make-camp;lookout"), ("roleKeys", "navigator;lookout;forager;scout")),
                    Structural(GenericProcedureCatalog.NavigationOutcomeModule, GenericProcedureCatalog.NavigationOutcomePolicyMechanic,
                        ("checkTriggerModel", "per-watch-when-navigation-required"), ("failureStateModel", "lost-until-recognized"), ("directionalErrorModel", "persistent-veer"), ("recognitionModel", "boundary-check"), ("reorientationModel", "procedure-check")),
                    Structural(GenericProcedureCatalog.ResourceConsumptionModule, GenericProcedureCatalog.ResourceConsumptionPolicyMechanic,
                        ("resourceKinds", "food;water;light"), ("inventoryModel", "supply-die"), ("consumptionModel", "usage-roll"), ("consumptionInterval", "watch")),
                    Structural(GenericProcedureCatalog.ForagingModule, GenericProcedureCatalog.ForagingPolicyMechanic,
                        ("resolutionModel", "activity-check"), ("timeCost", "1"), ("timeUnit", "watch-activity"), ("movementTradeoff", "replaces-activity")),
                    Structural(GenericProcedureCatalog.CampingModule, GenericProcedureCatalog.CampingPolicyMechanic,
                        ("resolutionModel", "activity-check"), ("timeCost", "1"), ("timeUnit", "watch"), ("watchModel", "assigned-lookout")),
                    Structural(GenericProcedureCatalog.ForcedTravelModule, GenericProcedureCatalog.ForcedTravelPolicyMechanic,
                        ("normalTravelLimit", "2"), ("limitUnit", "watches"), ("checkModel", "escalating-check"), ("failureConsequence", "fatigue")),
                    Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                        ("effectKinds", "fatigue"), ("accumulationModel", "levels"), ("recoveryModel", "safe-rest"), ("scope", "participant")),
                    Structural(GenericProcedureCatalog.JourneyEventsModule, GenericProcedureCatalog.JourneyEventPolicyMechanic,
                        ("triggerModel", "per-watch-or-landmark"), ("targetingModel", "travel-role"), ("terrainInfluence", "difficulty"), ("consequenceModel", "event-and-fatigue"))
                ],
                attribution: "Original Dorks & Dice house-rule composition used to prove cross-preset generic composition.")
        ];
    }

    private static CrawlProcedurePresetDefinition Preset(
        string presetKey,
        string displayName,
        string description,
        string procedureKey,
        string procedureName,
        CrawlProcedureProfile compatibilityTemplate,
        IReadOnlyList<ProcedureModuleRecipe> structuralModules,
        string? attribution = null,
        string? disclaimer = null)
    {
        var captured = CampaignProcedureCompatibilityProjector.ToRecipe(compatibilityTemplate);
        return new CrawlProcedurePresetDefinition(
            presetKey,
            displayName,
            description,
            1,
            captured with
            {
                DefaultProcedureKey = procedureKey,
                DefaultProcedureName = procedureName,
                ModuleSelections = captured.ModuleSelections.Concat(structuralModules).ToArray()
            },
            attribution,
            disclaimer);
    }

    private static ProcedureModuleRecipe Structural(
        string moduleKey,
        string mechanicKey,
        params (string Key, string Value)[] parameters) =>
        new(
            moduleKey,
            mechanicKey,
            1,
            parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));

    private static CrawlProcedureProfile CoreProfile(
        string key,
        string name,
        TimeSpan interval,
        TravelResolutionMode travelResolution,
        EncounterCheckCadence encounterCadence,
        bool navigation)
    {
        var hexSteps = travelResolution == TravelResolutionMode.HexSteps;
        return new CrawlProcedureProfile
        {
            Key = key,
            Name = name,
            WatchLength = interval,
            TravelResolution = travelResolution,
            ActualDistanceResolution = ActualDistanceResolutionMode.Fixed,
            EncounterCadence = encounterCadence,
            UsesNavigationChecks = navigation,
            UsesPersistentVeer = false,
            TracksIntraHexProgress = !hexSteps,
            DirectionChangesCostProgress = false,
            SupportsDeliberateDoubleBack = false,
            StartingExitProgressFactor = 0.5d,
            NearExitProgressFactor = 0.5d,
            FarExitProgressFactor = 1d,
            BackExitProgressFactor = 0.5d
        };
    }

    private static CrawlProcedureProfile AlexandrianAdvancedTemplate() => new()
    {
        Key = "alexandrian-advanced",
        Name = "Alexandrian Advanced",
        WatchLength = TimeSpan.FromHours(4),
        TravelResolution = TravelResolutionMode.ContinuousDistance,
        ActualDistanceResolution = ActualDistanceResolutionMode.VariableResolved,
        EncounterCadence = EncounterCheckCadence.PerWatch,
        UsesNavigationChecks = true,
        UsesPersistentVeer = true,
        TracksIntraHexProgress = true,
        DirectionChangesCostProgress = true,
        SupportsDeliberateDoubleBack = true,
        StartingExitProgressFactor = 0.5d,
        NearExitProgressFactor = 0.5d,
        FarExitProgressFactor = 1d,
        BackExitProgressFactor = 0.5d,
        DirectionChangeProgressCostFactor = 1d / 6d,
        ResolutionHelpers = new ProcedureResolutionHelperProfile(
            Travel: new TravelResolutionHelperProfile(new DiceRollFormula(2, 6, 3), 0.1d),
            Navigation: new NavigationResolutionHelperProfile(new DiceRollFormula(1, 20)),
            Encounter: new EncounterResolutionHelperProfile(
                new DiceRollFormula(1, 8),
                DiceRollResultSet.From(new[] { 1 }),
                DiceRollResultSet.From(new[] { 8 }),
                8))
    };

    private static CrawlProcedureProfile SimplifiedFixedDistanceTemplate() => new()
    {
        Key = "simple-fixed-distance",
        Name = "Simple Fixed Distance",
        WatchLength = TimeSpan.FromHours(4),
        TravelResolution = TravelResolutionMode.ContinuousDistance,
        ActualDistanceResolution = ActualDistanceResolutionMode.Fixed,
        EncounterCadence = EncounterCheckCadence.None,
        UsesNavigationChecks = false,
        UsesPersistentVeer = false,
        TracksIntraHexProgress = true,
        DirectionChangesCostProgress = false,
        SupportsDeliberateDoubleBack = false,
        StartingExitProgressFactor = 0.5d,
        NearExitProgressFactor = 0.5d,
        FarExitProgressFactor = 1d,
        BackExitProgressFactor = 0.5d
    };

    private static CrawlProcedureProfile SimplifiedHexStepTemplate() => new()
    {
        Key = "simple-hex-step",
        Name = "Simple Hex Step",
        WatchLength = TimeSpan.FromHours(4),
        TravelResolution = TravelResolutionMode.HexSteps,
        ActualDistanceResolution = ActualDistanceResolutionMode.Fixed,
        EncounterCadence = EncounterCheckCadence.None,
        UsesNavigationChecks = false,
        UsesPersistentVeer = false,
        TracksIntraHexProgress = false,
        DirectionChangesCostProgress = false,
        SupportsDeliberateDoubleBack = false
    };
}
