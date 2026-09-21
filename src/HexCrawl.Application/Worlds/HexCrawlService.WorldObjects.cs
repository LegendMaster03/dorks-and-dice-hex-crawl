using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed partial class HexCrawlService
{
    public async Task<StoredOverworld> CreateLocationAsync(
        Guid overworldId,
        string ownerUserId,
        CreateLocationCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        ValidatePoint(command.Position, "Location position");
        var location = new Location(
            Guid.NewGuid(),
            RequiredText(command.Name, "Location name"),
            RequiredText(command.Category, "Location category"),
            command.Position,
            command.Discoverability,
            []);
        var updated = current with
        {
            World = current.World with { Locations = [.. current.World.Locations, location] }
        };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> UpdateLocationAsync(
        Guid overworldId,
        Guid locationId,
        string ownerUserId,
        UpdateLocationCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        ValidatePoint(command.Position, "Location position");
        var existing = current.World.Locations.FirstOrDefault(item => item.Id == locationId)
            ?? throw new HexCrawlNotFoundException("Location was not found.");
        var replacement = existing with
        {
            Name = RequiredText(command.Name, "Location name"),
            Category = RequiredText(command.Category, "Location category"),
            Position = command.Position,
            Discoverability = command.Discoverability
        };
        var updated = current with
        {
            World = current.World with
            {
                Locations = current.World.Locations.Select(item => item.Id == locationId ? replacement : item).ToArray()
            }
        };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> DeleteLocationAsync(
        Guid overworldId,
        Guid locationId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        if (!current.World.Locations.Any(item => item.Id == locationId))
        {
            throw new HexCrawlNotFoundException("Location was not found.");
        }
        await EnsureSemanticDeletionIsSafeAsync(current, cancellationToken);
        var updated = current with
        {
            World = current.World with { Locations = current.World.Locations.Where(item => item.Id != locationId).ToArray() }
        };
        return await SaveWorldAsync(updated, expectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> CreateFeatureAsync(
        Guid overworldId,
        string ownerUserId,
        CreateFeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        var feature = BuildFeature(Guid.NewGuid(), command.Name, command.Category, command.Kind, command.Position, command.Path, command.Boundary);
        var updated = current with
        {
            World = current.World with { Features = [.. current.World.Features, feature] }
        };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> ImportWorldObjectsAsync(
        Guid overworldId,
        string ownerUserId,
        ImportWorldObjectsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        if (command.Locations.Count == 0 && command.Features.Count == 0)
        {
            throw new ArgumentException("At least one semantic world object is required for import.", nameof(command));
        }

        var locations = command.Locations.Select(definition =>
        {
            ValidatePoint(definition.Position, "Imported location position");
            return new Location(
                Guid.NewGuid(),
                RequiredText(definition.Name, "Imported location name"),
                RequiredText(definition.Category, "Imported location category"),
                definition.Position,
                definition.Discoverability,
                []);
        }).ToArray();

        var features = command.Features.Select(definition =>
            BuildFeature(
                Guid.NewGuid(),
                definition.Name,
                definition.Category,
                definition.Kind,
                definition.Position,
                definition.Path,
                definition.Boundary)).ToArray();

        var updated = current with
        {
            World = current.World with
            {
                Locations = [.. current.World.Locations, .. locations],
                Features = [.. current.World.Features, .. features]
            }
        };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> UpdateFeatureAsync(
        Guid overworldId,
        Guid featureId,
        string ownerUserId,
        UpdateFeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, current.Version);
        if (!current.World.Features.Any(item => item.Id == featureId))
        {
            throw new HexCrawlNotFoundException("Feature was not found.");
        }
        var replacement = BuildFeature(featureId, command.Name, command.Category, command.Kind, command.Position, command.Path, command.Boundary);
        var updated = current with
        {
            World = current.World with
            {
                Features = current.World.Features.Select(item => item.Id == featureId ? replacement : item).ToArray()
            }
        };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredOverworld> DeleteFeatureAsync(
        Guid overworldId,
        Guid featureId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var current = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        RequireVersion(expectedVersion, current.Version);
        if (!current.World.Features.Any(item => item.Id == featureId))
        {
            throw new HexCrawlNotFoundException("Feature was not found.");
        }
        await EnsureSemanticDeletionIsSafeAsync(current, cancellationToken);
        var updated = current with
        {
            World = current.World with { Features = current.World.Features.Where(item => item.Id != featureId).ToArray() }
        };
        return await SaveWorldAsync(updated, expectedVersion, cancellationToken);
    }

    private async Task EnsureSemanticDeletionIsSafeAsync(StoredOverworld world, CancellationToken cancellationToken)
    {
        if (await _store.HasExpeditionsAsync(world.World.Id, world.OwnerUserId, cancellationToken))
        {
            throw new HexCrawlConflictException(
                "Locations and features can not be deleted after an expedition has been created because persisted history or knowledge may reference their stable IDs.");
        }
    }

    private static SpatialFeature BuildFeature(
        Guid id,
        string name,
        string category,
        SpatialFeatureKind kind,
        WorldPoint? position,
        IReadOnlyList<WorldPoint>? path,
        IReadOnlyList<WorldPoint>? boundary)
    {
        name = RequiredText(name, "Feature name");
        category = RequiredText(category, "Feature category");
        return kind switch
        {
            SpatialFeatureKind.Point when position.HasValue => BuildPointFeature(id, name, category, position.Value),
            SpatialFeatureKind.Line when path is { Count: >= 2 } => BuildLineFeature(id, name, category, path),
            SpatialFeatureKind.Region when boundary is { Count: >= 3 } => BuildRegionFeature(id, name, category, boundary),
            SpatialFeatureKind.Point => throw new ArgumentException("Point features require a position."),
            SpatialFeatureKind.Line => throw new ArgumentException("Linear features require at least two path vertices."),
            SpatialFeatureKind.Region => throw new ArgumentException("Region features require at least three boundary vertices."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static PointFeature BuildPointFeature(Guid id, string name, string category, WorldPoint position)
    {
        ValidatePoint(position, "Point-feature position");
        return new PointFeature(id, name, category, position);
    }

    private static LinearFeature BuildLineFeature(Guid id, string name, string category, IReadOnlyList<WorldPoint> path)
    {
        ValidatePoints(path, "Linear-feature path");
        return new LinearFeature(id, name, category, path.ToArray());
    }

    private static RegionFeature BuildRegionFeature(Guid id, string name, string category, IReadOnlyList<WorldPoint> boundary)
    {
        ValidatePoints(boundary, "Region-feature boundary");
        var twiceArea = 0d;
        for (var index = 0; index < boundary.Count; index++)
        {
            var current = boundary[index];
            var next = boundary[(index + 1) % boundary.Count];
            twiceArea += (current.X * next.Y) - (next.X * current.Y);
        }
        if (Math.Abs(twiceArea) < 1e-9)
        {
            throw new ArgumentException("Region-feature boundary must enclose a non-zero area.", nameof(boundary));
        }
        return new RegionFeature(id, name, category, boundary.ToArray());
    }


}
