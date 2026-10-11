using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// Shape data supplied alongside a proved atomic topology, not an inference
/// from the D-symbol. The topology keeps every interface and reciprocal index.
/// Requested split fractions are also stored as metric constraints so saved
/// realizations are independently revalidated on reload.
/// </summary>
public static class PeriodicStraightEdgeGeometry
{
    private const string Prefix = "straight-edge-fraction:";

    public static IReadOnlyList<PeriodicStraightEdgeSubdivision> Normalize(
        PeriodicTopologyWitness topology, IReadOnlyList<PeriodicStraightEdgeSubdivision>? requested)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (requested is null || requested.Count == 0) return [];
        var byId = topology.MotifCells.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var seen = new HashSet<(string Cell, int Vertex)>();
        var normalized = new List<PeriodicStraightEdgeSubdivision>(requested.Count);
        foreach (var split in requested)
        {
            if (split is null || string.IsNullOrWhiteSpace(split.MotifCellId)
                || !byId.TryGetValue(split.MotifCellId, out var cell)
                || split.BoundaryVertexIndex < 0 || split.BoundaryVertexIndex >= cell.Boundary.Count)
                throw new ArgumentException("A straight-edge subdivision must identify an existing motif cell and boundary vertex.");
            if (!double.IsFinite(split.Fraction) || split.Fraction <= 0 || split.Fraction >= 1)
                throw new ArgumentException("A straight-edge subdivision fraction must be strictly between zero and one.");
            if (!seen.Add((split.MotifCellId, split.BoundaryVertexIndex)))
                throw new ArgumentException("A boundary vertex may have only one straight-edge subdivision constraint.");
            normalized.Add(split);
        }
        // This also rejects requests that turn the whole polygon into fewer than
        // three geometric sides (including a cyclic chain of all its interfaces).
        Apply(topology, normalized);
        return normalized.OrderBy(s => s.MotifCellId, StringComparer.Ordinal)
            .ThenBy(s => s.BoundaryVertexIndex).ToArray();
    }

    public static PeriodicTopologyWitness Apply(
        PeriodicTopologyWitness topology, IReadOnlyList<PeriodicStraightEdgeSubdivision> splits)
    {
        var revised = topology.MotifCells.Select(cell =>
        {
            int size = cell.Boundary.Count;
            var parent = Enumerable.Range(0, size).ToArray();
            int Root(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            foreach (var split in splits.Where(s => s.MotifCellId == cell.Id))
                parent[Root((split.BoundaryVertexIndex + size - 1) % size)] =
                    Root(split.BoundaryVertexIndex);
            var labels = new Dictionary<int, int>();
            var boundary = cell.Boundary.Select(edge =>
            {
                int root = Root(edge.Index);
                if (!labels.TryGetValue(root, out int side))
                {
                    side = labels.Count;
                    labels.Add(root, side);
                }
                return edge with { BoundarySideIndex = side };
            }).ToArray();
            if (labels.Count < 3)
                throw new ArgumentException("Straight-edge subdivisions would leave fewer than three geometric sides.");
            return cell with { Boundary = boundary };
        }).ToArray();
        var result = topology with { MotifCells = revised };
        result.ValidateAdjacency();
        return result;
    }

    public static TilingMetricConstraint Constraint(PeriodicStraightEdgeSubdivision split) =>
        new(Prefix + split.MotifCellId + ":" +
            split.BoundaryVertexIndex.ToString(CultureInfo.InvariantCulture),
            "ratio", split.Fraction, split.Fraction);

    public static bool TryParseConstraint(string key, out string motifCellId, out int boundaryVertexIndex)
    {
        motifCellId = "";
        boundaryVertexIndex = -1;
        if (!key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int lastColon = key.LastIndexOf(':');
        if (lastColon <= Prefix.Length || lastColon == key.Length - 1
            || !int.TryParse(key[(lastColon + 1)..], NumberStyles.None,
                CultureInfo.InvariantCulture, out boundaryVertexIndex))
            throw new InvalidOperationException("Invalid straight-edge subdivision metric constraint key.");
        motifCellId = key[Prefix.Length..lastColon];
        return true;
    }
}
