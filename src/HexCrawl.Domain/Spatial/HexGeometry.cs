namespace HexCrawl.Domain.Spatial;

public static class HexGeometry
{
    private const double Sqrt3 = 1.7320508075688772935;

    public static WorldPoint HexToWorld(HexGridDefinition grid, HexCoordinate hex)
    {
        grid.Validate();
        var radius = grid.HexRadiusWorldUnits;

        var local = grid.Orientation switch
        {
            HexOrientation.PointyTop => new WorldPoint(
                radius * Sqrt3 * (hex.Q + (hex.R / 2d)),
                radius * 1.5d * hex.R),
            HexOrientation.FlatTop => new WorldPoint(
                radius * 1.5d * hex.Q,
                radius * Sqrt3 * (hex.R + (hex.Q / 2d))),
            _ => throw new ArgumentOutOfRangeException(nameof(grid.Orientation))
        };

        return Rotate(local, grid.RotationDegrees) + grid.Origin;
    }

    public static IReadOnlyList<WorldPoint> Corners(HexGridDefinition grid, HexCoordinate hex)
    {
        var center = HexToWorld(grid, hex);
        var startAngle = grid.Orientation == HexOrientation.PointyTop ? -30d : 0d;
        var corners = new WorldPoint[6];

        for (var i = 0; i < 6; i++)
        {
            var angle = DegreesToRadians(startAngle + (60d * i) + grid.RotationDegrees);
            corners[i] = new WorldPoint(
                center.X + (grid.HexRadiusWorldUnits * Math.Cos(angle)),
                center.Y + (grid.HexRadiusWorldUnits * Math.Sin(angle)));
        }

        return corners;
    }

    private static WorldPoint Rotate(WorldPoint point, double degrees)
    {
        if (degrees == 0)
        {
            return point;
        }

        var radians = DegreesToRadians(degrees);
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new WorldPoint((point.X * cos) - (point.Y * sin), (point.X * sin) + (point.Y * cos));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
