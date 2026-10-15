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

/// <summary>
/// A generic resolved range-table entry with the existing procedure roll audit.
/// The same result contract is available to every procedure consumer.
/// </summary>
public sealed record ProcedureRangeTableOutcome(
    string TableKey,
    string ResultText,
    string SourceReference,
    ProcedureResolutionRoll Roll);

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
    ResolvedTravelAmount? Travel,
    ResolvedCellNavigation? Navigation,
    ResolvedEncounter? Encounter,
    GeneratedProcedureResolutionStatus Status,
    long? ConsumedAtVersion = null,
    long? ConsumedAuditSequence = null)
{
    // A missing policy binding must fail authorization, not default to false.
    public bool? SuppressesNavigationCheck { get; init; }
    public bool? DeliberateDoubleBack { get; init; }
}
