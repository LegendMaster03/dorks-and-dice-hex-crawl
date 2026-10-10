using System.Text.Json.Serialization;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

/// <summary>
/// Versioned read-only topology contract for an authenticated cell expedition.
/// Atomic interfaces are not polygon sides or synthetic hex directions.
/// </summary>
public sealed record CellInterfaceContract(
    int InterfaceIndex,
    int BoundarySideIndex,
    WorldCellId NeighborCell,
    int ReciprocalInterfaceIndex,
    WorldPoint Start,
    WorldPoint End)
{
    public static CellInterfaceContract From(WorldCellBoundary boundary) => new(
        boundary.InterfaceIndex,
        boundary.BoundarySideIndex,
        boundary.To,
        boundary.ReciprocalInterfaceIndex,
        boundary.Start,
        boundary.End);
}

public sealed record CellTraversalContextContract(
    int FormatVersion,
    WorldCellId CurrentCell,
    WorldPoint Position,
    WorldPoint Center,
    IReadOnlyList<WorldPoint> Polygon,
    WorldPoint? Heading,
    WorldCellId? EnteredFrom,
    int? EntryInterfaceIndex,
    int? SelectedExitInterfaceIndex,
    bool HasPhysicalCalibration,
    IReadOnlyList<CellInterfaceContract> Interfaces)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DistanceContract? PhysicalDistancePerWorldUnit { get; init; }

    public static CellTraversalContextContract From(
        PeriodicWorldTiling world,
        CellExpeditionState expedition)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(expedition);
        expedition.Validate(world);
        var cursor = expedition.Traversal;
        var geometry = world.Resolve(cursor.CurrentCell.Address);
        var exits = world.Boundaries(cursor.CurrentCell.Address);
        // Verify both endpoints of every advertised edge before projecting
        // it into an API choice. Never derive adjacency from polygon corners.
        foreach (var boundary in exits)
        {
            var reciprocal = world.Reciprocal(boundary);
            if (reciprocal.From != boundary.To
                || reciprocal.To != boundary.From
                || reciprocal.InterfaceIndex != boundary.ReciprocalInterfaceIndex)
                throw new InvalidOperationException("Inconsistent authoritative reciprocal interface.");
        }

        return new CellTraversalContextContract(
            PeriodicCellTraversal.CurrentFormatVersion,
            cursor.CurrentCell,
            cursor.Position,
            geometry.Center,
            geometry.Polygon,
            cursor.TravelHeading,
            cursor.EnteredFrom,
            cursor.EntryInterfaceIndex,
            cursor.SelectedExitInterfaceIndex,
            world.PhysicalDistancePerWorldUnit is not null,
            exits.Select(CellInterfaceContract.From).ToArray())
        {
            PhysicalDistancePerWorldUnit = world.PhysicalDistancePerWorldUnit is { } calibration
                ? DistanceContract.From(calibration) : null
        };
    }
}
