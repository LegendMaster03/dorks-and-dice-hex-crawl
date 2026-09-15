using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed record NavigationRuntimeState(bool IsLost, int VeerSteps)
{
    public double VeerDegrees => VeerSteps * 60d;
}

public enum WorldPositionPrecision
{
    Exact,
    HexAnchor
}

public sealed record ExpeditionState
{
    public required Guid Id { get; init; }
    public required Guid OverworldId { get; init; }

    // World position is retained for maps and future exact positioning, but crawl
    // procedure resolution uses Traversal rather than ray-casting this point.
    public required WorldPoint Position { get; init; }
    public WorldPositionPrecision PositionPrecision { get; init; } = WorldPositionPrecision.Exact;

    public required HexTraversalState Traversal { get; init; }
    public HexCoordinate CurrentHex => Traversal.CurrentHex;
    public DistanceMeasure DistanceSinceEnteringHex => Traversal.Progress;

    public HexDirection? IntendedDirection { get; init; }
    public HexDirection? ActualDirection { get; init; }
    public double? IntendedBearingDegrees => IntendedDirection?.Value * 60d;
    public double? ActualBearingDegrees => ActualDirection?.Value * 60d;

    public NavigationRuntimeState Navigation { get; init; } = new(false, 0);
    public required DistanceMeasure DistanceTraveled { get; init; }
    public TimeSpan ElapsedTravelTime { get; init; }
    public int CompletedWatches { get; init; }
    public ActiveWatchState? ActiveWatch { get; init; }
    public IReadOnlyList<CrawlRuntimeEvent> History { get; init; } = [];
}
