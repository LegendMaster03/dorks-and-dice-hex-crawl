using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Runtime;

public enum WorldObjectActivityState
{
    Active,
    Inactive,
    Depleted,
    Destroyed
}

public sealed record LocationRuntimeState(Guid LocationId, WorldObjectActivityState State, string? Note = null);

public sealed record FeatureRuntimeState(Guid FeatureId, WorldObjectActivityState State, string? Note = null);

public sealed record WorldRuntimeState
{
    public required Guid Id { get; init; }
    public required Guid OverworldId { get; init; }
    public IReadOnlyDictionary<Guid, LocationRuntimeState> Locations { get; init; } = new Dictionary<Guid, LocationRuntimeState>();
    public IReadOnlyDictionary<Guid, FeatureRuntimeState> Features { get; init; } = new Dictionary<Guid, FeatureRuntimeState>();
    public IReadOnlySet<Guid> VisitedLocations { get; init; } = new HashSet<Guid>();
    public IReadOnlyList<SpatialFeature> GeneratedFeatures { get; init; } = [];
    public IReadOnlyList<Location> GeneratedLocations { get; init; } = [];
}
