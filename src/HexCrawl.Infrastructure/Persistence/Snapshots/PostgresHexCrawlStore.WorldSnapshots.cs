using System.Text.Json.Serialization;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Infrastructure.Persistence;

public sealed partial class PostgresHexCrawlStore
{
    private sealed record FeatureSnapshot(
        Guid Id,
        string Name,
        string Category,
        SpatialFeatureKind Kind,
        WorldPoint? Position,
        IReadOnlyList<WorldPoint>? Path,
        IReadOnlyList<WorldPoint>? Boundary)
    {
        public static FeatureSnapshot FromDomain(SpatialFeature feature) => feature switch
        {
            PointFeature point => new(point.Id, point.Name, point.Category, point.Kind, point.Position, null, null),
            LinearFeature line => new(line.Id, line.Name, line.Category, line.Kind, null, line.Path, null),
            RegionFeature region => new(region.Id, region.Name, region.Category, region.Kind, null, null, region.Boundary),
            _ => throw new ArgumentOutOfRangeException(nameof(feature))
        };

        public SpatialFeature ToDomain() => Kind switch
        {
            SpatialFeatureKind.Point => new PointFeature(Id, Name, Category, Position ?? throw new InvalidDataException("Persisted point feature has no position.")),
            SpatialFeatureKind.Line => new LinearFeature(Id, Name, Category, Path ?? throw new InvalidDataException("Persisted line feature has no path.")),
            SpatialFeatureKind.Region => new RegionFeature(Id, Name, Category, Boundary ?? throw new InvalidDataException("Persisted region feature has no boundary.")),
            _ => throw new InvalidDataException("Persisted feature kind is not supported.")
        };
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record WorldSnapshot(
        Guid Id,
        string Name,
        HexGridDefinition? Grid,
        IReadOnlyList<FeatureSnapshot> Features,
        IReadOnlyList<Location> Locations,
        IReadOnlyList<SourceMapRepresentation> SourceMaps,
        IReadOnlyList<EnvironmentAnnotation> EnvironmentAnnotations,
        PeriodicWorldTiling? Tiling = null,
        int FormatVersion = 1)
    {
        public static WorldSnapshot FromDomain(OverworldDefinition world)
        {
            world.ValidateEnvironmentAnnotations();
            return new WorldSnapshot(
                world.Id,
                world.Name,
                world.HasLegacyHexGrid ? world.Grid : null,
                world.Features.Select(FeatureSnapshot.FromDomain).ToArray(),
                world.Locations,
                world.SourceMaps,
                world.EnvironmentAnnotations,
                world.Tiling,
                world.HasLegacyHexGrid ? 1 : 2);
        }

        public OverworldDefinition ToDomain()
        {
            if ((FormatVersion == 1 && (Grid is null || Tiling is not null))
                || (FormatVersion == 2 && (Grid is not null || Tiling is null))
                || FormatVersion is < 1 or > 2)
                throw new InvalidDataException(
                    "Unsupported or inconsistent persisted world format. The original record has been preserved.");
            var world = new OverworldDefinition
            {
                Id = Id,
                Name = Name,
                Grid = Grid!,
                Tiling = Tiling,
                Features = Features.Select(item => item.ToDomain()).ToArray(),
                Locations = Locations.ToArray(),
                SourceMaps = SourceMaps.ToArray(),
                EnvironmentAnnotations = EnvironmentAnnotations.ToArray()
            };
            world.ValidateEnvironmentAnnotations();
            return world;
        }
    }
}
