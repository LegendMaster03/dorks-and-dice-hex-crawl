using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record CreateOverworldCommand(
    string Name,
    HexOrientation Orientation,
    WorldPoint Origin,
    double RotationDegrees,
    double HexRadiusWorldUnits,
    double NeighborCenterDistance,
    DistanceUnit DistanceUnit);

public sealed record UpdateOverworldCommand(
    string Name,
    HexGridDefinition Grid,
    long ExpectedVersion);

public sealed record CreateLocationCommand(
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability,
    long ExpectedVersion);

public sealed record UpdateLocationCommand(
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability,
    long ExpectedVersion);

public sealed record CreateFeatureCommand(
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary,
    long ExpectedVersion);

public sealed record UpdateFeatureCommand(
    string Name,
    string Category,
    SpatialFeatureKind Kind,
    WorldPoint? Position,
    IReadOnlyList<WorldPoint>? Path,
    IReadOnlyList<WorldPoint>? Boundary,
    long ExpectedVersion);

public sealed record CreateSourceMapCommand(
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary,
    long ExpectedVersion);

public sealed record UpdateSourceMapCommand(
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary,
    long ExpectedVersion);

public sealed record StartExpeditionCommand(
    string Name,
    string ProcedureKey,
    HexCoordinate StartHex);

public sealed record AdvanceExpeditionCommand
{
    public long ExpectedVersion { get; init; }
    public int IntendedDirection { get; init; }
    public string PaceKey { get; init; } = "normal";
    public IReadOnlyList<string> Activities { get; init; } = [];
    public string NavigationAidKey { get; init; } = "none";
    public bool SuppressesNavigationCheck { get; init; }
    public bool ResetsVeerAtBoundary { get; init; }
    public double? ExpectedDistance { get; init; }
    public double? ActualDistance { get; init; }
    public int? HexSteps { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public NavigationCheckOutcome? NavigationOutcome { get; init; }
    public int? VeerSteps { get; init; }
    public EncounterOutcomeKind? EncounterOutcome { get; init; }
    public double? EncounterHour { get; init; }
    public Guid? LocationId { get; init; }
    public string? EncounterNote { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public bool ContinueAcrossBoundaries { get; init; }
    public bool? RecognizedLost { get; init; }
    public bool? Reorient { get; init; }
    public string? DmOverrideNote { get; init; }
}

public sealed record DiscoverSubjectCommand(
    long ExpectedVersion,
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    string? Source);
