using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

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
    IReadOnlyList<WorldPoint> WorldCoverageBoundary)
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
        map.WorldCoverageBoundary);
}

public sealed record SourceMapListContract(long OverworldVersion, IReadOnlyList<SourceMapDetailContract> SourceMaps);

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
