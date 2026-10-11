using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Geometry and runtime state for a newly created mapless spatial session.
/// The spatial world is derived entirely from the stable session identity and
/// the recorded hex orientation/physical scale, not from imagery or an overworld.
/// This is NOT a converter for historic V1 states: those states lack an exact
/// geometric position and require a separate 20.2 migration decision.
/// </summary>
public sealed record AbstractCellSessionStart(
    PeriodicWorldTiling World, CellExpeditionState Expedition);

public static class AbstractCellSessionGeometry
{
    /// <summary>
    /// The world has an arbitrary but deterministic unit-radius coordinate
    /// pose, while physical calibration comes from the existing session scale.
    /// Reconstructing the same id/context yields identical cell identities.
    /// </summary>
    public static PeriodicWorldTiling CreateHexWorld(
        Guid sessionId, AbstractHexCrawlSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        if (sessionId == Guid.Empty)
            throw new ArgumentException("Mapless tiling identity must be stable and nonempty.", nameof(sessionId));

        return LegacyHexTilingCompatibility.Create(new HexGridDefinition
        {
            Id = sessionId,
            Orientation = context.Orientation,
            CoordinateConvention = HexCoordinateConvention.AxialQr,
            HexRadiusWorldUnits = 1d,
            Origin = new WorldPoint(0d, 0d),
            RotationDegrees = 0d,
            NeighborCenterDistance = context.HexContext.HexCenterDistance
        });
    }

    /// <summary>
    /// Creation-time initialization only. Do not call this for saved V1
    /// sessions: their center-distance progress has no provable exact pose.
    /// </summary>
    public static AbstractCellSessionStart CreateNewHexSession(
        Guid sessionId, AbstractHexCrawlSessionContext context, HexCoordinate start)
    {
        var world = CreateHexWorld(sessionId, context);
        var address = LegacyHexTilingCompatibility.ToAddress(start);
        var cell = world.Resolve(address);
        var state = new CellExpeditionState
        {
            Id = sessionId,
            Traversal = new PeriodicCellTraversal
            {
                CurrentCell = cell.Id,
                Position = cell.Center
            },
            DistanceTraveled = new DistanceMeasure(0, context.HexContext.HexCenterDistance.Unit)
        };
        state.Validate(world);
        return new AbstractCellSessionStart(world, state);
    }
}
