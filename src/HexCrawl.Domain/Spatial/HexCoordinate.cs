namespace HexCrawl.Domain.Spatial;

public readonly record struct HexCoordinate(int Q, int R)
{
    public override string ToString() => $"{Q},{R}";
}
