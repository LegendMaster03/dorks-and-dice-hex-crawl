using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public interface IProcedureResolutionRandomSource
{
    int NextInt32(int minInclusive, int maxExclusive);
}

public sealed record ProcedureResolutionHelperCommand
{
    public long ExpectedVersion { get; init; }
    public double? ExpectedDistance { get; init; }
    public bool SuppressesNavigationCheck { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public int? NavigationDifficultyClass { get; init; }
    public int NavigationModifier { get; init; }
    public int? FailureVeerSteps { get; init; }
    public Guid? KeyedLocationId { get; init; }
    public string? TravelDistanceRule { get; init; }
    public int? BaseSpeedFeet { get; init; }
    public string? Terrain { get; init; }
    public string? Route { get; init; }
    public IReadOnlyList<string> NavigationRiskFactors { get; init; } = [];
    internal string? ExpectedDistanceRulesNote { get; init; }
    internal string? NavigationDifficultyRulesNote { get; init; }
}

public sealed record ProcedureResolutionRoll(
    string Purpose,
    string Formula,
    IReadOnlyList<int> Dice,
    int Modifier,
    int Total);

public sealed record ProcedureResolvedTravel(
    double ExpectedDistance,
    double ActualDistance,
    ResolutionProvenance Provenance);

public sealed record ProcedureResolvedNavigation(
    NavigationCheckOutcome Outcome,
    int? VeerSteps,
    ResolutionProvenance Provenance);

public sealed record ProcedureResolvedEncounter(
    EncounterOutcomeKind Kind,
    double? OccursAtHours,
    Guid? LocationId,
    string? Note,
    ResolutionProvenance Provenance);

public enum GeneratedProcedureResolutionStatus
{
    Available,
    Superseded,
    Consumed
}

public sealed record GeneratedProcedureResolution(
    Guid Id,
    Guid SessionId,
    long GeneratedFromVersion,
    long GeneratedAtVersion,
    long AuditSequence,
    int WatchNumber,
    IReadOnlyList<ProcedureResolutionRoll> Rolls,
    ProcedureResolvedTravel? Travel,
    ProcedureResolvedNavigation? Navigation,
    ProcedureResolvedEncounter? Encounter,
    GeneratedProcedureResolutionStatus Status,
    long? ConsumedAtVersion = null,
    long? ConsumedAuditSequence = null);

public sealed record ProcedureResolutionHelperResult(
    long ExpeditionVersion,
    Guid? GeneratedResolutionId,
    long? AuditSequence,
    ProcedureResolvedTravel? Travel,
    ProcedureResolvedNavigation? Navigation,
    ProcedureResolvedEncounter? Encounter,
    IReadOnlyList<ProcedureResolutionRoll> Rolls,
    IReadOnlyList<string> Notes);
