using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public enum LocationDiscoverability
{
    Obvious,
    Hidden,
    Conditional
}

public sealed record LocationDetailMapReference(Guid Id, string Kind, string ReferenceKey);

public sealed record Location(
    Guid Id,
    string Name,
    string Category,
    WorldPoint Position,
    LocationDiscoverability Discoverability,
    IReadOnlyList<LocationDetailMapReference> DetailMaps);
