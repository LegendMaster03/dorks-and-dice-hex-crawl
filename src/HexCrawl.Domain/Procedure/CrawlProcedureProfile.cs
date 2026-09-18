using System.Globalization;

namespace HexCrawl.Domain.Procedure;

public enum EncounterCheckCadence
{
    None,
    PerWatch,
    PerDay,
    Custom
}

public enum TravelResolutionMode
{
    ContinuousDistance,
    HexSteps
}

public enum ActualDistanceResolutionMode
{
    Fixed,
    VariableResolved
}

public sealed record DiceRollFormula(int DiceCount, int DieSides, int Modifier = 0)
{
    public int MinimumTotal => checked(DiceCount + Modifier);
    public int MaximumTotal => checked((DiceCount * DieSides) + Modifier);

    public void Validate(string label)
    {
        if (DiceCount <= 0)
        {
            throw new InvalidOperationException($"{label} must roll at least one die.");
        }
        if (DieSides < 2 || DieSides == int.MaxValue)
        {
            throw new InvalidOperationException($"{label} must use dice with between 2 and {int.MaxValue - 1} sides.");
        }

        try
        {
            checked
            {
                _ = DiceCount + Modifier;
                _ = (DiceCount * DieSides) + Modifier;
            }
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException($"{label} exceeds the supported integer roll range.", exception);
        }
    }
}

public sealed record DiceRollResultSet(string Canonical)
{
    public IReadOnlyList<int> Values
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Canonical))
            {
                return Array.Empty<int>();
            }

            try
            {
                return Canonical
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture))
                    .ToArray();
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new InvalidOperationException("Dice-roll result set contains an invalid integer value.", exception);
            }
        }
    }

    public bool Contains(int value) => Values.Contains(value);

    public static DiceRollResultSet From(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var canonical = string.Join(
            ",",
            values
                .Distinct()
                .Order()
                .Select(value => value.ToString(CultureInfo.InvariantCulture)));
        return new DiceRollResultSet(canonical);
    }
}

public sealed record TravelPaceDefaults(
    double? Normal = null,
    double? Slow = null,
    double? Fast = null,
    double? Exploration = null)
{
    public double? Resolve(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "normal" => Normal,
        "slow" => Slow,
        "fast" => Fast,
        "exploration" => Exploration,
        _ => null
    };

    public void Validate()
    {
        ValidatePace(Normal, "normal");
        ValidatePace(Slow, "slow");
        ValidatePace(Fast, "fast");
        ValidatePace(Exploration, "exploration");
    }

    private static void ValidatePace(double? value, string key)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value <= 0))
        {
            throw new InvalidOperationException($"Travel pace '{key}' must be finite and positive.");
        }
    }
}

public sealed record TravelResolutionHelperProfile
{
    public bool SupportsRateArithmetic { get; init; }
    public TravelPaceDefaults? PaceDefaults { get; init; }
    public DiceRollFormula? Roll { get; init; }
    public double? DistanceFactorPerRollPoint { get; init; }

    public void Validate()
    {
        PaceDefaults?.Validate();
        if (Roll is null)
        {
            if (DistanceFactorPerRollPoint.HasValue)
            {
                throw new InvalidOperationException("Travel helper variance factor requires a variance roll.");
            }
            return;
        }

        Roll.Validate("Travel helper roll");
        if (!DistanceFactorPerRollPoint.HasValue
            || !double.IsFinite(DistanceFactorPerRollPoint.Value)
            || DistanceFactorPerRollPoint.Value <= 0)
        {
            throw new InvalidOperationException("Travel helper distance factor must be finite and positive when a variance roll is configured.");
        }
        if (Roll.MinimumTotal <= 0)
        {
            throw new InvalidOperationException("Travel helper roll must always produce a positive multiplier.");
        }
    }
}

public enum FailureVeerRuleKind
{
    AlexandrianHexD10
}

public sealed record FailureVeerRule(
    FailureVeerRuleKind Kind,
    DiceRollFormula Roll)
{
    public void Validate()
    {
        Roll.Validate("Navigation failure veer roll");
        if (Kind == FailureVeerRuleKind.AlexandrianHexD10
            && (Roll.DiceCount != 1 || Roll.DieSides != 10 || Roll.Modifier != 0))
        {
            throw new InvalidOperationException("Alexandrian hex veer requires an unmodified 1d10 roll.");
        }
    }
}

public sealed record NavigationResolutionHelperProfile(
    DiceRollFormula CheckRoll,
    FailureVeerRule? FailureVeer = null)
{
    public void Validate()
    {
        CheckRoll.Validate("Navigation helper check roll");
        FailureVeer?.Validate();
    }
}

public sealed record EncounterResolutionHelperProfile(
    DiceRollFormula CheckRoll,
    DiceRollResultSet WanderingResults,
    DiceRollResultSet KeyedLocationResults,
    int TimingSlots)
{
    public void Validate()
    {
        CheckRoll.Validate("Encounter helper check roll");
        if (TimingSlots <= 0 || TimingSlots == int.MaxValue)
        {
            throw new InvalidOperationException("Encounter helper timing slots must be between 1 and int.MaxValue - 1.");
        }

        var minimum = CheckRoll.MinimumTotal;
        var maximum = CheckRoll.MaximumTotal;
        var wandering = WanderingResults.Values.Distinct().ToHashSet();
        var keyed = KeyedLocationResults.Values.Distinct().ToHashSet();
        if (wandering.Any(result => result < minimum || result > maximum)
            || keyed.Any(result => result < minimum || result > maximum))
        {
            throw new InvalidOperationException("Encounter helper trigger results must be possible totals for its check roll.");
        }
        if (wandering.Overlaps(keyed))
        {
            throw new InvalidOperationException("Encounter helper wandering and keyed-location trigger results can not overlap.");
        }
    }
}

public sealed record ProcedureResolutionHelperProfile(
    TravelResolutionHelperProfile? Travel = null,
    NavigationResolutionHelperProfile? Navigation = null,
    EncounterResolutionHelperProfile? Encounter = null)
{
    public void Validate()
    {
        Travel?.Validate();
        Navigation?.Validate();
        Encounter?.Validate();
    }
}

public sealed record CrawlProcedureProfile
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public TimeSpan WatchLength { get; init; } = TimeSpan.FromHours(4);
    public TravelResolutionMode TravelResolution { get; init; } = TravelResolutionMode.ContinuousDistance;
    public ActualDistanceResolutionMode ActualDistanceResolution { get; init; } = ActualDistanceResolutionMode.Fixed;
    public EncounterCheckCadence EncounterCadence { get; init; } = EncounterCheckCadence.PerWatch;
    public bool UsesNavigationChecks { get; init; }
    public bool UsesPersistentVeer { get; init; }
    public bool TracksIntraHexProgress { get; init; } = true;
    public bool DirectionChangesCostProgress { get; init; }
    public bool SupportsDeliberateDoubleBack { get; init; } = true;
    public ProcedureResolutionHelperProfile? ResolutionHelpers { get; init; }

    public double StartingExitProgressFactor { get; init; } = 0.5d;
    public double NearExitProgressFactor { get; init; } = 0.5d;
    public double FarExitProgressFactor { get; init; } = 1d;
    public double BackExitProgressFactor { get; init; } = 0.5d;
    public double DirectionChangeProgressCostFactor { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key) || string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("A crawl procedure profile requires a key and name.");
        }

        if (WatchLength <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Watch length must be positive.");
        }

        ValidateFactor(StartingExitProgressFactor, nameof(StartingExitProgressFactor));
        ValidateFactor(NearExitProgressFactor, nameof(NearExitProgressFactor));
        ValidateFactor(FarExitProgressFactor, nameof(FarExitProgressFactor));
        ValidateFactor(BackExitProgressFactor, nameof(BackExitProgressFactor));
        ValidateFactor(DirectionChangeProgressCostFactor, nameof(DirectionChangeProgressCostFactor), allowZero: true);
        ResolutionHelpers?.Validate();

        if (TravelResolution == TravelResolutionMode.HexSteps && TracksIntraHexProgress)
        {
            throw new InvalidOperationException("Hex-step travel can not simultaneously use intra-hex progress.");
        }
        if (TravelResolution == TravelResolutionMode.HexSteps && ActualDistanceResolution != ActualDistanceResolutionMode.Fixed)
        {
            throw new InvalidOperationException("Hex-step travel does not use variable resolved physical distance.");
        }
        if (TravelResolution == TravelResolutionMode.ContinuousDistance && !TracksIntraHexProgress)
        {
            throw new InvalidOperationException("Continuous-distance travel currently requires intra-hex progress tracking.");
        }
        if (DirectionChangesCostProgress && !TracksIntraHexProgress)
        {
            throw new InvalidOperationException("Direction-change progress costs require intra-hex progress tracking.");
        }
    }

    public static CrawlProcedureProfile AlexandrianAdvancedBaseline() => new()
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
            Travel: new TravelResolutionHelperProfile
            {
                SupportsRateArithmetic = true,
                PaceDefaults = new TravelPaceDefaults(
                    Normal: 1d,
                    Slow: 2d / 3d,
                    Fast: 1.5d,
                    Exploration: 0.5d),
                Roll = new DiceRollFormula(2, 6, 3),
                DistanceFactorPerRollPoint = 0.1d
            },
            Navigation: new NavigationResolutionHelperProfile(
                new DiceRollFormula(1, 20),
                new FailureVeerRule(
                    FailureVeerRuleKind.AlexandrianHexD10,
                    new DiceRollFormula(1, 10))),
            Encounter: new EncounterResolutionHelperProfile(
                new DiceRollFormula(1, 8),
                DiceRollResultSet.From(new[] { 1 }),
                DiceRollResultSet.From(new[] { 8 }),
                8))
    };

    public static CrawlProcedureProfile SimplifiedFixedDistance() => new()
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
        BackExitProgressFactor = 0.5d,
        ResolutionHelpers = new ProcedureResolutionHelperProfile(
            Travel: new TravelResolutionHelperProfile
            {
                SupportsRateArithmetic = true
            })
    };

    public static CrawlProcedureProfile SimplifiedHexStep() => new()
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

    private static void ValidateFactor(double factor, string name, bool allowZero = false)
    {
        var invalidFloor = allowZero ? factor < 0 : factor <= 0;
        if (invalidFloor || double.IsNaN(factor) || double.IsInfinity(factor))
        {
            throw new InvalidOperationException($"{name} must be finite and {(allowZero ? "non-negative" : "positive")}.");
        }
    }
}
