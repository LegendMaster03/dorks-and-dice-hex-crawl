using System.Globalization;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Stable native execution-handler identities embedded in materialized generic procedure snapshots.
/// The persisted values retain their Phase 1 names for snapshot compatibility; runtime dispatch is
/// based only on the embedded handler identity and never on a preset key or catalog lookup.
/// </summary>
public static class GenericProcedureExecutionHandlers
{
    public const string FixedIntervalDuration = "crawl-profile.watch-length";
    public const string MovementResolutionPolicy = "crawl-profile.movement-resolution";
    public const string HexProgressPolicy = "crawl-profile.hex-progress";
    public const string NavigationCheckPolicy = "crawl-profile.navigation";
    public const string EncounterCheckCadence = "crawl-profile.encounter-cadence";
    public const string DeterministicResolutionHelpers = "crawl-profile.resolution-helpers";

    internal static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        FixedIntervalDuration,
        MovementResolutionPolicy,
        HexProgressPolicy,
        NavigationCheckPolicy,
        EncounterCheckCadence,
        DeterministicResolutionHelpers
    };
}

public sealed class UnsupportedProcedureMechanicException : InvalidOperationException
{
    public UnsupportedProcedureMechanicException(
        string moduleKey,
        string mechanicKey,
        string executionHandler)
        : base($"Module '{moduleKey}' uses mechanic '{mechanicKey}' with unsupported execution handler '{executionHandler}'. The pinned procedure snapshot remains preserved, but this runtime version can not execute it.")
    {
        ModuleKey = moduleKey;
        MechanicKey = mechanicKey;
        ExecutionHandler = executionHandler;
    }

    public string ModuleKey { get; }
    public string MechanicKey { get; }
    public string ExecutionHandler { get; }
}

public sealed record ProcedureTimeRuntime(TimeSpan IntervalDuration);

public sealed record ProcedureMovementRuntime(
    TravelResolutionMode TravelResolution,
    ActualDistanceResolutionMode ActualDistanceResolution,
    bool TracksIntraHexProgress);

public sealed record ProcedureHexProgressRuntime(
    double StartingExitProgressFactor,
    double NearExitProgressFactor,
    double FarExitProgressFactor,
    double BackExitProgressFactor,
    bool DirectionChangesCostProgress,
    double DirectionChangeProgressCostFactor,
    bool SupportsDeliberateDoubleBack);

public sealed record ProcedureNavigationRuntime(
    bool UsesNavigationChecks,
    bool UsesPersistentVeer);

public sealed record ProcedureEncounterRuntime(EncounterCheckCadence Cadence);

/// <summary>
/// Ephemeral native runtime binding of one pinned generic procedure. This is not persisted and is
/// intentionally organized by generic procedure modules rather than recreating CrawlProcedureProfile.
/// </summary>
public sealed class GenericProcedureRuntime
{
    private GenericProcedureRuntime(
        string name,
        ProcedureTimeRuntime time,
        ProcedureMovementRuntime movement,
        ProcedureHexProgressRuntime hexProgress,
        ProcedureNavigationRuntime navigation,
        ProcedureEncounterRuntime encounters,
        ProcedureResolutionHelperProfile? resolutionHelpers)
    {
        Name = name;
        Time = time;
        Movement = movement;
        HexProgress = hexProgress;
        Navigation = navigation;
        Encounters = encounters;
        ResolutionHelpers = resolutionHelpers;
        Validate();
    }

    public string Name { get; }
    public ProcedureTimeRuntime Time { get; }
    public ProcedureMovementRuntime Movement { get; }
    public ProcedureHexProgressRuntime HexProgress { get; }
    public ProcedureNavigationRuntime Navigation { get; }
    public ProcedureEncounterRuntime Encounters { get; }
    public ProcedureResolutionHelperProfile? ResolutionHelpers { get; }

    public static GenericProcedureRuntime Bind(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        procedure.Validate();

        foreach (var selected in procedure.Modules)
        {
            if (!GenericProcedureExecutionHandlers.Supported.Contains(selected.Mechanic.ExecutionHandler))
            {
                throw new UnsupportedProcedureMechanicException(
                    selected.Module.Key,
                    selected.Mechanic.Key,
                    selected.Mechanic.ExecutionHandler);
            }
        }

        var time = Required(procedure, GenericProcedureExecutionHandlers.FixedIntervalDuration);
        var movement = Required(procedure, GenericProcedureExecutionHandlers.MovementResolutionPolicy);
        var progress = Required(procedure, GenericProcedureExecutionHandlers.HexProgressPolicy);
        var navigation = Required(procedure, GenericProcedureExecutionHandlers.NavigationCheckPolicy);
        var encounters = Required(procedure, GenericProcedureExecutionHandlers.EncounterCheckCadence);
        var helpers = Optional(procedure, GenericProcedureExecutionHandlers.DeterministicResolutionHelpers);

        return new GenericProcedureRuntime(
            procedure.Name,
            new ProcedureTimeRuntime(TimeSpan.FromTicks(Long(time, "durationTicks"))),
            new ProcedureMovementRuntime(
                EnumValue<TravelResolutionMode>(movement, "travelResolution"),
                EnumValue<ActualDistanceResolutionMode>(movement, "actualDistanceResolution"),
                Boolean(movement, "tracksIntraHexProgress")),
            new ProcedureHexProgressRuntime(
                Double(progress, "startingExitProgressFactor"),
                Double(progress, "nearExitProgressFactor"),
                Double(progress, "farExitProgressFactor"),
                Double(progress, "backExitProgressFactor"),
                Boolean(progress, "directionChangesCostProgress"),
                Double(progress, "directionChangeProgressCostFactor"),
                Boolean(progress, "supportsDeliberateDoubleBack")),
            new ProcedureNavigationRuntime(
                Boolean(navigation, "usesNavigationChecks"),
                Boolean(navigation, "usesPersistentVeer")),
            new ProcedureEncounterRuntime(EnumValue<EncounterCheckCadence>(encounters, "cadence")),
            helpers is null ? null : ReadHelpers(helpers));
    }

    /// <summary>
    /// Deliberate compatibility adapter for historical profile-only persisted sessions.
    /// New materialized sessions should bind their CampaignProcedure instead.
    /// </summary>
    public static GenericProcedureRuntime FromLegacyProfile(CrawlProcedureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        return new GenericProcedureRuntime(
            profile.Name,
            new ProcedureTimeRuntime(profile.WatchLength),
            new ProcedureMovementRuntime(
                profile.TravelResolution,
                profile.ActualDistanceResolution,
                profile.TracksIntraHexProgress),
            new ProcedureHexProgressRuntime(
                profile.StartingExitProgressFactor,
                profile.NearExitProgressFactor,
                profile.FarExitProgressFactor,
                profile.BackExitProgressFactor,
                profile.DirectionChangesCostProgress,
                profile.DirectionChangeProgressCostFactor,
                profile.SupportsDeliberateDoubleBack),
            new ProcedureNavigationRuntime(profile.UsesNavigationChecks, profile.UsesPersistentVeer),
            new ProcedureEncounterRuntime(profile.EncounterCadence),
            profile.ResolutionHelpers);
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("A generic procedure runtime requires a procedure name.");
        }
        if (Time.IntervalDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Procedure interval duration must be positive.");
        }

        ValidateFactor(HexProgress.StartingExitProgressFactor, nameof(HexProgress.StartingExitProgressFactor));
        ValidateFactor(HexProgress.NearExitProgressFactor, nameof(HexProgress.NearExitProgressFactor));
        ValidateFactor(HexProgress.FarExitProgressFactor, nameof(HexProgress.FarExitProgressFactor));
        ValidateFactor(HexProgress.BackExitProgressFactor, nameof(HexProgress.BackExitProgressFactor));
        ValidateFactor(HexProgress.DirectionChangeProgressCostFactor, nameof(HexProgress.DirectionChangeProgressCostFactor), allowZero: true);
        ResolutionHelpers?.Validate();

        if (Movement.TravelResolution == TravelResolutionMode.HexSteps && Movement.TracksIntraHexProgress)
        {
            throw new InvalidOperationException("Hex-step travel can not simultaneously use intra-hex progress.");
        }
        if (Movement.TravelResolution == TravelResolutionMode.HexSteps
            && Movement.ActualDistanceResolution != ActualDistanceResolutionMode.Fixed)
        {
            throw new InvalidOperationException("Hex-step travel does not use variable resolved physical distance.");
        }
        if (Movement.TravelResolution == TravelResolutionMode.ContinuousDistance && !Movement.TracksIntraHexProgress)
        {
            throw new InvalidOperationException("Continuous-distance travel currently requires intra-hex progress tracking.");
        }
        if (HexProgress.DirectionChangesCostProgress && !Movement.TracksIntraHexProgress)
        {
            throw new InvalidOperationException("Direction-change progress costs require intra-hex progress tracking.");
        }
    }

    private static MaterializedProcedureModule Required(CampaignProcedure procedure, string handler)
    {
        var selected = procedure.Modules
            .Where(value => string.Equals(value.Mechanic.ExecutionHandler, handler, StringComparison.Ordinal))
            .ToArray();
        return selected.Length switch
        {
            1 => selected[0],
            0 => throw new InvalidOperationException($"Pinned campaign procedure is missing required execution handler '{handler}'."),
            _ => throw new InvalidOperationException($"Pinned campaign procedure contains multiple modules for execution handler '{handler}'.")
        };
    }

    private static MaterializedProcedureModule? Optional(CampaignProcedure procedure, string handler)
    {
        var selected = procedure.Modules
            .Where(value => string.Equals(value.Mechanic.ExecutionHandler, handler, StringComparison.Ordinal))
            .ToArray();
        return selected.Length switch
        {
            0 => null,
            1 => selected[0],
            _ => throw new InvalidOperationException($"Pinned campaign procedure contains multiple modules for execution handler '{handler}'.")
        };
    }

    private static ProcedureResolutionHelperProfile? ReadHelpers(MaterializedProcedureModule module)
    {
        var values = module.Parameters;
        var travel = Boolean(values, "travel.enabled")
            ? new TravelResolutionHelperProfile(
                new DiceRollFormula(Int(values, "travel.diceCount"), Int(values, "travel.dieSides"), Int(values, "travel.modifier")),
                Double(values, "travel.distanceFactor"))
            : null;
        var navigation = Boolean(values, "navigation.enabled")
            ? new NavigationResolutionHelperProfile(
                new DiceRollFormula(Int(values, "navigation.diceCount"), Int(values, "navigation.dieSides"), Int(values, "navigation.modifier")))
            : null;
        var encounter = Boolean(values, "encounter.enabled")
            ? new EncounterResolutionHelperProfile(
                new DiceRollFormula(Int(values, "encounter.diceCount"), Int(values, "encounter.dieSides"), Int(values, "encounter.modifier")),
                new DiceRollResultSet(Value(values, "encounter.wanderingResults")),
                new DiceRollResultSet(Value(values, "encounter.keyedLocationResults")),
                Int(values, "encounter.timingSlots"))
            : null;
        return travel is null && navigation is null && encounter is null
            ? null
            : new ProcedureResolutionHelperProfile(travel, navigation, encounter);
    }

    private static string Value(MaterializedProcedureModule module, string key) => Value(module.Parameters, key);

    private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure execution requires parameter '{key}'.");

    private static bool Boolean(MaterializedProcedureModule module, string key) => Boolean(module.Parameters, key);

    private static bool Boolean(IReadOnlyDictionary<string, string> values, string key) =>
        bool.TryParse(Value(values, key), out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid boolean.");

    private static int Int(IReadOnlyDictionary<string, string> values, string key) =>
        int.TryParse(Value(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid integer.");

    private static long Long(MaterializedProcedureModule module, string key) =>
        long.TryParse(Value(module, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid long integer.");

    private static double Double(MaterializedProcedureModule module, string key) => Double(module.Parameters, key);

    private static double Double(IReadOnlyDictionary<string, string> values, string key) =>
        double.TryParse(Value(values, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a valid number.");

    private static T EnumValue<T>(MaterializedProcedureModule module, string key) where T : struct, Enum =>
        Enum.TryParse<T>(Value(module, key), true, out var value)
            ? value
            : throw new InvalidOperationException($"Procedure parameter '{key}' is not a supported {typeof(T).Name} value.");

    private static void ValidateFactor(double factor, string name, bool allowZero = false)
    {
        var invalidFloor = allowZero ? factor < 0 : factor <= 0;
        if (invalidFloor || !double.IsFinite(factor))
        {
            throw new InvalidOperationException($"{name} must be finite and {(allowZero ? "non-negative" : "positive")}.");
        }
    }
}

public static class GenericProcedureRuntimeRequirements
{
    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, CrawlSessionRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(state);
        return state switch
        {
            ExpeditionState spatial => IsEncounterCheckDue(procedure, spatial),
            NonSpatialSessionState nonSpatial => IsEncounterCheckDue(procedure, nonSpatial),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }

    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, ExpeditionState state)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(state);
        if (state.ActiveWatch is not null)
        {
            return false;
        }
        return IsEncounterCheckDue(
            procedure.Encounters.Cadence,
            state.History,
            Math.Max(1, state.CompletedWatches + 1),
            state.ElapsedTravelTime);
    }

    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, NonSpatialSessionState state)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(state);
        return IsEncounterCheckDue(
            procedure.Encounters.Cadence,
            state.History,
            state.ActiveWatch?.WatchNumber ?? Math.Max(1, state.CompletedWatches + 1),
            state.ElapsedTime);
    }

    public static bool IsNavigationResolutionPotentiallyRequired(GenericProcedureRuntime procedure, ExpeditionState state)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(state);
        if (state.ActiveWatch is not null || !procedure.Navigation.UsesNavigationChecks)
        {
            return false;
        }
        var watchNumber = Math.Max(1, state.CompletedWatches + 1);
        return !state.History.Any(runtimeEvent =>
            runtimeEvent.Kind == CrawlRuntimeEventKind.NavigationCheckResolved
            && runtimeEvent.WatchNumber == watchNumber);
    }

    private static bool IsEncounterCheckDue(
        EncounterCheckCadence cadence,
        IReadOnlyList<CrawlRuntimeEvent> history,
        int watchNumber,
        TimeSpan elapsed)
    {
        if (cadence == EncounterCheckCadence.None)
        {
            return false;
        }
        if (cadence is EncounterCheckCadence.PerWatch or EncounterCheckCadence.Custom)
        {
            return !history.Any(runtimeEvent =>
                runtimeEvent.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && runtimeEvent.WatchNumber == watchNumber);
        }

        var day = DayIndex(elapsed);
        return !history.Any(runtimeEvent =>
            runtimeEvent.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
            && DayIndex(runtimeEvent.ExpeditionElapsedTime) == day);
    }

    private static int DayIndex(TimeSpan elapsed) => (int)Math.Floor(elapsed.TotalDays);
}
