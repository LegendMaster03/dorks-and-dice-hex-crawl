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

public sealed record CrawlProcedureProfile
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public TimeSpan WatchLength { get; init; } = TimeSpan.FromHours(4);
    public TravelResolutionMode TravelResolution { get; init; } = TravelResolutionMode.ContinuousDistance;
    public EncounterCheckCadence EncounterCadence { get; init; } = EncounterCheckCadence.PerWatch;
    public bool UsesNavigationChecks { get; init; }
    public bool UsesPersistentVeer { get; init; }

    public static CrawlProcedureProfile AlexandrianAdvancedBaseline() => new()
    {
        Key = "alexandrian-advanced",
        Name = "Alexandrian Advanced",
        WatchLength = TimeSpan.FromHours(4),
        TravelResolution = TravelResolutionMode.ContinuousDistance,
        EncounterCadence = EncounterCheckCadence.PerWatch,
        UsesNavigationChecks = true,
        UsesPersistentVeer = true
    };
}
