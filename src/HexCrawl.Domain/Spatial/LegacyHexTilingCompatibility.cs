using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// Phase 17 compatibility bridge. Axial (Q,R) maps bijectively to the single
/// hex motif's (U,V), without changing the existing grid UUID, world pose,
/// distance units, direction ordering or saved HexId representation.
/// The bridge is temporary until Phase 18 accepts boundary-based movement.
/// </summary>
public static class LegacyHexTilingCompatibility
{
    private static readonly ConditionalWeakTable<HexGridDefinition, PeriodicWorldTiling> Cached = new();
    public static PeriodicWorldTiling FromGrid(HexGridDefinition grid) =>
        Cached.GetValue(grid, Create);

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

    /// <summary>Legacy grid is a verified compatibility projection, never a second editable authority.</summary>
    public static bool Matches(PeriodicWorldTiling authoritative, HexGridDefinition grid)
    {
        if (authoritative.Id != grid.Id || authoritative.Revision < 1)
            return false;
        var projection = FromGrid(grid);
        if (authoritative.Topology.QuotientDsSymbol != projection.Topology.QuotientDsSymbol
            || authoritative.Topology.TranslationDsSymbol != projection.Topology.TranslationDsSymbol
            || authoritative.Topology.MotifCells.Count != projection.Topology.MotifCells.Count
            || authoritative.Realization.Units != projection.Realization.Units)
            return false;
        var source = projection.Topology.MotifCells[0];
        var actual = authoritative.Topology.MotifCells[0];
        if (source.Id != actual.Id || source.Boundary.Count != actual.Boundary.Count
            || source.Boundary.Where((edge, index) => edge != actual.Boundary[index]).Any())
            return false;
        // All polygon vertices and both basis translations must retain their
        // original world-space meaning, not merely an equivalent D-symbol.
        foreach (var hex in new[] { new HexCoordinate(0, 0), new HexCoordinate(1, 0), new HexCoordinate(0, 1) })
        {
            var address = ToAddress(hex);
            var left = projection.Resolve(address).Polygon;
            var right = authoritative.Resolve(address).Polygon;
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                double scale = Math.Max(1, Math.Max(Math.Abs(left[i].X), Math.Abs(left[i].Y)));
                if (left[i].DistanceTo(right[i]) > scale * 1e-9)
                    return false;
            }
        }
        return true;
    }

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
            grid.NeighborCenterDistance.Unit.Symbol,
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
