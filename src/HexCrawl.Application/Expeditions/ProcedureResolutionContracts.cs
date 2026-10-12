using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

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
    // Set only after provider/composition has proved the physical unit.
    internal DistanceMeasure? ExpectedDistanceQualified { get; init; }
}

/// <summary>
/// The geometry-neutral server helper uses explicit physical units and angular
/// navigation, never an axial step-count or implicit map-distance unit.
/// </summary>
public sealed record CellProcedureResolutionHelperCommand
{
    public long ExpectedVersion { get; init; }
    public DistanceMeasure? ExpectedDistance { get; init; }
    public bool SuppressesNavigationCheck { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public int? NavigationDifficultyClass { get; init; }
    public int NavigationModifier { get; init; }
    public double? FailureVeerDegrees { get; init; }
    public Guid? KeyedLocationId { get; init; }
    public string? TravelDistanceRule { get; init; }
    public int? BaseSpeedFeet { get; init; }
    public string? Terrain { get; init; }
    public string? Route { get; init; }
    public IReadOnlyList<string> NavigationRiskFactors { get; init; } = [];
    internal string? NavigationDifficultyRulesNote { get; init; }
    internal string? ExpectedDistanceRulesNote { get; init; }
}

public sealed record CellProcedureResolutionHelperResult(
    long ExpeditionVersion,
    Guid? GeneratedResolutionId,
    long? AuditSequence,
    ResolvedTravelAmount? Travel,
    ResolvedCellNavigation? Navigation,
    ResolvedEncounter? Encounter,
    IReadOnlyList<ProcedureResolutionRoll> Rolls,
    IReadOnlyList<string> Notes);

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
    long? ConsumedAuditSequence = null)
{
    // V2 grants are typed and must never be authorized by legacy scalar or
    // hex-step fields in the positional V1 grant payload.
    public ResolvedTravelAmount? CellTravel { get; init; }
    public ResolvedCellNavigation? CellNavigation { get; init; }
    public ResolvedEncounter? CellEncounter { get; init; }
    // Bind the generated navigation result to the plan whose check policy
    // produced it. Null marks older, non-V2 grants; these are never usable
    // to authorize geometric automatic resolutions.
    public bool? CellSuppressesNavigationCheck { get; init; }
    public bool? CellDeliberateDoubleBack { get; init; }
}

public sealed record ProcedureResolutionHelperResult(
    long ExpeditionVersion,
    Guid? GeneratedResolutionId,
    long? AuditSequence,
    ProcedureResolvedTravel? Travel,
    ProcedureResolvedNavigation? Navigation,
    ProcedureResolvedEncounter? Encounter,
    IReadOnlyList<ProcedureResolutionRoll> Rolls,
    IReadOnlyList<string> Notes);
