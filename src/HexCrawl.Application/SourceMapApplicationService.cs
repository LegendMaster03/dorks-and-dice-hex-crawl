using HexCrawl.Application.Persistence;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed record UploadSourceMapCommand(
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    string AssetKey,
    bool ContainsBakedGrid,
    int PixelWidth,
    int PixelHeight,
    string MediaType,
    string? OriginalFileName,
    long ExpectedVersion);

public sealed record UpdateSourceMapMetadataCommand(
    string GeographyKey,
    string Name,
    SourceMapRole Role,
    bool ContainsBakedGrid,
    long ExpectedVersion);

public sealed record RegisterSourceMapCommand(
    IReadOnlyList<MapRegistrationControlPoint> ControlPoints,
    long ExpectedVersion);

public sealed record DeletedSourceMapResult(StoredOverworld World, SourceMapRepresentation SourceMap);

public sealed class SourceMapApplicationService(IHexCrawlStore store)
{
    public async Task<StoredOverworld> GetOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        await store.GetOverworldAsync(overworldId, RequireUser(ownerUserId), cancellationToken)
        ?? throw new HexCrawlNotFoundException("Overworld was not found.");

    public async Task<IReadOnlyList<SourceMapRepresentation>> ListAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        (await GetOverworldAsync(overworldId, ownerUserId, cancellationToken)).World.SourceMaps;

    public async Task<StoredOverworld> CreateUploadedAsync(
        Guid overworldId,
        string ownerUserId,
        UploadSourceMapCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        if (command.PixelWidth <= 0 || command.PixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.PixelWidth), "Source-map pixel dimensions must be positive.");
        }

        var map = new SourceMapRepresentation(
            Guid.NewGuid(),
            RequiredText(command.GeographyKey, "Geography key"),
            RequiredText(command.Name, "Source-map name"),
            command.Role,
            RequiredText(command.AssetKey, "Asset key"),
            command.ContainsBakedGrid,
            null,
            [],
            command.PixelWidth,
            command.PixelHeight,
            RequiredText(command.MediaType, "Media type"),
            string.IsNullOrWhiteSpace(command.OriginalFileName) ? null : command.OriginalFileName.Trim());

        return await SaveAsync(current with
        {
            World = current.World with { SourceMaps = [.. current.World.SourceMaps, map] }
        }, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> UpdateMetadataAsync(
        Guid overworldId,
        Guid sourceMapId,
        string ownerUserId,
        UpdateSourceMapMetadataCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var existing = Find(current, sourceMapId);
        var replacement = existing with
        {
            GeographyKey = RequiredText(command.GeographyKey, "Geography key"),
            Name = RequiredText(command.Name, "Source-map name"),
            Role = command.Role,
            ContainsBakedGrid = command.ContainsBakedGrid
        };
        return await ReplaceAsync(current, sourceMapId, replacement, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> RegisterAsync(
        Guid overworldId,
        Guid sourceMapId,
        string ownerUserId,
        RegisterSourceMapCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var existing = Find(current, sourceMapId);
        if (existing.PixelWidth <= 0 || existing.PixelHeight <= 0)
        {
            throw new InvalidOperationException("This source map predates raster-dimension metadata and can not be registered until it is re-imported.");
        }

        var transform = AffineRegistrationSolver.Solve(command.ControlPoints);
        var replacement = existing with
        {
            Alignment = transform,
            WorldCoverageBoundary = AffineRegistrationSolver.Coverage(transform, existing.PixelWidth, existing.PixelHeight)
        };
        return await ReplaceAsync(current, sourceMapId, replacement, command.ExpectedVersion, cancellationToken);
    }

    public async Task<DeletedSourceMapResult> DeleteAsync(
        Guid overworldId,
        Guid sourceMapId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        var existing = Find(current, sourceMapId);
        var updated = await SaveAsync(current with
        {
            World = current.World with
            {
                SourceMaps = current.World.SourceMaps.Where(item => item.Id != sourceMapId).ToArray()
            }
        }, expectedVersion, cancellationToken);
        return new DeletedSourceMapResult(updated, existing);
    }

    private async Task<StoredOverworld> ReplaceAsync(
        StoredOverworld current,
        Guid sourceMapId,
        SourceMapRepresentation replacement,
        long expectedVersion,
        CancellationToken cancellationToken) =>
        await SaveAsync(current with
        {
            World = current.World with
            {
                SourceMaps = current.World.SourceMaps.Select(item => item.Id == sourceMapId ? replacement : item).ToArray()
            }
        }, expectedVersion, cancellationToken);

    private async Task<StoredOverworld> SaveAsync(
        StoredOverworld world,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await store.SaveOverworldAsync(world, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.NotFound => throw new HexCrawlNotFoundException("Overworld was not found."),
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException("The overworld changed before the source-map update could be saved."),
            _ => throw new InvalidOperationException("Unexpected persistence save outcome.")
        };
    }

    private static SourceMapRepresentation Find(StoredOverworld world, Guid sourceMapId) =>
        world.World.SourceMaps.FirstOrDefault(item => item.Id == sourceMapId)
        ?? throw new HexCrawlNotFoundException("Source-map representation was not found.");

    private static void RequireVersion(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new HexCrawlConcurrencyException($"Expected overworld version {expected}, but current version is {actual}.");
        }
    }

    private static string RequireUser(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new UnauthorizedAccessException("A user identity is required.") : value.Trim();

    private static string RequiredText(string value, string label) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{label} is required.") : value.Trim();
}
