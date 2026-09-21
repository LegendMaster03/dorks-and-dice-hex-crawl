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

    public async Task<StoredOverworld> CreateOverworldAsync(
        string ownerUserId,
        CreateOverworldCommand command,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireUser(ownerUserId);
        var name = RequiredText(command.Name, "Overworld name");
        ValidatePoint(command.Origin, "Grid origin");
        if (command.NeighborCenterDistance <= 0 || !double.IsFinite(command.NeighborCenterDistance))
        {
            throw new ArgumentOutOfRangeException(nameof(command.NeighborCenterDistance), "Grid scale must be finite and positive.");
        }

        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            Orientation = command.Orientation,
            Origin = command.Origin,
            RotationDegrees = command.RotationDegrees,
            HexRadiusWorldUnits = command.HexRadiusWorldUnits,
            NeighborCenterDistance = new DistanceMeasure(command.NeighborCenterDistance, command.DistanceUnit)
        };
        grid.Validate();

        var now = DateTimeOffset.UtcNow;
        return await _store.CreateOverworldAsync(new StoredOverworld(
            new OverworldDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                Grid = grid
            },
            owner,
            1,
            now,
            now), cancellationToken);
    }

    public async Task<StoredOverworld> UpdateOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        UpdateOverworldCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var name = RequiredText(command.Name, "Overworld name");
        command.Grid.Validate();
        if (command.Grid.Id != current.World.Grid.Id)
        {
            throw new InvalidOperationException("Grid identity can not be replaced during an overworld update.");
        }

        if (command.Grid != current.World.Grid
            && await _store.HasExpeditionsAsync(overworldId, current.OwnerUserId, cancellationToken))
        {
            throw new HexCrawlConflictException("Grid geometry can not be changed after an expedition has been created for this overworld.");
        }

        var updated = current with { World = current.World with { Name = name, Grid = command.Grid } };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }


}
