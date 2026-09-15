namespace HexCrawl.Domain.Spatial;

public enum HexOrientation
{
    PointyTop,
    FlatTop
}

public enum HexCoordinateConvention
{
    AxialQr
}

public sealed record HexGridDefinition
{
    public required Guid Id { get; init; }
    public HexOrientation Orientation { get; init; } = HexOrientation.PointyTop;
    public HexCoordinateConvention CoordinateConvention { get; init; } = HexCoordinateConvention.AxialQr;
    public WorldPoint Origin { get; init; } = new(0, 0);
    public double RotationDegrees { get; init; }
    public double HexRadiusWorldUnits { get; init; } = 1;
    public required DistanceMeasure NeighborCenterDistance { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Grid identity must be stable and non-empty.");
        }

        if (HexRadiusWorldUnits <= 0 || double.IsNaN(HexRadiusWorldUnits) || double.IsInfinity(HexRadiusWorldUnits))
        {
            throw new InvalidOperationException("Hex radius must be finite and positive.");
        }

        if (CoordinateConvention != HexCoordinateConvention.AxialQr)
        {
            throw new NotSupportedException($"Coordinate convention {CoordinateConvention} is not implemented.");
        }
    }
}

public readonly record struct HexId(Guid GridId, HexCoordinate Coordinate)
{
    public override string ToString() => $"{GridId:N}:{Coordinate.Q}:{Coordinate.R}";
}
