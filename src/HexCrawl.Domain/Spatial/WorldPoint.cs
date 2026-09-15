namespace HexCrawl.Domain.Spatial;

public readonly record struct WorldPoint(double X, double Y)
{
    public static WorldPoint operator +(WorldPoint left, WorldPoint right) => new(left.X + right.X, left.Y + right.Y);
    public static WorldPoint operator -(WorldPoint left, WorldPoint right) => new(left.X - right.X, left.Y - right.Y);
    public static WorldPoint operator *(WorldPoint point, double scale) => new(point.X * scale, point.Y * scale);

    public double DistanceTo(WorldPoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
