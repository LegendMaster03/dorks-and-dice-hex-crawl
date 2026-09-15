namespace HexCrawl.Domain.Spatial;

public readonly record struct HexCoordinate(int Q, int R)
{
    private static readonly HexCoordinate[] NeighborOffsets =
    [
        new(1, 0), new(1, -1), new(0, -1),
        new(-1, 0), new(-1, 1), new(0, 1)
    ];

    public int S => -Q - R;

    public HexCoordinate Neighbor(int direction)
    {
        if (direction is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Hex direction must be between 0 and 5.");
        }

        var offset = NeighborOffsets[direction];
        return new HexCoordinate(Q + offset.Q, R + offset.R);
    }

    public IReadOnlyList<HexCoordinate> Neighbors()
    {
        var q = Q;
        var r = R;
        return NeighborOffsets.Select(offset => new HexCoordinate(q + offset.Q, r + offset.R)).ToArray();
    }

    public int DistanceTo(HexCoordinate other) =>
        (Math.Abs(Q - other.Q) + Math.Abs(R - other.R) + Math.Abs(S - other.S)) / 2;

    public override string ToString() => $"{Q},{R}";
}
