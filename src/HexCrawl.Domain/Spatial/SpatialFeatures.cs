namespace HexCrawl.Domain.Spatial;

public enum SpatialFeatureKind
{
    Point,
    Line,
    Region
}

public abstract record SpatialFeature(Guid Id, string Name, string Category)
{
    public abstract SpatialFeatureKind Kind { get; }
}

public sealed record PointFeature(Guid Id, string Name, string Category, WorldPoint Position)
    : SpatialFeature(Id, Name, Category)
{
    public override SpatialFeatureKind Kind => SpatialFeatureKind.Point;
}

public sealed record LinearFeature(Guid Id, string Name, string Category, IReadOnlyList<WorldPoint> Path)
    : SpatialFeature(Id, Name, Category)
{
    public override SpatialFeatureKind Kind => SpatialFeatureKind.Line;
}

public sealed record RegionFeature(Guid Id, string Name, string Category, IReadOnlyList<WorldPoint> Boundary)
    : SpatialFeature(Id, Name, Category)
{
    public override SpatialFeatureKind Kind => SpatialFeatureKind.Region;
}
