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

    // These are ratios of the grid's physical neighbor-center distance. They preserve
    // tabletop abstract hex progress without assuming a 12-mile grid.
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
        if (UsesPersistentVeer && !UsesNavigationChecks)
        {
            throw new InvalidOperationException("Persistent veer requires navigation checks to be enabled.");
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
        DirectionChangeProgressCostFactor = 1d / 6d
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
        BackExitProgressFactor = 0.5d
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
