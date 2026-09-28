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

    public const string FixedIntervalDurationMechanic = "fixed-interval-duration";
    public const string MovementResolutionPolicyMechanic = "movement-resolution-policy";
    public const string HexProgressPolicyMechanic = "hex-progress-policy";
    public const string NavigationCheckPolicyMechanic = "navigation-check-policy";
    public const string EncounterCheckCadenceMechanic = "encounter-check-cadence";
    public const string DeterministicResolutionHelpersMechanic = "deterministic-resolution-helpers";

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
                ("usesPersistentVeer", "boolean", "Whether failed navigation persists as directional veer."))),
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
            new Dictionary<string, ProcedureParameterDefinition>())
    ];

    public static IReadOnlyList<MechanicDefinition> Mechanics { get; } =
    [
        Mechanic(FixedIntervalDurationMechanic, "Fixed interval duration", "Uses one configured duration for every travel interval.", [], ["time.interval-duration"], Required("durationTicks", "integer", "Duration in ticks."), "crawl-profile.watch-length", ProcedureAutomationLevel.Automatic),
        Mechanic(MovementResolutionPolicyMechanic, "Movement resolution policy", "Selects continuous-distance or whole-hex movement and how actual distance is resolved.", ["time.interval-duration"], ["movement.resolution-mode"], RequiredMany(("travelResolution", "enum", "Travel resolution mode."), ("actualDistanceResolution", "enum", "Actual-distance resolution mode."), ("tracksIntraHexProgress", "boolean", "Track progress within a hex.")), "crawl-profile.movement-resolution", ProcedureAutomationLevel.Automatic),
        Mechanic(HexProgressPolicyMechanic, "Hex progress policy", "Configures generic progress costs and exit factors for spatial hex traversal.", ["movement.resolution-mode"], ["movement.hex-progress"], RequiredMany(("startingExitProgressFactor", "number", "Starting exit factor."), ("nearExitProgressFactor", "number", "Near exit factor."), ("farExitProgressFactor", "number", "Far exit factor."), ("backExitProgressFactor", "number", "Back exit factor."), ("directionChangesCostProgress", "boolean", "Direction changes cost progress."), ("directionChangeProgressCostFactor", "number", "Direction-change cost factor."), ("supportsDeliberateDoubleBack", "boolean", "Deliberate double-back support.")), "crawl-profile.hex-progress", ProcedureAutomationLevel.Automatic),
        Mechanic(NavigationCheckPolicyMechanic, "Navigation check policy", "Configures generic navigation-check and persistent directional-error behavior.", ["movement.resolution-mode"], ["navigation.policy"], RequiredMany(("usesNavigationChecks", "boolean", "Navigation checks required."), ("usesPersistentVeer", "boolean", "Navigation failure persists as veer.")), "crawl-profile.navigation", ProcedureAutomationLevel.Assisted),
        Mechanic(EncounterCheckCadenceMechanic, "Encounter check cadence", "Configures when encounter checks are due.", ["time.interval-duration"], ["encounter.check-cadence"], Required("cadence", "enum", "Encounter cadence."), "crawl-profile.encounter-cadence", ProcedureAutomationLevel.Assisted),
        Mechanic(DeterministicResolutionHelpersMechanic, "Deterministic resolution helpers", "Configures optional dice helpers that resolve runtime inputs without becoming runtime dependencies.", ["movement.resolution-mode", "navigation.policy", "encounter.check-cadence"], ["procedure.helper-configuration"], new Dictionary<string, ProcedureParameterDefinition>(), "crawl-profile.resolution-helpers", ProcedureAutomationLevel.Assisted)
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
        IReadOnlyDictionary<string, ProcedureParameterDefinition> schema) =>
        new(key, category, displayName, purpose, stage, reads, produces, required, optional, compatible, schema,
            new Dictionary<string, string> { ["phase"] = "1" });

    private static MechanicDefinition Mechanic(
        string key,
        string displayName,
        string description,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        IReadOnlyDictionary<string, ProcedureParameterDefinition> schema,
        string handler,
        ProcedureAutomationLevel automation) =>
        new(key, displayName, description, inputs, outputs, schema, handler, [], automation, 1);

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
