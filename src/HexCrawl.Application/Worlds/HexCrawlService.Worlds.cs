using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed partial class HexCrawlService
{
    public Task<IReadOnlyList<OverworldSummary>> ListOverworldsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        _store.ListOverworldsAsync(RequireUser(ownerUserId), cancellationToken);

    public async Task<StoredOverworld> GetOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        await _store.GetOverworldAsync(overworldId, RequireUser(ownerUserId), cancellationToken)
        ?? throw new HexCrawlNotFoundException("Overworld was not found.");

    public async Task<StoredOverworld> CreatePeriodicOverworldAsync(
        string ownerUserId,
        string name,
        PeriodicWorldTiling tiling,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireUser(ownerUserId);
        var title = RequiredText(name, "Overworld name");
        ArgumentNullException.ThrowIfNull(tiling);
        tiling.Validate();
        var now = DateTimeOffset.UtcNow;
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = title,
            Tiling = tiling
        };
        world.ValidateEnvironmentAnnotations();
        return await _store.CreateOverworldAsync(new StoredOverworld(
            world, owner, 1, now, now), cancellationToken);
    }

    public async Task<StoredOverworld> RenameOverworldAsync(
        Guid overworldId, string ownerUserId, string name, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        var updated = current with
        {
            World = current.World with { Name = RequiredText(name, "Overworld name") }
        };
        return await SaveWorldAsync(updated, expectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> DeleteOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        var outcome = await _store.DeleteOverworldAsync(
            overworldId,
            current.OwnerUserId,
            expectedVersion,
            cancellationToken);
        return outcome switch
        {
            DeleteOverworldOutcome.Deleted => current,
            DeleteOverworldOutcome.NotFound => throw new HexCrawlNotFoundException("Overworld was not found."),
            DeleteOverworldOutcome.Conflict => throw new HexCrawlConcurrencyException("The overworld was changed by another request. Reload it before deleting it."),
            DeleteOverworldOutcome.HasExpeditions => throw new HexCrawlConflictException("The overworld can not be deleted while it has saved expeditions. Remove those expeditions first."),
            _ => throw new InvalidOperationException("The overworld delete operation returned an unknown result.")
        };
    }
}
