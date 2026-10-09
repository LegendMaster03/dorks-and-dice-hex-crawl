using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

public enum DelaneyDressCoverStatus { Constructed, Unsupported, Inconclusive, Invalid }

/// <summary>A mathematical, image-independent translation cover construction result.</summary>
public sealed record DelaneyDressCoverResult(
    DelaneyDressCoverStatus Status,
    PeriodicTopologyWitness? Witness,
    string? Reason = null);

/// <summary>
/// Constructs an integer-addressed Z² motif from a fully expanded, orientable,
/// unbranched torus D-symbol without geometric polygons, source images, or a
/// catalog of tiling types. Symmetry quotients with local stabilizers first
/// require a separate, proven unbranched-cover construction and are explicitly
/// reported as unsupported rather than rejected as invalid mathematics.
/// </summary>
public static class DelaneyDressTorusCover
{
    private sealed record Partition(List<int[]> Orbits, int[] Which);
    private sealed class GraphEdge(
        int id, int faceA, int faceB, int vertexA, int vertexB, HashSet<int> sourceFlags)
    {
        public int Id { get; } = id;
        public int FaceA { get; } = faceA;
        public int FaceB { get; } = faceB;
        public int VertexA { get; } = vertexA;
        public int VertexB { get; } = vertexB;
        public HashSet<int> SourceFlags { get; } = sourceFlags;
        public LatticeDisplacement? Voltage { get; set; }
    }
    private sealed class Forest(int size)
    {
        private readonly int[] parent = Enumerable.Range(0, size).ToArray();
        private int Root(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }
        public bool Join(int first, int second)
        {
            int a = Root(first), b = Root(second);
            if (a == b) return false;
            parent[b] = a;
            return true;
        }
    }
    private readonly record struct PendingBoundary(int EdgeId, int TargetFace, LatticeDisplacement Shift);

    public static DelaneyDressCoverResult Construct(string source, int chamberLimit = 1024)
    {
        static DelaneyDressCoverResult Unsupported(string reason) => new(DelaneyDressCoverStatus.Unsupported, null, reason);
        static DelaneyDressCoverResult Inconclusive(string reason) => new(DelaneyDressCoverStatus.Inconclusive, null, reason);
        if (chamberLimit < 1 || chamberLimit > 2048)
            return Unsupported("Invalid bounded chamber limit.");
        var inspected = DelaneyDressTopology.Inspect(source, chamberLimit);
        if (inspected.Status == DelaneyDressStatus.LimitExceeded)
            return Unsupported("D-symbol exceeds bounded chamber capacity.");
        if (inspected.Status != DelaneyDressStatus.Euclidean)
            return new(DelaneyDressCoverStatus.Invalid, null, "A valid Euclidean D-symbol is required.");

        // Normalize first: cell IDs and translation offsets must be independent
        // of the caller's arbitrary numbering of equivalent chambers.
        var canonical = DelaneyDressTopology.Inspect(inspected.Symbol!.Canonical, chamberLimit);
        if (canonical.Status != DelaneyDressStatus.Euclidean)
            return Inconclusive("Canonical chamber normalization was inconsistent.");
        var symbol = canonical.Symbol!;
        if (!symbol.FixedPointFree || !symbol.WeaklyOrientable)
            return Unsupported("A fully expanded orientable unbranched torus symbol is required; a symmetry quotient needs unfolding.");
        int n = symbol.ChamberCount;
        int[] s0 = symbol.Involutions[0], s1 = symbol.Involutions[1], s2 = symbol.Involutions[2];
        Partition faces = SplitOrbits(symbol.Involutions, 0, 1, n);
        Partition edges = SplitOrbits(symbol.Involutions, 0, 2, n);
        Partition vertices = SplitOrbits(symbol.Involutions, 1, 2, n);
        int faceCount = faces.Orbits.Count, edgeCount = edges.Orbits.Count, vertexCount = vertices.Orbits.Count;
        if (faceCount < 1 || edgeCount < 1 || vertexCount < 1 || vertexCount - edgeCount + faceCount != 0)
            return Unsupported("The chamber quotient is not an orientable combinatorial torus.");
        if (edgeCount > 1024 || faceCount > 256 || vertexCount > 512)
            return Unsupported("Torus quotient exceeds bounded construction limits.");
        foreach (var orbit in faces.Orbits)
        {
            int multiplicity = symbol.M01[orbit[0]];
            if (orbit.Length != 2 * multiplicity || orbit.Any(c => symbol.M01[c] != multiplicity))
                return Unsupported("A face is locally branched under the selected symmetry group.");
        }
        foreach (var orbit in vertices.Orbits)
        {
            int multiplicity = symbol.M12[orbit[0]];
            if (orbit.Length != 2 * multiplicity || orbit.Any(c => symbol.M12[c] != multiplicity))
                return Unsupported("A vertex is locally branched under the selected symmetry group.");
        }
        if (edges.Orbits.Any(orbit => orbit.Length != 4))
            return Unsupported("An edge has a local stabilizer and must first be unfolded.");
        var graph = new List<GraphEdge>();
        for (int e = 0; e < edgeCount; e++)
        {
            int root = edges.Orbits[e][0];
            var sourceFlags = new HashSet<int> { root, s0[root] };
            var opposite = new HashSet<int> { s2[root], s2[s0[root]] };
            if (sourceFlags.Count != 2 || opposite.Count != 2 || sourceFlags.Overlaps(opposite))
                return Unsupported("Invalid chamber edge-side orientation.");
            int faceA = faces.Which[root], faceB = faces.Which[s2[root]];
            if (sourceFlags.Any(c => faces.Which[c] != faceA)
                || opposite.Any(c => faces.Which[c] != faceB))
                return Unsupported("Edge flags have inconsistent face incidence.");
            graph.Add(new GraphEdge(e, faceA, faceB, vertices.Which[root], vertices.Which[s0[root]], sourceFlags));
        }

        // Equations for closure of the dual 2-cell surrounding each vertex.
        var relations = new List<Dictionary<int, int>>();
        foreach (var orbit in vertices.Orbits)
        {
            var coefficients = new Dictionary<int, int>();
            int start = orbit[0], chamber = start, steps = 0;
            do
            {
                int edge = edges.Which[chamber];
                int sign = graph[edge].SourceFlags.Contains(chamber) ? 1 : -1;
                coefficients[edge] = coefficients.GetValueOrDefault(edge) + sign;
                chamber = s1[s2[chamber]];
                if (++steps > n) return Unsupported("Vertex rotation did not close.");
            } while (chamber != start);
            if (steps * 2 != orbit.Length) return Unsupported("Vertex rotation is inconsistent with its chamber orbit.");
            relations.Add(coefficients);
        }

        // Tree/cotree decomposition: first choose a primal vertex tree,
        // then a disjoint dual face tree. Euler characteristic zero leaves
        // exactly two homological generator edges.
        var primal = new Forest(vertexCount);
        var primalTree = new HashSet<int>();
        foreach (var edge in graph)
            if (primal.Join(edge.VertexA, edge.VertexB)) primalTree.Add(edge.Id);
        if (primalTree.Count != vertexCount - 1) return Inconclusive("Disconnected primal vertex graph.");
        var dual = new Forest(faceCount);
        var dualTree = new HashSet<int>();
        foreach (var edge in graph)
        {
            if (primalTree.Contains(edge.Id)) continue;
            if (dual.Join(edge.FaceA, edge.FaceB)) dualTree.Add(edge.Id);
        }
        if (dualTree.Count != faceCount - 1) return Inconclusive("Disconnected complementary dual graph.");
        var generators = graph.Where(edge => !primalTree.Contains(edge.Id) && !dualTree.Contains(edge.Id)).ToArray();
        if (generators.Length != 2) return Inconclusive("A torus must have exactly two free translation generators.");
        generators[0].Voltage = new LatticeDisplacement(1, 0);
        generators[1].Voltage = new LatticeDisplacement(0, 1);
        foreach (int id in dualTree) graph[id].Voltage = new LatticeDisplacement(0, 0);

        var adjacency = Enumerable.Range(0, vertexCount)
            .Select(_ => new List<(int Vertex, int Edge)>()).ToArray();
        foreach (int id in primalTree)
        {
            var edge = graph[id];
            adjacency[edge.VertexA].Add((edge.VertexB, id));
            adjacency[edge.VertexB].Add((edge.VertexA, id));
        }
        var parents = Enumerable.Repeat(-1, vertexCount).ToArray();
        var parentEdges = Enumerable.Repeat(-1, vertexCount).ToArray();
        var order = new List<int> { 0 };
        parents[0] = 0;
        for (int cursor = 0; cursor < order.Count; cursor++)
        {
            foreach (var (next, edge) in adjacency[order[cursor]])
            {
                if (parents[next] != -1) continue;
                parents[next] = order[cursor];
                parentEdges[next] = edge;
                order.Add(next);
            }
        }
        if (order.Count != vertexCount) return Inconclusive("Primal spanning tree is disconnected.");
        for (int cursor = order.Count - 1; cursor >= 1; cursor--)
        {
            int vertex = order[cursor], edgeId = parentEdges[vertex];
            int coefficient = relations[vertex].GetValueOrDefault(edgeId);
            if (Math.Abs(coefficient) != 1)
                return Unsupported("Cannot orient a primal-tree edge in its dual boundary.");
            long x = 0, y = 0;
            foreach (var (id, multiplicity) in relations[vertex])
            {
                if (id == edgeId) continue;
                if (graph[id].Voltage is not { } shift)
                    return Inconclusive("Dual-face elimination encountered an unassigned voltage.");
                x = checked(x + multiplicity * shift.U);
                y = checked(y + multiplicity * shift.V);
            }
            graph[edgeId].Voltage = new LatticeDisplacement(-x / coefficient, -y / coefficient);
        }
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            long x = 0, y = 0;
            foreach (var (edgeId, sign) in relations[vertex])
            {
                if (graph[edgeId].Voltage is not { } voltage)
                    return Inconclusive("A periodic interface voltage remains unresolved.");
                x = checked(x + sign * voltage.U);
                y = checked(y + sign * voltage.V);
            }
            if (x != 0 || y != 0)
                return Inconclusive("Torus voltages do not close around a vertex.");
        }

        // The same combinatorial face may meet itself on two opposing sides of
        // a fundamental domain. Preserve both sides, not just distinct edge IDs.
        var pending = new List<PendingBoundary>[faceCount];
        for (int face = 0; face < faceCount; face++)
        {
            var orbit = faces.Orbits[face];
            int sides = orbit.Length / 2;
            int chamber = orbit[0];
            var boundaries = new List<PendingBoundary>();
            for (int side = 0; side < sides; side++)
            {
                if (faces.Which[chamber] != face)
                    return Inconclusive("A face boundary walk left its original orbit.");
                int edgeId = edges.Which[chamber];
                var edge = graph[edgeId];
                bool positive = edge.SourceFlags.Contains(chamber);
                var voltage = edge.Voltage!.Value;
                boundaries.Add(new PendingBoundary(edgeId, positive ? edge.FaceB : edge.FaceA,
                    positive ? voltage : voltage.Opposite()));
                chamber = s1[s0[chamber]];
            }
            if (chamber != orbit[0]) return Unsupported("An ordered face boundary is ambiguous.");
            pending[face] = boundaries;
        }
        var motif = new List<PeriodicMotifCell>();
        for (int face = 0; face < faceCount; face++)
        {
            var boundaries = new List<PeriodicEdgeInterface>();
            for (int side = 0; side < pending[face].Count; side++)
            {
                var edge = pending[face][side];
                var reversed = pending[edge.TargetFace]
                    .Select((candidate, index) => new { Candidate = candidate, Index = index })
                    .Where(item => item.Candidate.EdgeId == edge.EdgeId && item.Candidate.TargetFace == face
                        && item.Candidate.Shift == edge.Shift.Opposite()).ToArray();
                if (reversed.Length != 1)
                    return Inconclusive("Periodic boundary has no unique reciprocal interface.");
                boundaries.Add(new PeriodicEdgeInterface(side, side, $"face-{edge.TargetFace}", edge.Shift,
                    reversed[0].Index));
            }
            motif.Add(new PeriodicMotifCell($"face-{face}", boundaries));
        }
        var witness = new PeriodicTopologyWitness(PeriodicTopologyContractVersion.Current,
            symbol.Canonical, symbol.Canonical, motif, "derived:unbranched-orientable-torus");
        witness.ValidateAdjacency();
        return new(DelaneyDressCoverStatus.Constructed, witness);
    }

    private static Partition SplitOrbits(int[][] maps, int first, int second, int n)
    {
        var which = Enumerable.Repeat(-1, n + 1).ToArray();
        var groups = new List<int[]>();
        for (int start = 1; start <= n; start++)
        {
            if (which[start] != -1) continue;
            int id = groups.Count;
            var values = new List<int> { start };
            which[start] = id;
            for (int cursor = 0; cursor < values.Count; cursor++)
            {
                foreach (int next in new[] { maps[first][values[cursor]], maps[second][values[cursor]] })
                {
                    if (which[next] != -1) continue;
                    which[next] = id;
                    values.Add(next);
                }
            }
            values.Sort();
            groups.Add(values.ToArray());
        }
        return new Partition(groups, which);
    }
}
