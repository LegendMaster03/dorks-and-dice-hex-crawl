using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public static class GenericProcedureCatalog
{
    public const string TimeIntervalModule = "time.interval";
    public const string MovementResolutionModule = "movement.resolution";
    public const string HexProgressModule = "movement.hex-progress";
    public const string NavigationModule = "navigation.check";
    public const string EncounterCadenceModule = "encounters.cadence";
    public const string ResolutionHelpersModule = "procedure.helpers";

    public const string MovementBudgetModule = "movement.budget";
    public const string TerrainMovementModule = "movement.terrain";
    public const string PartyActivitiesModule = "party.activities";
    public const string NavigationOutcomeModule = "navigation.outcome";
    public const string EncounterScheduleModule = "encounters.schedule";
    public const string ResourceConsumptionModule = "survival.resources";
    public const string ForagingModule = "exploration.foraging";
    public const string CampingModule = "survival.camping";
    public const string ForcedTravelModule = "time.forced-travel";
    public const string PersistentEffectsModule = "effects.expedition";
    public const string JourneyEventsModule = "journey.events";
    public const string JourneyProcessModule = "journey.process";

    public const string FixedIntervalDurationMechanic = "fixed-interval-duration";
    public const string MovementResolutionPolicyMechanic = "movement-resolution-policy";
    public const string HexProgressPolicyMechanic = "hex-progress-policy";
    public const string NavigationCheckPolicyMechanic = "navigation-check-policy";
    public const string EncounterCheckCadenceMechanic = "encounter-check-cadence";
    public const string DeterministicResolutionHelpersMechanic = "deterministic-resolution-helpers";

    public const string MovementBudgetMechanic = "movement-budget";
    public const string JourneyProgressBudgetMechanic = "journey-progress-budget";
    public const string TerrainMovementPolicyMechanic = "terrain-movement-policy";
    public const string ParticipantActivityPolicyMechanic = "participant-activity-policy";
    public const string JourneyRoleActivityPolicyMechanic = "journey-role-activity-policy";
    public const string NavigationOutcomePolicyMechanic = "navigation-outcome-policy";
    public const string EncounterSchedulePolicyMechanic = "encounter-schedule-policy";
    public const string ContextualEncounterSchedulePolicyMechanic = "contextual-encounter-schedule-policy";
    public const string ResourceConsumptionPolicyMechanic = "resource-consumption-policy";
    public const string ForagingPolicyMechanic = "foraging-policy";
    public const string ActivityForagingPolicyMechanic = "activity-foraging-policy";
    public const string CampingPolicyMechanic = "camping-policy";
    public const string ActivityCampingPolicyMechanic = "activity-camping-policy";
    public const string ForcedTravelPolicyMechanic = "forced-travel-policy";
    public const string ProgressiveExpeditionEffectMechanic = "progressive-expedition-effect";
    public const string JourneyEventPolicyMechanic = "journey-event-policy";
    public const string ProgressTriggeredJourneyEventPolicyMechanic = "progress-triggered-journey-event-policy";
    public const string MultiStageExpeditionProcessMechanic = "multi-stage-expedition-process";

    private const string DeclarativeContractHandler = "procedure.declarative-contract";

    public static IReadOnlyList<ProcedureModuleDefinition> Modules { get; } =
    [
        Module(
            TimeIntervalModule,
            "Time",
            "Travel interval",
            "Defines the duration of one expedition travel interval.",
            "interval-start",
            [],
            ["time.interval-duration"],
            [],
            [],
            [FixedIntervalDurationMechanic],
            Required("durationTicks", "integer", "Duration of one travel interval in .NET ticks.")),
        Module(
            MovementResolutionModule,
            "Movement",
            "Movement resolution",
            "Defines whether travel advances by resolved physical distance or whole hex steps.",
            "movement",
            ["time.interval-duration"],
            ["movement.resolution-mode"],
            [TimeIntervalModule],
            [],
            [MovementResolutionPolicyMechanic],
            RequiredMany(
                ("travelResolution", "enum", "Travel resolution mode."),
                ("actualDistanceResolution", "enum", "Actual-distance resolution mode."),
                ("tracksIntraHexProgress", "boolean", "Whether physical movement tracks progress inside a hex."))),
        Module(
            HexProgressModule,
            "Movement",
            "Hex progress",
            "Defines entry, exit, direction-change, and deliberate-double-back progress behavior.",
            "movement",
            ["movement.resolution-mode"],
            ["movement.hex-progress"],
            [MovementResolutionModule],
            [],
            [HexProgressPolicyMechanic],
            RequiredMany(
                ("startingExitProgressFactor", "number", "Starting exit progress factor."),
                ("nearExitProgressFactor", "number", "Near exit progress factor."),
                ("farExitProgressFactor", "number", "Far exit progress factor."),
                ("backExitProgressFactor", "number", "Back exit progress factor."),
                ("directionChangesCostProgress", "boolean", "Whether changing direction costs progress."),
                ("directionChangeProgressCostFactor", "number", "Direction-change progress cost factor."),
                ("supportsDeliberateDoubleBack", "boolean", "Whether deliberate double-back movement is supported."))),
        Module(
            NavigationModule,
            "Navigation",
            "Navigation checks",
            "Defines whether navigation checks are required and whether navigation failure persists as veer state.",
            "navigation",
            ["movement.resolution-mode"],
            ["navigation.policy"],
            [MovementResolutionModule],
            [],
            [NavigationCheckPolicyMechanic],
            RequiredMany(
                ("usesNavigationChecks", "boolean", "Whether navigation checks are required."),
                ("usesPersistentVeer", "boolean", "Whether failed navigation persists as veer."))),
        Module(
            EncounterCadenceModule,
            "Encounters",
            "Encounter check cadence",
            "Defines when encounter checks are required during expedition procedure.",
            "encounter",
            ["time.interval-duration"],
            ["encounter.check-cadence"],
            [TimeIntervalModule],
            [],
            [EncounterCheckCadenceMechanic],
            Required("cadence", "enum", "Encounter check cadence.")),
        Module(
            ResolutionHelpersModule,
            "Procedure",
            "Resolution helpers",
            "Defines optional deterministic dice helpers used to produce travel, navigation, and encounter inputs.",
            "input-resolution",
            ["movement.resolution-mode", "navigation.policy", "encounter.check-cadence"],
            ["procedure.helper-configuration"],
            [],
            [MovementResolutionModule, NavigationModule, EncounterCadenceModule],
            [DeterministicResolutionHelpersMechanic],
            new Dictionary<string, ProcedureParameterDefinition>()),

        Module(
            MovementBudgetModule,
            "Movement",
            "Movement budget",
            "Hosts a selected travel-budget behavior measured in distance, activities, points, journey progress, or another explicit unit.",
            "movement-planning",
            [],
            ["movement.budget"],
            [],
            [TimeIntervalModule],
            [MovementBudgetMechanic, JourneyProgressBudgetMechanic],
            RequiredMany(
                ("budgetModel", "enum", "How the travel budget is calculated."),
                ("baseBudget", "number", "Base amount of travel budget."),
                ("budgetUnit", "enum", "Unit used by the travel budget."),
                ("limitingScope", "enum", "Whether the budget is party-wide, participant-limited, role-driven, or externally resolved.")),
            phase: "3"),
        Module(
            TerrainMovementModule,
            "Movement",
            "Terrain and route movement",
            "Hosts a selected relationship between terrain tags and model-specific movement adjustments, including numeric costs, multipliers, activity costs, pace limits, or difficulty states.",
            "movement-planning",
            [],
            ["movement.terrain-adjustment"],
            [],
            [MovementBudgetModule],
            [TerrainMovementPolicyMechanic],
            RequiredMany(
                ("adjustmentModel", "enum", "Semantic relationship represented by the terrain mapping."),
                ("terrainAdjustments", "map<string>", "Terrain-tag to model-specific adjustment value, state, or limit."),
                ("routeAdjustmentModel", "enum", "How routes alter the selected terrain relationship."),
                ("weatherAdjustmentModel", "enum", "How weather alters the selected terrain relationship.")),
            phase: "3"),
        Module(
            PartyActivitiesModule,
            "Party procedure",
            "Participant activities",
            "Hosts a selected party-wide, per-participant, or role-based activity assignment behavior.",
            "activity-assignment",
            [],
            ["participant.activity-state"],
            [],
            [MovementBudgetModule, TimeIntervalModule],
            [ParticipantActivityPolicyMechanic, JourneyRoleActivityPolicyMechanic],
            RequiredMany(
                ("assignmentScope", "enum", "Party, participant, or role assignment scope."),
                ("activityBudgetModel", "enum", "How activity capacity is determined."),
                ("activityKeys", "key-list", "Generic activities available to participants."),
                ("roleKeys", "key-list", "Generic travel roles available to participants.")),
            phase: "3"),
        Module(
            NavigationOutcomeModule,
            "Navigation",
            "Navigation outcome",
            "Hosts navigation failure-state, directional-error, recognition, and reorientation behavior independently of the check engine.",
            "navigation",
            [],
            ["navigation.outcome-state"],
            [],
            [NavigationModule, PartyActivitiesModule],
            [NavigationOutcomePolicyMechanic],
            RequiredMany(
                ("checkTriggerModel", "enum", "When navigation resolution is required."),
                ("failureStateModel", "enum", "State created by a failed navigation resolution."),
                ("directionalErrorModel", "enum", "How an off-course direction is established."),
                ("recognitionModel", "enum", "How the party recognizes that it is off course."),
                ("reorientationModel", "enum", "How the party returns to an intended route.")),
            phase: "3"),
        Module(
            EncounterScheduleModule,
            "Encounters",
            "Encounter schedule",
            "Hosts a selected encounter timing behavior beyond the core none/per-watch/per-day cadence.",
            "encounter",
            [],
            ["encounter.schedule"],
            [],
            [TimeIntervalModule, EncounterCadenceModule],
            [EncounterSchedulePolicyMechanic, ContextualEncounterSchedulePolicyMechanic],
            RequiredMany(
                ("scheduleModel", "enum", "Functional encounter schedule model."),
                ("travelChecksPerInterval", "number", "Number of travel checks represented by one interval."),
                ("campCheck", "boolean", "Whether camping has a separate encounter check."),
                ("terrainProbabilityModel", "enum", "Whether encounter probability varies with environment.")),
            phase: "3"),
        Module(
            ResourceConsumptionModule,
            "Survival/resources",
            "Resource consumption",
            "Hosts expedition resource kinds, inventory style, and consumption cadence without owning a full resource inventory.",
            "interval-completion",
            [],
            ["resource.consumed"],
            [],
            [TimeIntervalModule, PartyActivitiesModule],
            [ResourceConsumptionPolicyMechanic],
            RequiredMany(
                ("resourceKinds", "key-list", "Generic expedition resources consumed by the procedure."),
                ("inventoryModel", "enum", "Counted, abstract, supply-die, or external/manual inventory model."),
                ("consumptionModel", "enum", "How consumption is determined."),
                ("consumptionInterval", "enum", "When consumption is applied.")),
            phase: "3"),
        Module(
            ForagingModule,
            "Exploration",
            "Foraging",
            "Hosts forage resolution and its time, activity, and travel tradeoff without implementing a resource engine.",
            "activity-resolution",
            [],
            ["resource.gathered"],
            [],
            [PartyActivitiesModule, TerrainMovementModule],
            [ForagingPolicyMechanic, ActivityForagingPolicyMechanic],
            RequiredMany(
                ("resolutionModel", "enum", "How a forage attempt is resolved."),
                ("timeCost", "number", "Amount of procedure time or activity budget consumed."),
                ("timeUnit", "enum", "Unit used by the forage time cost."),
                ("movementTradeoff", "enum", "How foraging affects travel progress.")),
            phase: "3"),
        Module(
            CampingModule,
            "Survival/resources",
            "Camping",
            "Hosts camp setup, time cost, and watch requirements without implementing a complete camp subsystem.",
            "rest",
            [],
            ["camp.state"],
            [],
            [TimeIntervalModule, PartyActivitiesModule],
            [CampingPolicyMechanic, ActivityCampingPolicyMechanic],
            RequiredMany(
                ("resolutionModel", "enum", "How camp setup is resolved."),
                ("timeCost", "number", "Amount of procedure time or activity budget consumed."),
                ("timeUnit", "enum", "Unit used by the camp time cost."),
                ("watchModel", "enum", "How camp watch responsibility is represented.")),
            phase: "3"),
        Module(
            ForcedTravelModule,
            "Time",
            "Forced travel",
            "Hosts the threshold, check model, and transient consequence of traveling beyond a normal limit.",
            "interval-completion",
            [],
            ["effects.transient"],
            [],
            [TimeIntervalModule, MovementBudgetModule],
            [ForcedTravelPolicyMechanic],
            RequiredMany(
                ("normalTravelLimit", "number", "Travel amount before forced-travel resolution begins."),
                ("limitUnit", "enum", "Unit used for the normal travel limit."),
                ("checkModel", "enum", "How continued travel is tested."),
                ("failureConsequence", "enum", "Generic consequence produced by failure.")),
            phase: "3"),
        Module(
            PersistentEffectsModule,
            "Environment/effects",
            "Persistent expedition effects",
            "Hosts persistent effect accumulation and recovery from transient consequences without implementing the later generalized effect engine.",
            "interval-completion",
            [],
            ["effects.persistent"],
            [],
            [ForcedTravelModule, JourneyEventsModule],
            [ProgressiveExpeditionEffectMechanic],
            RequiredMany(
                ("effectKinds", "key-list", "Generic effect families used by the procedure."),
                ("accumulationModel", "enum", "How repeated consequences accumulate."),
                ("recoveryModel", "enum", "How persistent effects are reduced or cleared."),
                ("scope", "enum", "Participant, party, mount, vehicle, or expedition scope.")),
            phase: "3"),
        Module(
            JourneyEventsModule,
            "Journey processes",
            "Journey events",
            "Hosts selected event-trigger, role-targeting, terrain-influence, and transient-consequence behavior for higher-level journeys.",
            "journey-event",
            [],
            ["journey.event", "effects.transient"],
            [],
            [PartyActivitiesModule, TerrainMovementModule, JourneyProcessModule],
            [JourneyEventPolicyMechanic, ProgressTriggeredJourneyEventPolicyMechanic],
            RequiredMany(
                ("triggerModel", "enum", "How journey events are placed or triggered."),
                ("targetingModel", "enum", "How an event selects a participant or role."),
                ("terrainInfluence", "enum", "How environment changes event resolution."),
                ("consequenceModel", "enum", "Generic form of event consequences.")),
            phase: "3"),
        Module(
            JourneyProcessModule,
            "Journey processes",
            "Multi-stage journey process",
            "Hosts stage, progress, completion, and role participation for a higher-level journey without requiring a repeating travel interval.",
            "journey",
            [],
            ["journey.progress"],
            [],
            [PartyActivitiesModule, TerrainMovementModule, JourneyEventsModule],
            [MultiStageExpeditionProcessMechanic],
            RequiredMany(
                ("stageModel", "enum", "How journey stages are divided."),
                ("progressModel", "enum", "How progress through the journey is measured."),
                ("completionModel", "enum", "How the process determines arrival or completion."),
                ("roleDriven", "boolean", "Whether participant roles drive stage or event resolution.")),
            phase: "3")
    ];

    public static IReadOnlyList<MechanicDefinition> Mechanics { get; } =
    [
        Mechanic(
            FixedIntervalDurationMechanic,
            "Fixed interval duration",
            "Uses one configured duration for every travel interval.",
            [],
            ["time.interval-duration"],
            Required("durationTicks", "integer", "Duration in ticks."),
            "crawl-profile.watch-length",
            ProcedureAutomationLevel.Automatic),
        Mechanic(
            MovementResolutionPolicyMechanic,
            "Movement resolution policy",
            "Selects continuous-distance or whole-hex movement and how actual distance is resolved.",
            ["time.interval-duration"],
            ["movement.resolution-mode"],
            RequiredMany(
                ("travelResolution", "enum", "Travel resolution mode."),
                ("actualDistanceResolution", "enum", "Actual-distance resolution mode."),
                ("tracksIntraHexProgress", "boolean", "Track progress within a hex.")),
            "crawl-profile.movement-resolution",
            ProcedureAutomationLevel.Automatic),
        Mechanic(
            HexProgressPolicyMechanic,
            "Hex progress policy",
            "Configures generic progress costs and exit factors for spatial hex traversal.",
            ["movement.resolution-mode"],
            ["movement.hex-progress"],
            RequiredMany(
                ("startingExitProgressFactor", "number", "Starting exit factor."),
                ("nearExitProgressFactor", "number", "Near exit factor."),
                ("farExitProgressFactor", "number", "Far exit factor."),
                ("backExitProgressFactor", "number", "Back exit factor."),
                ("directionChangesCostProgress", "boolean", "Direction changes cost progress."),
                ("directionChangeProgressCostFactor", "number", "Direction-change cost factor."),
                ("supportsDeliberateDoubleBack", "boolean", "Deliberate double-back support.")),
            "crawl-profile.hex-progress",
            ProcedureAutomationLevel.Automatic),
        Mechanic(
            NavigationCheckPolicyMechanic,
            "Navigation check policy",
            "Configures generic navigation-check and persistent directional-error behavior.",
            ["movement.resolution-mode"],
            ["navigation.policy"],
            RequiredMany(
                ("usesNavigationChecks", "boolean", "Navigation checks required."),
                ("usesPersistentVeer", "boolean", "Navigation failure persists as veer.")),
            "crawl-profile.navigation",
            ProcedureAutomationLevel.Assisted),
        Mechanic(
            EncounterCheckCadenceMechanic,
            "Encounter check cadence",
            "Configures when encounter checks are due.",
            ["time.interval-duration"],
            ["encounter.check-cadence"],
            Required("cadence", "enum", "Encounter cadence."),
            "crawl-profile.encounter-cadence",
            ProcedureAutomationLevel.Assisted),
        Mechanic(
            DeterministicResolutionHelpersMechanic,
            "Deterministic resolution helpers",
            "Configures optional dice helpers that resolve runtime inputs without becoming runtime dependencies.",
            ["movement.resolution-mode", "navigation.policy", "encounter.check-cadence"],
            ["procedure.helper-configuration"],
            new Dictionary<string, ProcedureParameterDefinition>(),
            "crawl-profile.resolution-helpers",
            ProcedureAutomationLevel.Assisted),

        Mechanic(
            MovementBudgetMechanic,
            "Interval movement budget",
            "Represents a travel budget that is explicitly calculated for a selected travel interval.",
            ["time.interval-duration"],
            ["movement.budget"],
            RequiredMany(
                ("budgetModel", "enum", "Travel budget calculation model."),
                ("baseBudget", "number", "Base travel budget."),
                ("budgetUnit", "enum", "Travel budget unit."),
                ("limitingScope", "enum", "Scope that limits the budget.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["movement", "phase-3"]),
        Mechanic(
            JourneyProgressBudgetMechanic,
            "Journey progress budget",
            "Represents route or journey-leg progress that is not derived from a repeating travel interval.",
            [],
            ["movement.budget"],
            RequiredMany(
                ("budgetModel", "enum", "Journey progress calculation model."),
                ("baseBudget", "number", "Base journey progress budget."),
                ("budgetUnit", "enum", "Journey progress unit."),
                ("limitingScope", "enum", "Scope or role that limits progress.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["movement", "journey", "phase-3"]),
        Mechanic(
            TerrainMovementPolicyMechanic,
            "Terrain movement policy",
            "Maps terrain tags to model-specific movement adjustments such as multipliers, costs, activity costs, pace limits, or difficulty states.",
            ["movement.budget"],
            ["movement.terrain-adjustment"],
            RequiredMany(
                ("adjustmentModel", "enum", "Terrain adjustment relationship."),
                ("terrainAdjustments", "map<string>", "Terrain-tag to model-specific adjustment value, state, or limit."),
                ("routeAdjustmentModel", "enum", "Route adjustment model."),
                ("weatherAdjustmentModel", "enum", "Weather adjustment model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["movement", "environment", "phase-3"]),
        Mechanic(
            ParticipantActivityPolicyMechanic,
            "Budget-backed participant activity policy",
            "Represents participant assignments whose activity capacity is derived from the selected movement or travel budget.",
            ["movement.budget"],
            ["participant.activity-state"],
            RequiredMany(
                ("assignmentScope", "enum", "Assignment scope."),
                ("activityBudgetModel", "enum", "Activity capacity model."),
                ("activityKeys", "key-list", "Available generic activity keys."),
                ("roleKeys", "key-list", "Available generic role keys.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Manual,
            ["party", "activity", "phase-3"]),
        Mechanic(
            JourneyRoleActivityPolicyMechanic,
            "Journey role activity policy",
            "Represents role assignments that organize a higher-level journey without deriving capacity from a travel interval or movement budget.",
            [],
            ["participant.activity-state"],
            RequiredMany(
                ("assignmentScope", "enum", "Assignment scope."),
                ("activityBudgetModel", "enum", "Role-capacity model."),
                ("activityKeys", "key-list", "Available generic journey duties."),
                ("roleKeys", "key-list", "Available generic journey roles.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Manual,
            ["party", "activity", "journey", "phase-3"]),
        Mechanic(
            NavigationOutcomePolicyMechanic,
            "Navigation outcome policy",
            "Represents lost, directional-error, recognition, and reorientation behavior from an independently resolved navigation check result.",
            ["navigation.check-result"],
            ["navigation.outcome-state"],
            RequiredMany(
                ("checkTriggerModel", "enum", "Navigation check trigger model."),
                ("failureStateModel", "enum", "Failure state model."),
                ("directionalErrorModel", "enum", "Directional error model."),
                ("recognitionModel", "enum", "Recognition model."),
                ("reorientationModel", "enum", "Reorientation model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["navigation", "phase-3"],
            inputRequirements:
            [
                new ProcedureInputRequirement(
                    "navigation.check-result",
                    ProcedureInputSource.SelectedModule
                    | ProcedureInputSource.Dm
                    | ProcedureInputSource.OptionalProvider
                    | ProcedureInputSource.ExternalState)
            ]),
        Mechanic(
            EncounterSchedulePolicyMechanic,
            "Cadence-backed encounter schedule policy",
            "Represents a richer encounter schedule that refines a selected core cadence within a selected interval.",
            ["time.interval-duration", "encounter.check-cadence"],
            ["encounter.schedule"],
            RequiredMany(
                ("scheduleModel", "enum", "Encounter schedule model."),
                ("travelChecksPerInterval", "number", "Travel checks represented per interval."),
                ("campCheck", "boolean", "Separate camp check."),
                ("terrainProbabilityModel", "enum", "Environment-dependent probability model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["encounter", "phase-3"]),
        Mechanic(
            ContextualEncounterSchedulePolicyMechanic,
            "Contextual encounter schedule policy",
            "Represents a contextual travel/camp encounter schedule that is not derived from a selected core encounter cadence.",
            ["time.interval-duration"],
            ["encounter.schedule"],
            RequiredMany(
                ("scheduleModel", "enum", "Encounter schedule model."),
                ("travelChecksPerInterval", "number", "Travel checks represented per interval."),
                ("campCheck", "boolean", "Separate camp check."),
                ("terrainProbabilityModel", "enum", "Environment-dependent probability model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["encounter", "phase-3"]),
        Mechanic(
            ResourceConsumptionPolicyMechanic,
            "Interval resource consumption policy",
            "Represents resource kinds, inventory style, and consumption timing tied to a selected interval while the full resource engine remains deferred.",
            ["time.interval-duration"],
            ["resource.consumed"],
            RequiredMany(
                ("resourceKinds", "key-list", "Resource kinds."),
                ("inventoryModel", "enum", "Inventory model."),
                ("consumptionModel", "enum", "Consumption model."),
                ("consumptionInterval", "enum", "Consumption interval.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Manual,
            ["resource", "survival", "phase-3"]),
        Mechanic(
            ForagingPolicyMechanic,
            "Travel-tradeoff foraging policy",
            "Represents forage resolution whose travel tradeoff depends on the selected terrain-adjusted movement behavior rather than an activity assignment.",
            ["movement.terrain-adjustment"],
            ["resource.gathered"],
            RequiredMany(
                ("resolutionModel", "enum", "Forage resolution model."),
                ("timeCost", "number", "Forage time cost."),
                ("timeUnit", "enum", "Forage time unit."),
                ("movementTradeoff", "enum", "Travel tradeoff.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["exploration", "resource", "phase-3"]),
        Mechanic(
            ActivityForagingPolicyMechanic,
            "Activity-based foraging policy",
            "Represents forage resolution that consumes a selected participant activity and is modified by terrain-adjusted movement context.",
            ["participant.activity-state", "movement.terrain-adjustment"],
            ["resource.gathered"],
            RequiredMany(
                ("resolutionModel", "enum", "Forage resolution model."),
                ("timeCost", "number", "Forage time or activity cost."),
                ("timeUnit", "enum", "Forage time or activity unit."),
                ("movementTradeoff", "enum", "Travel tradeoff.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["exploration", "resource", "activity", "phase-3"]),
        Mechanic(
            CampingPolicyMechanic,
            "Interval camping policy",
            "Represents camp setup and watch policy anchored to the selected expedition interval without requiring an activity-assignment subsystem.",
            ["time.interval-duration"],
            ["camp.state"],
            RequiredMany(
                ("resolutionModel", "enum", "Camp resolution model."),
                ("timeCost", "number", "Camp time cost."),
                ("timeUnit", "enum", "Camp time unit."),
                ("watchModel", "enum", "Camp watch model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["survival", "rest", "phase-3"]),
        Mechanic(
            ActivityCampingPolicyMechanic,
            "Activity-based camping policy",
            "Represents camp setup that consumes a selected participant activity without requiring a separate interval input.",
            ["participant.activity-state"],
            ["camp.state"],
            RequiredMany(
                ("resolutionModel", "enum", "Camp resolution model."),
                ("timeCost", "number", "Camp activity cost."),
                ("timeUnit", "enum", "Camp activity unit."),
                ("watchModel", "enum", "Camp watch model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["survival", "rest", "activity", "phase-3"]),
        Mechanic(
            ForcedTravelPolicyMechanic,
            "Forced travel policy",
            "Represents travel beyond a normal limit and the transient consequence of a failed continuation check.",
            ["time.interval-duration", "movement.budget"],
            ["effects.transient"],
            RequiredMany(
                ("normalTravelLimit", "number", "Normal travel limit."),
                ("limitUnit", "enum", "Travel limit unit."),
                ("checkModel", "enum", "Forced-travel check model."),
                ("failureConsequence", "enum", "Generic failure consequence.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Assisted,
            ["time", "effect", "phase-3"]),
        Mechanic(
            ProgressiveExpeditionEffectMechanic,
            "Progressive expedition effect",
            "Represents persistent effect families that accumulate from transient expedition consequences and recover according to procedure-defined rules.",
            ["effects.transient"],
            ["effects.persistent"],
            RequiredMany(
                ("effectKinds", "key-list", "Effect families."),
                ("accumulationModel", "enum", "Accumulation model."),
                ("recoveryModel", "enum", "Recovery model."),
                ("scope", "enum", "Effect scope.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Manual,
            ["effect", "phase-3"]),
        Mechanic(
            JourneyEventPolicyMechanic,
            "Journey event policy",
            "Represents event placement, role targeting, environmental influence, and transient consequences for event schedules that do not depend on journey-progress output.",
            ["participant.activity-state", "movement.terrain-adjustment"],
            ["journey.event", "effects.transient"],
            RequiredMany(
                ("triggerModel", "enum", "Journey event trigger model."),
                ("targetingModel", "enum", "Journey event target model."),
                ("terrainInfluence", "enum", "Terrain influence model."),
                ("consequenceModel", "enum", "Event consequence model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Manual,
            ["journey", "event", "phase-3"]),
        Mechanic(
            ProgressTriggeredJourneyEventPolicyMechanic,
            "Progress-triggered journey event policy",
            "Represents events placed from journey progress, targeted through journey roles, modified by terrain, and capable of producing transient consequences.",
            ["journey.progress", "participant.activity-state", "movement.terrain-adjustment"],
            ["journey.event", "effects.transient"],
            RequiredMany(
                ("triggerModel", "enum", "Journey event trigger model."),
                ("targetingModel", "enum", "Journey event target model."),
                ("terrainInfluence", "enum", "Terrain influence model."),
                ("consequenceModel", "enum", "Event consequence model.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Manual,
            ["journey", "event", "phase-3"]),
        Mechanic(
            MultiStageExpeditionProcessMechanic,
            "Multi-stage expedition process",
            "Represents role-driven journey progress and completion using the selected role assignments and terrain relationship while the later process engine remains deferred.",
            ["participant.activity-state", "movement.terrain-adjustment"],
            ["journey.progress"],
            RequiredMany(
                ("stageModel", "enum", "Journey stage model."),
                ("progressModel", "enum", "Journey progress model."),
                ("completionModel", "enum", "Journey completion model."),
                ("roleDriven", "boolean", "Whether roles drive journey resolution.")),
            DeclarativeContractHandler,
            ProcedureAutomationLevel.Manual,
            ["journey", "process", "phase-3"])
    ];

    public static ProcedureModuleDefinition ResolveModule(string key) =>
        Modules.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Unknown generic procedure module '{key}'.");

    public static MechanicDefinition ResolveMechanic(string key, int? version = null)
    {
        var mechanic = Mechanics.FirstOrDefault(value =>
            string.Equals(value.Key, key, StringComparison.Ordinal)
            && (!version.HasValue || value.Version == version));
        return mechanic ?? throw new InvalidOperationException(
            version.HasValue
                ? $"Unknown generic mechanic '{key}' version {version}."
                : $"Unknown generic mechanic '{key}'.");
    }

    private static ProcedureModuleDefinition Module(
        string key,
        string category,
        string displayName,
        string purpose,
        string stage,
        IReadOnlyList<string> reads,
        IReadOnlyList<string> produces,
        IReadOnlyList<string> required,
        IReadOnlyList<string> optional,
        IReadOnlyList<string> compatible,
        IReadOnlyDictionary<string, ProcedureParameterDefinition> schema,
        string phase = "1") =>
        new(
            key,
            category,
            displayName,
            purpose,
            stage,
            reads,
            produces,
            required,
            optional,
            compatible,
            schema,
            new Dictionary<string, string> { ["phase"] = phase });

    private static MechanicDefinition Mechanic(
        string key,
        string displayName,
        string description,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        IReadOnlyDictionary<string, ProcedureParameterDefinition> schema,
        string handler,
        ProcedureAutomationLevel automation,
        IReadOnlyList<string>? compatibilityTags = null,
        int version = 1,
        IReadOnlyList<ProcedureInputRequirement>? inputRequirements = null) =>
        new(
            key,
            displayName,
            description,
            inputs,
            outputs,
            schema,
            handler,
            compatibilityTags ?? [],
            automation,
            version,
            inputRequirements);

    private static IReadOnlyDictionary<string, ProcedureParameterDefinition> Required(
        string key,
        string type,
        string description) =>
        new Dictionary<string, ProcedureParameterDefinition>
        {
            [key] = new(type, true, description)
        };

    private static IReadOnlyDictionary<string, ProcedureParameterDefinition> RequiredMany(
        params (string Key, string Type, string Description)[] values) =>
        values.ToDictionary(
            value => value.Key,
            value => new ProcedureParameterDefinition(value.Type, true, value.Description),
            StringComparer.Ordinal);
}
