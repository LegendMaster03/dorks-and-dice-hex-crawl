using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed partial class HexCrawlService
{
    public async Task<StoredOverworld> CreateSourceMapAsync(
        Guid overworldId,
        string ownerUserId,
        CreateSourceMapCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        ValidatePoints(command.WorldCoverageBoundary, "Source-map coverage");
        var map = new SourceMapRepresentation(
            Guid.NewGuid(),
            RequiredText(command.GeographyKey, "Geography key"),
            RequiredText(command.Name, "Source-map name"),
            command.Role,
            RequiredAssetKey(command.AssetKey),
            command.ContainsBakedGrid,
            command.Alignment,
            command.WorldCoverageBoundary ?? []);
        var updated = current with
        {
            World = current.World with { SourceMaps = [.. current.World.SourceMaps, map] }
        };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> UpdateSourceMapAsync(
        Guid overworldId,
        Guid sourceMapId,
        string ownerUserId,
        UpdateSourceMapCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var existing = current.World.SourceMaps.FirstOrDefault(item => item.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source-map representation was not found.");
        ValidatePoints(command.WorldCoverageBoundary, "Source-map coverage");
        var replacement = existing with
        {
            GeographyKey = RequiredText(command.GeographyKey, "Geography key"),
            Name = RequiredText(command.Name, "Source-map name"),
            Role = command.Role,
            AssetKey = RequiredAssetKey(command.AssetKey),
            ContainsBakedGrid = command.ContainsBakedGrid,
            Alignment = command.Alignment,
            WorldCoverageBoundary = command.WorldCoverageBoundary ?? []
        };
        var updated = current with
        {
            World = current.World with
            {
                SourceMaps = current.World.SourceMaps.Select(item => item.Id == sourceMapId ? replacement : item).ToArray()
            }
        };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> DeleteSourceMapAsync(
        Guid overworldId,
        Guid sourceMapId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        if (!current.World.SourceMaps.Any(item => item.Id == sourceMapId))
        {
            throw new HexCrawlNotFoundException("Source-map representation was not found.");
        }
        var updated = current with
        {
            World = current.World with { SourceMaps = current.World.SourceMaps.Where(item => item.Id != sourceMapId).ToArray() }
        };
        return await SaveWorldAsync(updated, expectedVersion, cancellationToken);
    }

    private static string RequiredAssetKey(string value)
    {
        var key = RequiredText(value, "Asset key");
        if (Path.IsPathRooted(key))
        {
            throw new ArgumentException("Source-map asset keys must be deployment-independent logical references, not absolute machine paths.", nameof(value));
        }
        return key;
    }


}
