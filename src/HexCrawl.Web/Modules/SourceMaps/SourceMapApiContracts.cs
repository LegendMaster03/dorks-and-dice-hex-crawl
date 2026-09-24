using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public sealed record SourceMapSourceArchiveContract(
    long Length,
    string MediaType,
    string? OriginalFileName);

public sealed record SourceMapDetailContract(
    Guid Id,
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    int PixelWidth,
    int PixelHeight,
    string MediaType,
    string? OriginalFileName,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary,
    int ImportedContentCount,
    SourceMapImportProvenance? ImportProvenance,
    SourceMapSourceArchiveContract? SourceArchive)
{
    public static SourceMapDetailContract From(SourceMapRepresentation map) => new(
        map.Id,
        map.GeographyKey,
        map.Name,
        map.Role,
        map.AssetKey,
        map.ContainsBakedGrid,
        map.PixelWidth,
        map.PixelHeight,
        map.MediaType,
        map.OriginalFileName,
        map.Alignment,
        map.WorldCoverageBoundary,
        map.ImportedContent?.Count ?? 0,
        map.ImportProvenance,
        map.SourceArchive is null
            ? null
            : new SourceMapSourceArchiveContract(
                map.SourceArchive.Length,
                map.SourceArchive.MediaType,
                map.SourceArchive.OriginalFileName));
}

public sealed record SourceMapListContract(long OverworldVersion, IReadOnlyList<SourceMapDetailContract> SourceMaps);

public sealed record WonderdraftInspectionContract(
    int? FormatVersion,
    int PixelWidth,
    int PixelHeight,
    int SymbolCount,
    int LabelCount,
    int PathCount,
    int TerritoryCount,
    bool HasGrid,
    IReadOnlyList<string> IncludedPacks,
    IReadOnlyList<string> IncludedDefaultPacks,
    IReadOnlyDictionary<string, string> GridMetadata,
    IReadOnlyDictionary<string, string> ScaleMetadata,
    WonderdraftPhysicalScaleContract? PhysicalScale);

public sealed record WonderdraftPhysicalScaleContract(
    string UnitLabel,
    double DistancePerSegment,
    int SegmentCount,
    double PixelLength,
    double UnitsPerPixel);

public sealed record WonderdraftSourceImportContract(
    OverworldContract World,
    WonderdraftInspectionContract Summary,
    Guid SourceMapId,
    int SourceRecordCount,
    string RegistrationMode,
    string? RegistrationNote);

public sealed record WonderdraftCandidateContract(
    string Key,
    string SourceKind,
    string GeometryKind,
    string DisplayName,
    string? Descriptor,
    string? Problem,
    IReadOnlyDictionary<string, string> Properties,
    WorldPoint? SourcePosition,
    IReadOnlyList<WorldPoint> SourcePoints,
    WorldPoint? WorldPosition,
    IReadOnlyList<WorldPoint> WorldPoints);

public sealed record WonderdraftCandidatePreviewContract(
    Guid SourceMapId,
    double SourceScaleX,
    double SourceScaleY,
    WonderdraftInspectionContract Summary,
    IReadOnlyList<WonderdraftCandidateContract> Candidates);

public sealed record WonderdraftImportSelectionContract(
    string CandidateKey,
    string Target,
    string Name,
    string Category,
    string? Discoverability);

public sealed record WonderdraftStoredImportRequest(
    IReadOnlyList<WonderdraftImportSelectionContract>? Selections,
    long ExpectedVersion);

public sealed record SourceMapMetadataRequest(
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    bool ContainsBakedGrid,
    long ExpectedVersion)
{
    public UpdateSourceMapMetadataCommand ToCommand() => new(GeographyKey, Name, Role, ContainsBakedGrid, ExpectedVersion);
}

public sealed record RegistrationControlPointContract(WorldPoint SourcePixel, WorldPoint WorldPoint)
{
    public MapRegistrationControlPoint ToDomain() => new(SourcePixel, WorldPoint);
}

public sealed record SourceMapRegistrationRequest(
    IReadOnlyList<RegistrationControlPointContract> ControlPoints,
    long ExpectedVersion)
{
    public RegisterSourceMapCommand ToCommand() => new(ControlPoints.Select(point => point.ToDomain()).ToArray(), ExpectedVersion);
}
