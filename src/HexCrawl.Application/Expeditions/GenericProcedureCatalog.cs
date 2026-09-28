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
    public const string TerrainMovementPolicyMechanic = "terrain-movement-policy";
    public const string ParticipantActivityPolicyMechanic = "participant-activity-policy";
    public const string NavigationOutcomePolicyMechanic = "navigation-outcome-policy";
    public const string EncounterSchedulePolicyMechanic = "encounter-schedule-policy";
    public const string ResourceConsumptionPolicyMechanic = "resource-consumption-policy";
    public const string ForagingPolicyMechanic = "foraging-policy";
    public const string CampingPolicyMechanic = "camping-policy";
    public const string ForcedTravelPolicyMechanic = "forced-travel-policy";
    public const string ProgressiveExpeditionEffectMechanic = "progressive-expedition-effect";
    public const string JourneyEventPolicyMechanic = "journey-event-policy";
    public const string MultiStageExpeditionProcessMechanic = "multi-stage-expedition-process";

    private const string DeclarativeContractHandler = "procedure.declarative-contract";

    private const ProcedureInputSource ManualOrExternal =
        ProcedureInputSource.SelectedModule | ProcedureInputSource.Dm | ProcedureInputSource.ExternalState;

    private const ProcedureInputSource ManualProviderOrExternal =
        ProcedureInputSource.SelectedModule | ProcedureInputSource.Dm | ProcedureInputSource.OptionalProvider | ProcedureInputSource.ExternalState;

    public static IReadOnlyList<ProcedureModuleDefinition> Modules { get; } =
    [
        Module(
            TimeIntervalModule,
            "Time",
            "Travel interval",
            "Defines the duration of one expedition travel interval.",
            "interval-start",
            [], ["time.interval-duration"], [], [], [FixedIntervalDurationMechanic],
            Required("durationTicks", "integer", "Duration of one travel interval in .NET ticks.")),
        Module(
            MovementResolutionModule,
            "Movement",
            "Movement resolution",
            "Defines whether travel advances by resolved physical distance or whole hex steps.",
            "movement",
            ["time.interval-duration"], ["movement.resolution-mode"], [TimeIntervalModule], [], [MovementResolutionPolicyMechanic],
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
            ["movement.resolution-mode"], ["movement.hex-progress"], [MovementResolutionModule], [], [HexProgressPolicyMechanic],
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
            ["movement.resolution-mode"], ["navigation.policy"], [MovementResolutionModule], [], [NavigationCheckPolicyMechanic],
            RequiredMany(
                ("usesNavigationChecks", "boolean", "Whether navigation checks are required."),
                ("usesPersistentVeer", "boolean", "Whether failed navigation persists as veer."))),
        Module(
            EncounterCadenceModule,
            "Encounters",
            "Encounter check cadence",
            "Defines when encounter checks are required during expedition procedure.",
            "encounter",
            ["time.interval-duration"], ["encounter.check-cadence"], [TimeIntervalModule], [], [EncounterCheckCadenceMechanic],
            Required("cadence", "enum", "Encounter check cadence.")),
        Module(
            ResolutionHelpersModule,
            "Procedure",
            "Resolution helpers",
            "Defines optional deterministic dice helpers used to produce travel, navigation, and encounter inputs.",
            "input-resolution",
            ["movement.resolution-mode", "navigation.policy", "encounter.check-cadence"], ["procedure.helper-configuration"], [],
            [MovementResolutionModule, NavigationModule, EncounterCadenceModule], [DeterministicResolutionHelpersMechanic],
            new Dictionary<string, ProcedureParameterDefinition>()),

        Module(
            MovementBudgetModule,
            "Movement",
            "Movement budget",
            "Represents a generic travel budget measured in distance, activities, points, or another explicit unit.",
            "movement-planning",
            ["time.interval-duration"], ["movement.budget"], [], [TimeIntervalModule], [MovementBudgetMechanic],
            RequiredMany(
                ("budgetModel", "enum", "How the travel budget is calculated."),
                ("baseBudget", "number", "Base amount of travel budget."),
                ("budgetUnit", "enum", "Unit used by the travel budget."),
                ("limitingScope", "enum", "Whether the budget is party-wide, participant-limited, or externally resolved.")),
            phase: "3"),
        Module(
            TerrainMovementModule,
            "Movement",
            "Terrain and route movement",
            "Represents how terrain, routes, and weather alter a generic movement budget.",
            "movement-planning",
            ["movement.budget"], ["movement.terrain-adjustment"], [MovementBudgetModule], [], [TerrainMovementPolicyMechanic],
            RequiredMany(
                ("costModel", "enum", "Functional terrain cost model."),
                ("terrainCosts", "map<number>", "Canonical terrain-tag to cost or multiplier map."),
                ("routeAdjustmentModel", "enum", "How routes alter terrain cost."),
                ("weatherAdjustmentModel", "enum", "How weather alters terrain cost.")),
            phase: "3"),
        Module(
            PartyActivitiesModule,
            "Party procedure",
            "Participant activities",
            "Represents party-wide, per-participant, or role-based activities during a travel interval or journey stage.",
            "activity-assignment",
            ["time.interval-duration", "movement.budget"], ["participant.activity-state"], [], [TimeIntervalModule, MovementBudgetModule],
            [ParticipantActivityPolicyMechanic],
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
            "Represents failure state, directional error, recognition, and reorientation behavior independently of the check itself.",
            "navigation",
            ["navigation.policy", "participant.activity-state"], ["navigation.outcome-state"], [], [NavigationModule, PartyActivitiesModule],
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
            "Represents encounter timing beyond the core none/per-watch/per-day cadence, including camp and terrain-sensitive schedules.",
            "encounter",
            ["time.interval-duration", "encounter.check-cadence"], ["encounter.schedule"], [], [TimeIntervalModule, EncounterCadenceModule],
            [EncounterSchedulePolicyMechanic],
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
            "Represents expedition resource kinds, inventory style, and consumption cadence without owning a full resource inventory.",
            "interval-completion",
            ["time.interval-duration", "participant.activity-state"], ["resource.consumed"], [], [TimeIntervalModule, PartyActivitiesModule],
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
            "Represents the time and movement tradeoff for attempting to gather expedition resources.",
            "activity-resolution",
            ["participant.activity-state", "movement.terrain-adjustment"], ["resource.gathered"], [], [PartyActivitiesModule, TerrainMovementModule],
            [ForagingPolicyMechanic],
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
            "Represents camp setup, time cost, and watch requirements without implementing a complete camp subsystem.",
            "rest",
            ["time.interval-duration", "participant.activity-state"], ["camp.state"], [], [TimeIntervalModule, PartyActivitiesModule],
            [CampingPolicyMechanic],
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
            "Represents the threshold, check model, and consequence of traveling beyond a normal limit.",
            "interval-completion",
            ["time.interval-duration", "movement.budget"], ["effects.transient"], [], [TimeIntervalModule, MovementBudgetModule],
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
            "Represents named generic effect families, accumulation, scope, and recovery without implementing the later full effect engine.",
            "interval-completion",
            ["effects.transient", "resource.consumed"], ["effects.persistent"], [], [ForcedTravelModule, ResourceConsumptionModule],
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
            "Represents event triggers, role targeting, terrain influence, and consequence shape for higher-level journeys.",
            "journey-event",
            ["participant.activity-state", "movement.terrain-adjustment", "effects.persistent"], ["journey.event"], [], [PartyActivitiesModule, TerrainMovementModule, PersistentEffectsModule],
            [JourneyEventPolicyMechanic],
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
            "Represents stage, progress, completion, and role participation for a higher-level journey process layered over normal intervals.",
            "journey",
            ["journey.event", "effects.persistent"], ["journey.progress"], [], [JourneyEventsModule, PersistentEffectsModule],
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
        Mechanic(FixedIntervalDurationMechanic, "Fixed interval duration", "Uses one configured duration for every travel interval.", [], ["time.interval-duration"], Required("durationTicks", "integer", "Duration in ticks."), "crawl-profile.watch-length", ProcedureAutomationLevel.Automatic),
        Mechanic(MovementResolutionPolicyMechanic, "Movement resolution policy", "Selects continuous-distance or whole-hex movement and how actual distance is resolved.", ["time.interval-duration"], ["movement.resolution-mode"], RequiredMany(("travelResolution", "enum", "Travel resolution mode."), ("actualDistanceResolution", "enum", "Actual-distance resolution mode."), ("tracksIntraHexProgress", "boolean", "Track progress within a hex.")), "crawl-profile.movement-resolution", ProcedureAutomationLevel.Automatic),
        Mechanic(HexProgressPolicyMechanic, "Hex progress policy", "Configures generic progress costs and exit factors for spatial hex traversal.", ["movement.resolution-mode"], ["movement.hex-progress"], RequiredMany(("startingExitProgressFactor", "number", "Starting exit factor."), ("nearExitProgressFactor", "number", "Near exit factor."), ("farExitProgressFactor", "number", "Far exit factor."), ("backExitProgressFactor", "number", "Back exit factor."), ("directionChangesCostProgress", "boolean", "Direction changes cost progress."), ("directionChangeProgressCostFactor", "number", "Direction-change cost factor."), ("supportsDeliberateDoubleBack", "boolean", "Deliberate double-back support.")), "crawl-profile.hex-progress", ProcedureAutomationLevel.Automatic),
        Mechanic(NavigationCheckPolicyMechanic, "Navigation check policy", "Configures generic navigation-check and persistent directional-error behavior.", ["movement.resolution-mode"], ["navigation.policy"], RequiredMany(("usesNavigationChecks", "boolean", "Navigation checks required."), ("usesPersistentVeer", "boolean", "Navigation failure persists as veer.")), "crawl-profile.navigation", ProcedureAutomationLevel.Assisted),
        Mechanic(EncounterCheckCadenceMechanic, "Encounter check cadence", "Configures when encounter checks are due.", ["time.interval-duration"], ["encounter.check-cadence"], Required("cadence", "enum", "Encounter cadence."), "crawl-profile.encounter-cadence", ProcedureAutomationLevel.Assisted),
        Mechanic(DeterministicResolutionHelpersMechanic, "Deterministic resolution helpers", "Configures optional dice helpers that resolve runtime inputs without becoming runtime dependencies.", ["movement.resolution-mode", "navigation.policy", "encounter.check-cadence"], ["procedure.helper-configuration"], new Dictionary<string, ProcedureParameterDefinition>(), "crawl-profile.resolution-helpers", ProcedureAutomationLevel.Assisted),

        Mechanic(MovementBudgetMechanic, "Movement budget", "Represents a procedure-defined travel budget without assuming a game system's unit or source.", ["time.interval-duration"], ["movement.budget"], RequiredMany(("budgetModel", "enum", "Travel budget calculation model."), ("baseBudget", "number", "Base travel budget."), ("budgetUnit", "enum", "Travel budget unit."), ("limitingScope", "enum", "Scope that limits the budget.")), DeclarativeContractHandler, ProcedureAutomationLevel.Assisted, ["movement", "phase-3"], inputRequirements: Sources(("time.interval-duration", ManualOrExternal))),
        Mechanic(TerrainMovementPolicyMechanic, "Terrain movement policy", "Represents terrain, route, and weather adjustments to a generic movement budget.", ["movement.budget"], ["movement.terrain-adjustment"], RequiredMany(("costModel", "enum", "Terrain cost model."), ("terrainCosts", "map<number>", "Terrain tag cost map."), ("routeAdjustmentModel", "enum", "Route adjustment model."), ("weatherAdjustmentModel", "enum", "Weather adjustment model.")), DeclarativeContractHandler, ProcedureAutomationLevel.Assisted, ["movement", "environment", "phase-3"]),
        Mechanic(ParticipantActivityPolicyMechanic, "Participant activity policy", "Represents participant or role assignments and their generic capacity model.", ["time.interval-duration", "movement.budget"], ["participant.activity-state"], RequiredMany(("assignmentScope", "enum", "Assignment scope."), ("activityBudgetModel", "enum", "Activity capacity model."), ("activityKeys", "key-list", "Available generic activity keys."), ("roleKeys", "key-list", "Available generic role keys.")), DeclarativeContractHandler, ProcedureAutomationLevel.Manual, ["party", "activity", "phase-3"], inputRequirements: Sources(("time.interval-duration", ManualOrExternal), ("movement.budget", ManualOrExternal))),
        Mechanic(NavigationOutcomePolicyMechanic, "Navigation outcome policy", "Represents lost, directional-error, recognition, and reorientation behavior separately from the navigation check.", ["navigation.policy", "participant.activity-state"], ["navigation.outcome-state"], RequiredMany(("checkTriggerModel", "enum", "Navigation check trigger model."), ("failureStateModel", "enum", "Failure state model."), ("directionalErrorModel", "enum", "Directional error model."), ("recognitionModel", "enum", "Recognition model."), ("reorientationModel", "enum", "Reorientation model.")), DeclarativeContractHandler, ProcedureAutomationLevel.Assisted, ["navigation", "phase-3"], inputRequirements: Sources(("navigation.policy", ManualOrExternal), ("participant.activity-state", ManualOrExternal))),
        Mechanic(EncounterSchedulePolicyMechanic, "Encounter schedule policy", "Represents travel, camp, event-driven, or terrain-sensitive encounter schedules beyond the core cadence.", ["time.interval-duration", "encounter.check-cadence"], ["encounter.schedule"], RequiredMany(("scheduleModel", "enum", "Encounter schedule model."), ("travelChecksPerInterval", "number", "Travel checks represented per interval."), ("campCheck", "boolean", "Separate camp check."), ("terrainProbabilityModel", "enum", "Environment-dependent probability model.")), DeclarativeContractHandler, ProcedureAutomationLevel.Assisted, ["encounter", "phase-3"], inputRequirements: Sources(("time.interval-duration", ManualOrExternal), ("encounter.check-cadence", ManualOrExternal))),
        Mechanic(ResourceConsumptionPolicyMechanic, "Resource consumption policy", "Represents resource kinds, inventory style, and consumption timing while the full resource engine remains deferred.", ["time.interval-duration", "participant.activity-state"], ["resource.consumed"], RequiredMany(("resourceKinds", "key-list", "Resource kinds."), ("inventoryModel", "enum", "Inventory model."), ("consumptionModel", "enum", "Consumption model."), ("consumptionInterval", "enum", "Consumption interval.")), DeclarativeContractHandler, ProcedureAutomationLevel.Manual, ["resource", "survival", "phase-3"], inputRequirements: Sources(("time.interval-duration", ManualOrExternal), ("participant.activity-state", ManualOrExternal))),
        Mechanic(ForagingPolicyMechanic, "Foraging policy", "Represents forage resolution, time cost, and travel tradeoff.", ["participant.activity-state", "movement.terrain-adjustment"], ["resource.gathered"], RequiredMany(("resolutionModel", "enum", "Forage resolution model."), ("timeCost", "number", "Forage time cost."), ("timeUnit", "enum", "Forage time unit."), ("movementTradeoff", "enum", "Travel tradeoff.")), DeclarativeContractHandler, ProcedureAutomationLevel.Assisted, ["exploration", "resource", "phase-3"], inputRequirements: Sources(("participant.activity-state", ManualProviderOrExternal), ("movement.terrain-adjustment", ManualProviderOrExternal))),
        Mechanic(CampingPolicyMechanic, "Camping policy", "Represents camp setup, time cost, and watch policy.", ["time.interval-duration", "participant.activity-state"], ["camp.state"], RequiredMany(("resolutionModel", "enum", "Camp resolution model."), ("timeCost", "number", "Camp time cost."), ("timeUnit", "enum", "Camp time unit."), ("watchModel", "enum", "Camp watch model.")), DeclarativeContractHandler, ProcedureAutomationLevel.Assisted, ["survival", "rest", "phase-3"], inputRequirements: Sources(("time.interval-duration", ManualOrExternal), ("participant.activity-state", ManualOrExternal))),
        Mechanic(ForcedTravelPolicyMechanic, "Forced travel policy", "Represents travel beyond a normal limit and the generic consequence of failure.", ["time.interval-duration", "movement.budget"], ["effects.transient"], RequiredMany(("normalTravelLimit", "number", "Normal travel limit."), ("limitUnit", "enum", "Travel limit unit."), ("checkModel", "enum", "Forced-travel check model."), ("failureConsequence", "enum", "Generic failure consequence.")), DeclarativeContractHandler, ProcedureAutomationLevel.Assisted, ["time", "effect", "phase-3"], inputRequirements: Sources(("time.interval-duration", ManualOrExternal), ("movement.budget", ManualOrExternal))),
        Mechanic(ProgressiveExpeditionEffectMechanic, "Progressive expedition effect", "Represents persistent effect families, accumulation, recovery, and scope without implementing the later generalized effect engine.", ["effects.transient", "resource.consumed"], ["effects.persistent"], RequiredMany(("effectKinds", "key-list", "Effect families."), ("accumulationModel", "enum", "Accumulation model."), ("recoveryModel", "enum", "Recovery model."), ("scope", "enum", "Effect scope.")), DeclarativeContractHandler, ProcedureAutomationLevel.Manual, ["effect", "phase-3"], inputRequirements: Sources(("effects.transient", ManualProviderOrExternal), ("resource.consumed", ManualProviderOrExternal))),
        Mechanic(JourneyEventPolicyMechanic, "Journey event policy", "Represents event placement, role targeting, environmental influence, and consequence shape.", ["participant.activity-state", "movement.terrain-adjustment", "effects.persistent"], ["journey.event"], RequiredMany(("triggerModel", "enum", "Journey event trigger model."), ("targetingModel", "enum", "Journey event target model."), ("terrainInfluence", "enum", "Terrain influence model."), ("consequenceModel", "enum", "Event consequence model.")), DeclarativeContractHandler, ProcedureAutomationLevel.Manual, ["journey", "event", "phase-3"], inputRequirements: Sources(("participant.activity-state", ManualProviderOrExternal), ("movement.terrain-adjustment", ManualProviderOrExternal), ("effects.persistent", ManualProviderOrExternal))),
        Mechanic(MultiStageExpeditionProcessMechanic, "Multi-stage expedition process", "Represents journey stages, progress, completion, and role-driven behavior while the later process engine remains deferred.", ["journey.event", "effects.persistent"], ["journey.progress"], RequiredMany(("stageModel", "enum", "Journey stage model."), ("progressModel", "enum", "Journey progress model."), ("completionModel", "enum", "Journey completion model."), ("roleDriven", "boolean", "Whether roles drive journey resolution.")), DeclarativeContractHandler, ProcedureAutomationLevel.Manual, ["journey", "process", "phase-3"], inputRequirements: Sources(("journey.event", ManualProviderOrExternal), ("effects.persistent", ManualProviderOrExternal)))
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
        new(key, category, displayName, purpose, stage, reads, produces, required, optional, compatible, schema,
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
        new(key, displayName, description, inputs, outputs, schema, handler, compatibilityTags ?? [], automation, version, inputRequirements);

    private static IReadOnlyList<ProcedureInputRequirement> Sources(
        params (string Input, ProcedureInputSource Sources)[] values) =>
        values.Select(value => new ProcedureInputRequirement(value.Input, value.Sources)).ToArray();

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
