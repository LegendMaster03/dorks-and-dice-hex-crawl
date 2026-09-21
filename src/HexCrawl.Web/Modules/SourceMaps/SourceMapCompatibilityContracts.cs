using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public sealed record SourceMapMutationRequest(
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    MapRegistrationTransform? Alignment,
    IReadOnlyList<WorldPoint> WorldCoverageBoundary,
    long ExpectedVersion)
{
    public CreateSourceMapCommand ToCreateCommand() => new(
        GeographyKey, Name, Role, AssetKey, ContainsBakedGrid, Alignment, WorldCoverageBoundary, ExpectedVersion);
    public UpdateSourceMapCommand ToUpdateCommand() => new(
        GeographyKey, Name, Role, AssetKey, ContainsBakedGrid, Alignment, WorldCoverageBoundary, ExpectedVersion);
}

