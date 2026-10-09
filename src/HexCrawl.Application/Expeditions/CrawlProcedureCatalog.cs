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

    private static IReadOnlyList<CrawlProcedurePresetDefinition> FullCatalog { get; } = BuildCatalog();

    public static IReadOnlyList<CrawlProcedurePresetDefinition> All => FullCatalog;
    public static IReadOnlyList<CrawlProcedurePresetDefinition> Catalog => FullCatalog;

    public static CrawlProcedurePresetDefinition Resolve(string? key)
    {
        var resolved = string.IsNullOrWhiteSpace(key)
            ? FullCatalog[0]
            : FullCatalog.FirstOrDefault(item => string.Equals(item.PresetKey, key.Trim(), StringComparison.OrdinalIgnoreCase));
        return resolved ?? throw new ArgumentException($"Unknown crawl procedure preset '{key}'.", nameof(key));
    }

    private static IReadOnlyList<CrawlProcedurePresetDefinition> BuildCatalog()
    {
        var bx = Preset(
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

        var ose = new CrawlProcedurePresetDefinition(
            OseClassicPresetKey,
            "Old-School Essentials Classic Fantasy",
            "Uses the same wilderness-travel behavior as B/X while preserving Old-School Essentials as its own source identity.",
            1,
            bx.Recipe,
            "Old-School Essentials is referenced only as preset metadata; the materialized procedure is the shared generic B/X recipe.");

        return
        [
            Preset(
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
                    tracksIntraHexProgress: true,
                    directionChangesCostProgress: true,
                    supportsDeliberateDoubleBack: true,
                    directionChangeProgressCostFactor: 1d / 6d,
                    resolutionHelpers: new ProcedureResolutionHelperProfile(
                        Travel: new TravelResolutionHelperProfile(new DiceRollFormula(2, 6, 3), 0.1d),
                        Navigation: new NavigationResolutionHelperProfile(new DiceRollFormula(1, 20)),
                        Encounter: new EncounterResolutionHelperProfile(
                            new DiceRollFormula(1, 8),
                            DiceRollResultSet.From([1]),
                            DiceRollResultSet.From([8]),
                            8))),
                attribution: "Procedure research based on The Alexandrian hexcrawl watch checklist."),
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
                    tracksIntraHexProgress: true,
                    directionChangesCostProgress: false,
                    supportsDeliberateDoubleBack: false),
                category: "Generic starting points"),
            Preset(
                "simple-hex-step",
                "Simple Hex Step",
                "Four-hour whole-cell travel with no partial progress, navigation, or encounter checks.",
                "simple-hex-step",
                "Simple Hex Step",
                ExecutableCore(
                    TimeSpan.FromHours(4),
                    TravelResolutionMode.HexSteps,
                    ActualDistanceResolutionMode.Fixed,
                    EncounterCheckCadence.None,
                    usesNavigationChecks: false,
                    usesPersistentVeer: false,
                    tracksIntraHexProgress: false,
                    directionChangesCostProgress: false,
                    supportsDeliberateDoubleBack: false),
                category: "Generic starting points"),

            bx,
            ose,

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
                attribution: "AD&D 2e procedure research; uncertain exact encounter cadence is not asserted by the preset."),

            Preset(
                Dnd35PresetKey,
                "D&D 3.5e",
                "Hourly overland travel with difficult terrain, getting lost, foraging, forced-march checks, and fatigue consequences.",
                "hourly-overland-travel",
                "Hourly overland travel",
                [
                    NativeTime(TimeSpan.FromHours(1)),
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.MovementBudgetMechanic,
                        ("budgetModel", "speed-derived-distance"), ("baseBudget", "1"), ("budgetUnit", "hour"), ("limitingScope", "party-limiting")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("adjustmentModel", "multiplier"), ("terrainAdjustments", "normal=1;difficult=0.5"), ("routeAdjustmentModel", "route-may-ignore-terrain"), ("weatherAdjustmentModel", "manual")),
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
                attribution: "D&D Free Rules (2024) travel terrain and extended travel procedure research."),

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
                attribution: "Pathfinder 2e GM Core Hexploration procedure research via Archives of Nethys."),

            Preset(
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
                disclaimer: "Some exact numerical and table-driven details were not available from a primary legally accessible rules source during Phase 3 and remain manual."),

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
                attribution: "Worlds Without Number SRD Wilderness Exploration and Overland Travel procedure research."),

            Preset(
                OneRing2ePresetKey,
                "The One Ring 2e",
                "Role-driven journeys with route planning, Guide progress, journey events, terrain influence, and fatigue.",
                "role-driven-journey",
                "Role-driven journey",
                [
                    Structural(GenericProcedureCatalog.MovementBudgetModule, GenericProcedureCatalog.JourneyProgressBudgetMechanic,
                        ("budgetModel", "journey-progress"), ("baseBudget", "1"), ("budgetUnit", "journey-leg"), ("limitingScope", "guide")),
                    Structural(GenericProcedureCatalog.TerrainMovementModule, GenericProcedureCatalog.TerrainMovementPolicyMechanic,
                        ("adjustmentModel", "terrain-difficulty"), ("terrainAdjustments", "open=1;hard=2;severe=3"), ("routeAdjustmentModel", "road-assistance"), ("weatherAdjustmentModel", "manual")),
                    Structural(GenericProcedureCatalog.PartyActivitiesModule, GenericProcedureCatalog.JourneyRoleActivityPolicyMechanic,
                        ("assignmentScope", "role"), ("activityBudgetModel", "journey-role"), ("activityKeys", "guide;hunt;lookout;scout"), ("roleKeys", "guide;hunter;lookout;scout")),
                    Structural(GenericProcedureCatalog.JourneyProcessModule, GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
                        ("stageModel", "route-then-events-then-arrival"),
                        ("stageKeys", "route;events;arrival"),
                        ("stageTransitionModel", "sequential"),
                        ("progressModel", "guide-marching-progress"),
                        ("progressKind", "numeric"),
                        ("progressUnit", "journey-progress"),
                        ("allowNegativeProgress", "false"),
                        ("completionModel", "final-stage-completion"),
                        ("roleDriven", "true"),
                        ("roleAssignmentModel", "current-at-resolution"),
                        ("intervalIntegrationModel", "none"),
                        ("blocksRelevantTravelWhileResolutionRequired", "false")),
                    Structural(GenericProcedureCatalog.JourneyEventsModule, GenericProcedureCatalog.ProgressTriggeredJourneyEventPolicyMechanic,
                        ("triggerModel", "guide-progress-test"),
                        ("triggerSources", "process-progress"),
                        ("linkMode", "process-linked"),
                        ("targetingModel", "travel-role"),
                        ("terrainInfluence", "difficulty-and-road"),
                        ("consequenceModel", "event-and-fatigue"),
                        ("requiresResolvedTrigger", "true"),
                        ("blocksRelevantTravelWhileResolutionRequired", "false")),
                    Structural(GenericProcedureCatalog.PersistentEffectsModule, GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
                        ("effectKinds", "fatigue"), ("accumulationModel", "journey-events"), ("recoveryModel", "safe-prolonged-rest"), ("scope", "participant"))
                ],
                attribution: "The One Ring 2e journey procedure research from publisher materials and secondary summaries.",
                disclaimer: "Exact event tables, distances, modifiers, and fatigue values are intentionally not encoded; resolved journey details remain explicit DM/provider input.")
        ];
    }

    private static CrawlProcedurePresetDefinition Preset(
        string presetKey,
        string displayName,
        string description,
        string procedureKey,
        string procedureName,
        IReadOnlyList<ProcedureModuleRecipe> modules,
        string? attribution = null,
        string? disclaimer = null,
        string category = "Familiar procedures") =>
        new(
            presetKey,
            displayName,
            description,
            1,
            new GenericProcedurePresetRecipe(procedureKey, procedureName, modules.ToArray())
            {
                TilingDsSymbol = CampaignProcedureSchema.CurrentHexTilingDsSymbol
            },
            attribution,
            disclaimer,
            category);

    private static IReadOnlyList<ProcedureModuleRecipe> ExecutableCore(
        TimeSpan interval,
        TravelResolutionMode travelResolution,
        ActualDistanceResolutionMode actualDistanceResolution,
        EncounterCheckCadence encounterCadence,
        bool usesNavigationChecks,
        bool usesPersistentVeer,
        bool tracksIntraHexProgress,
        bool directionChangesCostProgress,
        bool supportsDeliberateDoubleBack,
        double startingExitProgressFactor = 0.5d,
        double nearExitProgressFactor = 0.5d,
        double farExitProgressFactor = 1d,
        double backExitProgressFactor = 0.5d,
        double directionChangeProgressCostFactor = 0d,
        ProcedureResolutionHelperProfile? resolutionHelpers = null) =>
    [
        NativeTime(interval),
        Structural(GenericProcedureCatalog.MovementResolutionModule, GenericProcedureCatalog.MovementResolutionPolicyMechanic,
            ("travelResolution", travelResolution.ToString()),
            ("actualDistanceResolution", actualDistanceResolution.ToString()),
            ("tracksIntraHexProgress", Bool(tracksIntraHexProgress))),
        Structural(GenericProcedureCatalog.HexProgressModule, GenericProcedureCatalog.HexProgressPolicyMechanic,
            ("startingExitProgressFactor", Number(startingExitProgressFactor)),
            ("nearExitProgressFactor", Number(nearExitProgressFactor)),
            ("farExitProgressFactor", Number(farExitProgressFactor)),
            ("backExitProgressFactor", Number(backExitProgressFactor)),
            ("directionChangesCostProgress", Bool(directionChangesCostProgress)),
            ("directionChangeProgressCostFactor", Number(directionChangeProgressCostFactor)),
            ("supportsDeliberateDoubleBack", Bool(supportsDeliberateDoubleBack))),
        Structural(GenericProcedureCatalog.NavigationModule, GenericProcedureCatalog.NavigationCheckPolicyMechanic,
            ("usesNavigationChecks", Bool(usesNavigationChecks)),
            ("usesPersistentVeer", Bool(usesPersistentVeer))),
        NativeEncounterCadence(encounterCadence),
        new ProcedureModuleRecipe(
            GenericProcedureCatalog.ResolutionHelpersModule,
            GenericProcedureCatalog.DeterministicResolutionHelpersMechanic,
            1,
            HelperValues(resolutionHelpers))
    ];

    private static ProcedureModuleRecipe NativeTime(TimeSpan interval) =>
        Structural(
            GenericProcedureCatalog.TimeIntervalModule,
            GenericProcedureCatalog.FixedIntervalDurationMechanic,
            ("durationTicks", interval.Ticks.ToString(CultureInfo.InvariantCulture)));

    private static ProcedureModuleRecipe NativeEncounterCadence(EncounterCheckCadence cadence) =>
        Structural(
            GenericProcedureCatalog.EncounterCadenceModule,
            GenericProcedureCatalog.EncounterCheckCadenceMechanic,
            ("cadence", cadence.ToString()));

    private static ProcedureModuleRecipe Structural(
        string moduleKey,
        string mechanicKey,
        params (string Key, string Value)[] parameters) =>
        new(
            moduleKey,
            mechanicKey,
            1,
            parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, string> HelperValues(ProcedureResolutionHelperProfile? helpers)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["travel.enabled"] = Bool(helpers?.Travel is not null),
            ["navigation.enabled"] = Bool(helpers?.Navigation is not null),
            ["encounter.enabled"] = Bool(helpers?.Encounter is not null)
        };
        if (helpers?.Travel is { } travel)
        {
            AddRoll(values, "travel", travel.Roll);
            values["travel.distanceFactor"] = Number(travel.DistanceFactorPerRollPoint);
        }
        if (helpers?.Navigation is { } navigation)
        {
            AddRoll(values, "navigation", navigation.CheckRoll);
        }
        if (helpers?.Encounter is { } encounter)
        {
            AddRoll(values, "encounter", encounter.CheckRoll);
            values["encounter.wanderingResults"] = encounter.WanderingResults.Canonical;
            values["encounter.keyedLocationResults"] = encounter.KeyedLocationResults.Canonical;
            values["encounter.timingSlots"] = encounter.TimingSlots.ToString(CultureInfo.InvariantCulture);
        }
        return values;
    }

    private static void AddRoll(IDictionary<string, string> values, string prefix, DiceRollFormula roll)
    {
        values[$"{prefix}.diceCount"] = roll.DiceCount.ToString(CultureInfo.InvariantCulture);
        values[$"{prefix}.dieSides"] = roll.DieSides.ToString(CultureInfo.InvariantCulture);
        values[$"{prefix}.modifier"] = roll.Modifier.ToString(CultureInfo.InvariantCulture);
    }

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
