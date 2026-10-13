using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed record MaplessCellSessionStart(
    PeriodicWorldTiling World, CellExpeditionState Expedition);

public static class MaplessCellSessionGeometry
{
    public static MaplessCellSessionStart Create(
        Guid sessionId, PeriodicWorldTiling tiling,
        PeriodicCellAddress? startingCell = null)
    {
        ArgumentNullException.ThrowIfNull(tiling);
        tiling.Validate();
        if (sessionId == Guid.Empty || tiling.Id != sessionId)
            throw new ArgumentException("Mapless tiling identity must equal the session identity.");
        var address = startingCell ?? new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0));
        var cell = tiling.Resolve(address);
        var state = new CellExpeditionState
        {
            Id = sessionId,
            Traversal = new PeriodicCellTraversal
            {
                CurrentCell = cell.Id,
                Position = cell.Center
            },
            DistanceTraveled = tiling.PhysicalDistancePerWorldUnit is { } calibration
                ? new DistanceMeasure(0, calibration.Unit)
                : null
        };
        state.Validate(tiling);
        return new MaplessCellSessionStart(tiling, state);
    }
}
