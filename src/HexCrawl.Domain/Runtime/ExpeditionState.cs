using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed record NavigationRuntimeState(bool IsLost, double VeerDegrees);

public sealed record ExpeditionState
{
    public required Guid Id { get; init; }
    public required Guid OverworldId { get; init; }
    public required WorldPoint Position { get; init; }
    public required HexCoordinate CurrentHex { get; init; }
    public double? IntendedBearingDegrees { get; init; }
    public double? ActualBearingDegrees { get; init; }
    public NavigationRuntimeState Navigation { get; init; } = new(false, 0);
    public required DistanceMeasure DistanceTraveled { get; init; }
    public required DistanceMeasure DistanceSinceEnteringHex { get; init; }
    public TimeSpan ElapsedTravelTime { get; init; }
}
