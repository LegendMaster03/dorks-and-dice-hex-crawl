using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// Phase 17 compatibility bridge. Axial (Q,R) maps bijectively to the single
/// hex motif's (U,V), without changing the existing grid UUID, world pose,
/// distance units, direction ordering or saved HexId representation.
/// The bridge is temporary until Phase 18 accepts boundary-based movement.
/// </summary>
public static class LegacyHexTilingCompatibility
{
    public const string MotifId = "hex";
    public const string HexQuotient = "<1:1,1,1:6,3>";

    private static readonly LatticeDisplacement[] SideOffsets =
    [
        new(1, 0), new(0, 1), new(-1, 1),
        new(-1, 0), new(0, -1), new(1, -1)
    ];

    public static PeriodicCellAddress ToAddress(HexCoordinate hex) =>
        new(MotifId, new LatticeDisplacement(hex.Q, hex.R));

    public static HexCoordinate ToHex(PeriodicCellAddress address)
    {
        if (!string.Equals(address.MotifCellId, MotifId, StringComparison.Ordinal)
            || address.Translation.U < int.MinValue || address.Translation.U > int.MaxValue
            || address.Translation.V < int.MinValue || address.Translation.V > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(address), "Not a legacy axial hex cell address.");
        return new HexCoordinate((int)address.Translation.U, (int)address.Translation.V);
    }

    public static WorldCellId ToCellId(HexId hex) =>
        new(hex.GridId, ToAddress(hex.Coordinate));

    public static HexId ToHexId(WorldCellId cell) =>
        new(cell.TilingId, ToHex(cell.Address));

    public static PeriodicWorldTiling Create(HexGridDefinition grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.Validate();
        if (!double.IsFinite(grid.RotationDegrees)
            || !double.IsFinite(grid.Origin.X) || !double.IsFinite(grid.Origin.Y))
            throw new InvalidOperationException("Legacy hex pose is not finite.");

        // The chamber incidence below is reconstructed from one six-sided
        // oriented face and its opposite boundary pairings. It is not a
        // separate hex geometry validator: Phase 16 validates both witnesses.
        const string translationFaceIncidence =
            "<12:2 4 6 8 10 12,12 3 5 7 9 11,8 7 10 9 12 11:6,3 3>";
        var inspected = DelaneyDressTopology.Inspect(translationFaceIncidence);
        if (inspected.Status != DelaneyDressStatus.Euclidean)
            throw new InvalidOperationException("Legacy hex translation cover is invalid.");

        var interfaces = Enumerable.Range(0, 6).Select(side => new PeriodicEdgeInterface(
            side, side, MotifId, SideOffsets[side], (side + 3) % 6)).ToArray();
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current,
            HexQuotient, inspected.Symbol!.Canonical,
            [new PeriodicMotifCell(MotifId, interfaces)],
            "legacy-axial-qr:phase-17");
        var zero = HexGeometry.HexToWorld(grid, new HexCoordinate(0, 0));
        var translationU = HexGeometry.HexToWorld(grid, new HexCoordinate(1, 0)) - zero;
        var translationV = HexGeometry.HexToWorld(grid, new HexCoordinate(0, 1)) - zero;
        var corners = HexGeometry.Corners(grid, new HexCoordinate(0, 0))
            .Select(p => new TilingWorldPoint(p.X - zero.X, p.Y - zero.Y)).ToArray();
        var metric = new PeriodicMetricRealization(
            grid.NeighborCenterDistance.Unit.ToString(),
            new TilingWorldPoint(translationU.X, translationU.Y),
            new TilingWorldPoint(translationV.X, translationV.Y),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>(StringComparer.Ordinal)
            { [MotifId] = corners },
            []);
        var world = new PeriodicWorldTiling(grid.Id, topology, metric, grid.Origin);
        world.Validate();
        return world;
    }
}
