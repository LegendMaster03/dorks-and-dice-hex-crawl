using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application;

public sealed class HexCrawlService(IHexCrawlStore store)
{
    private readonly CrawlRuntimeEngine _runtime = new();

    public Task<IReadOnlyList<OverworldSummary>> ListOverworldsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        store.ListOverworldsAsync(RequireUser(ownerUserId), cancellationToken);

    public async Task<StoredOverworld> GetOverworldAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        await store.GetOverworldAsync(overworldId, RequireUser(ownerUserId), cancellationToken)
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
        return await store.CreateOverworldAsync(new StoredOverworld(
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
            && await store.HasExpeditionsAsync(overworldId, current.OwnerUserId, cancellationToken))
        {
            throw new HexCrawlConflictException("Grid geometry can not be changed after an expedition has been created for this overworld.");
        }

        var updated = current with { World = current.World with { Name = name, Grid = command.Grid } };
        return await SaveWorldAsync(updated, command.ExpectedVersion, cancellationToken);
    }

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
        if (!current.World.SourceMaps.Any(item => item.Id == sourceMapId))
        {
            throw new HexCrawlNotFoundException("Source-map representation was not found.");
        }
        ValidatePoints(command.WorldCoverageBoundary, "Source-map coverage");
        var replacement = new SourceMapRepresentation(
            sourceMapId,
            RequiredText(command.GeographyKey, "Geography key"),
            RequiredText(command.Name, "Source-map name"),
            command.Role,
            RequiredAssetKey(command.AssetKey),
            command.ContainsBakedGrid,
            command.Alignment,
            command.WorldCoverageBoundary ?? []);
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

    public Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        string ownerUserId,
        CancellationToken cancellationToken = default) =>
        store.ListExpeditionsAsync(RequireUser(ownerUserId), cancellationToken);

    public async Task<IReadOnlyList<ExpeditionSummary>> ListExpeditionsAsync(
        Guid overworldId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var world = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        return await store.ListExpeditionsAsync(overworldId, world.OwnerUserId, cancellationToken);
    }

    public async Task<StoredExpedition> GetExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var owner = RequireUser(ownerUserId);
        var expedition = await store.GetExpeditionAsync(expeditionId, owner, cancellationToken)
            ?? throw new HexCrawlNotFoundException("Expedition was not found.");
        _ = await GetOverworldAsync(expedition.State.OverworldId, owner, cancellationToken);
        return expedition;
    }

    public async Task<StoredExpedition> StartExpeditionAsync(
        Guid overworldId,
        string ownerUserId,
        StartExpeditionCommand command,
        CancellationToken cancellationToken = default)
    {
        var world = await GetOverworldAsync(overworldId, ownerUserId, cancellationToken);
        var profile = CrawlProcedureCatalog.Resolve(command.ProcedureKey);
        var expeditionId = Guid.NewGuid();
        var state = new ExpeditionState
        {
            Id = expeditionId,
            OverworldId = world.World.Id,
            Position = HexGeometry.HexToWorld(world.World.Grid, command.StartHex),
            PositionPrecision = WorldPositionPrecision.HexAnchor,
            Traversal = HexTraversalState.StartingIn(command.StartHex, world.World.Grid.NeighborCenterDistance.Unit),
            Navigation = new NavigationRuntimeState(false, 0),
            DistanceTraveled = new DistanceMeasure(0, world.World.Grid.NeighborCenterDistance.Unit)
        };
        var knowledge = new PlayerKnowledgeState
        {
            ScopeId = Guid.NewGuid(),
            OverworldId = world.World.Id
        };
        var now = DateTimeOffset.UtcNow;
        return await store.CreateExpeditionAsync(new StoredExpedition(
            RequiredText(command.Name, "Expedition name"),
            state,
            knowledge,
            profile,
            null,
            TimeSpan.Zero,
            world.OwnerUserId,
            1,
            now,
            now), cancellationToken);
    }

    public async Task<StoredExpedition> AdvanceExpeditionAsync(
        Guid expeditionId,
        string ownerUserId,
        AdvanceExpeditionCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var world = await GetOverworldAsync(expedition.State.OverworldId, ownerUserId, cancellationToken);
        var provenance = new ResolutionProvenance(command.ResolutionSource, command.DmOverrideNote);
        var profile = expedition.Procedure;
        var travel = BuildTravel(profile, world.World.Grid.NeighborCenterDistance.Unit, command, provenance);
        var navigation = BuildNavigation(profile, expedition.State, command, provenance);
        var encounter = BuildEncounter(profile, expedition.State, command, provenance);
        var boundaryDecision = command.RecognizedLost.HasValue || command.Reorient.HasValue
            ? new BoundaryNavigationDecision(command.RecognizedLost ?? false, command.Reorient ?? false, provenance)
            : null;
        var plan = new WatchTravelPlan(
            new HexDirection(command.IntendedDirection),
            new TravelModeSelection(RequiredText(command.PaceKey, "Pace key"), command.Activities ?? []),
            new NavigationAidSelection(
                string.IsNullOrWhiteSpace(command.NavigationAidKey) ? "none" : command.NavigationAidKey.Trim(),
                command.SuppressesNavigationCheck,
                command.ResetsVeerAtBoundary),
            command.DeliberateDoubleBack,
            command.ContinueAcrossBoundaries);
        var result = _runtime.Advance(
            world.World,
            profile,
            expedition.State,
            expedition.Knowledge,
            plan,
            new WatchAdvanceInputs(travel, navigation, encounter, boundaryDecision, command.DmOverrideNote));
        var updated = expedition with
        {
            State = result.Expedition,
            Knowledge = result.Knowledge,
            PauseReason = result.PauseReason,
            RemainingWatchTime = result.RemainingWatchTime
        };
        return await SaveExpeditionAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredExpedition> DiscoverAsync(
        Guid expeditionId,
        string ownerUserId,
        DiscoverSubjectCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        RequireVersion(command.ExpectedVersion, expedition.Version);
        var world = await GetOverworldAsync(expedition.State.OverworldId, ownerUserId, cancellationToken);
        var result = CrawlRuntimeActions.Discover(
            world.World,
            expedition.State,
            expedition.Knowledge,
            command.SubjectId,
            command.SubjectType,
            string.IsNullOrWhiteSpace(command.Source) ? "dm:manual-discovery" : command.Source.Trim());
        var updated = expedition with { State = result.Expedition, Knowledge = result.Knowledge };
        return await SaveExpeditionAsync(updated, command.ExpectedVersion, cancellationToken);
    }

    private async Task EnsureSemanticDeletionIsSafeAsync(StoredOverworld world, CancellationToken cancellationToken)
    {
        if (await store.HasExpeditionsAsync(world.World.Id, world.OwnerUserId, cancellationToken))
        {
            throw new HexCrawlConflictException(
                "Locations and features can not be deleted after an expedition has been created because persisted history or knowledge may reference their stable IDs.");
        }
    }

    private async Task<StoredOverworld> SaveWorldAsync(
        StoredOverworld world,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await store.SaveOverworldAsync(world, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException("The overworld was changed by another request. Reload it before saving again."),
            _ => throw new HexCrawlNotFoundException("Overworld was not found.")
        };
    }

    private async Task<StoredExpedition> SaveExpeditionAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var result = await store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException("The expedition was changed by another request. Reload it before advancing again."),
            _ => throw new HexCrawlNotFoundException("Expedition was not found.")
        };
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

    private static ResolvedTravelAmount BuildTravel(
        CrawlProcedureProfile profile,
        DistanceUnit unit,
        AdvanceExpeditionCommand command,
        ResolutionProvenance provenance)
    {
        if (profile.TravelResolution == TravelResolutionMode.HexSteps)
        {
            return command.HexSteps.HasValue
                ? ResolvedTravelAmount.Steps(command.HexSteps.Value, provenance)
                : throw new InvalidOperationException("The selected procedure requires a resolved hex-step count.");
        }
        if (!command.ExpectedDistance.HasValue || !command.ActualDistance.HasValue)
        {
            throw new InvalidOperationException("The selected procedure requires expected and actual travel distance.");
        }
        return ResolvedTravelAmount.Distance(
            new DistanceMeasure(command.ExpectedDistance.Value, unit),
            new DistanceMeasure(command.ActualDistance.Value, unit),
            provenance);
    }

    private static ResolvedNavigation? BuildNavigation(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        AdvanceExpeditionCommand command,
        ResolutionProvenance provenance)
    {
        if (state.ActiveWatch is not null
            || !profile.UsesNavigationChecks
            || command.SuppressesNavigationCheck
            || command.DeliberateDoubleBack)
        {
            return null;
        }
        var outcome = command.NavigationOutcome
            ?? throw new InvalidOperationException("This watch requires an explicit navigation outcome.");
        if (outcome == NavigationCheckOutcome.NotRequired)
        {
            throw new InvalidOperationException("Navigation outcome must be Succeeded or Failed when a check is required.");
        }
        return new ResolvedNavigation(
            outcome,
            outcome == NavigationCheckOutcome.Failed ? command.VeerSteps : null,
            provenance);
    }

    private static ResolvedEncounter? BuildEncounter(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        AdvanceExpeditionCommand command,
        ResolutionProvenance provenance)
    {
        if (state.ActiveWatch is not null || profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return null;
        }
        var kind = command.EncounterOutcome ?? EncounterOutcomeKind.None;
        TimeSpan? occursAt = kind == EncounterOutcomeKind.None
            ? null
            : TimeSpan.FromHours(command.EncounterHour
                ?? throw new InvalidOperationException("A triggered encounter requires an encounter hour."));
        return new ResolvedEncounter(kind, occursAt, command.LocationId, command.EncounterNote, provenance);
    }

    private static string RequireUser(string value) => RequiredText(value, "User identity");

    private static string RequiredAssetKey(string value)
    {
        var key = RequiredText(value, "Asset key");
        if (Path.IsPathRooted(key))
        {
            throw new ArgumentException("Source-map asset keys must be deployment-independent logical references, not absolute machine paths.", nameof(value));
        }
        return key;
    }

    private static string RequiredText(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"{label} is required.");

    private static void RequireVersion(long expected, long actual)
    {
        if (expected != actual)
        {
            throw new HexCrawlConcurrencyException("The resource version is stale. Reload it before saving again.");
        }
    }

    private static void ValidatePoint(WorldPoint point, string label)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentException($"{label} must contain finite coordinates.");
        }
    }

    private static void ValidatePoints(IReadOnlyList<WorldPoint>? points, string label)
    {
        if (points is null)
        {
            return;
        }
        foreach (var point in points)
        {
            ValidatePoint(point, label);
        }
    }
}

public sealed class HexCrawlNotFoundException(string message) : Exception(message);
public sealed class HexCrawlConcurrencyException(string message) : Exception(message);
public sealed class HexCrawlConflictException(string message) : Exception(message);
