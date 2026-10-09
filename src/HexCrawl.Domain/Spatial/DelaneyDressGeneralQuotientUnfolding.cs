using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

public sealed record DelaneyDressGeneralCoverResult(
    DelaneyDressCoverStatus Status,
    PeriodicTopologyWitness? Witness = null,
    int CyclicSheetCount = 0,
    int OrientationChamberCount = 0,
    IReadOnlyList<int>? SourceProjection = null,
    string? Reason = null);

/// <summary>
/// General finite, cyclic-holonomy orbifold unfolding. Valid Euclidean symbols
/// with variable face degrees and valences are permitted, not cataloged.
/// Removes mirrors by the connected orientation double, then cone stabilizers
/// by a finite cyclic cover whose local monodromies are proved independently.
/// </summary>
public static class DelaneyDressGeneralQuotientUnfolding
{
    private sealed record Vertex(int Id, int Branch);
    private sealed class Arc(int plus, int minus, int chamber, int involution)
    {
        public int Plus { get; } = plus;
        public int Minus { get; } = minus;
        public int Chamber { get; } = chamber;
        public int Involution { get; } = involution;
        public int Voltage { get; set; }
    }
    private sealed record Graph(
        List<Vertex> Vertices, int[][] Which, List<Arc> Arcs);

    private static int Mod(int x, int n) => (x % n + n) % n;
    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Abs(a);
    }

    private static Graph? ConstructOrbifoldGraph(DelaneyDressSymbol symbol)
    {
        int n = symbol.ChamberCount;
        var vertices = new List<Vertex>();
        int[][] which = [new int[n + 1], new int[n + 1], new int[n + 1]];
        int[][] pairs = [[1, 2], [0, 2], [0, 1]];
        for (int type = 0; type < 3; type++)
        {
            int[] labels = type switch
            {
                0 => symbol.M12,
                1 => [],
                _ => symbol.M01
            };
            int[] groups = which[type];
            Array.Fill(groups, -1);
            for (int root = 1; root <= n; root++)
            {
                if (groups[root] >= 0) continue;
                int id = vertices.Count;
                var members = new List<int> { root };
                groups[root] = id;
                for (int i = 0; i < members.Count; i++)
                {
                    foreach (int map in pairs[type])
                    {
                        int next = symbol.Involutions[map][members[i]];
                        if (groups[next] >= 0) continue;
                        groups[next] = id;
                        members.Add(next);
                    }
                }
                int label = type == 1 ? 2 : labels[root];
                int numerator = 2 * label;
                if (numerator % members.Count != 0) return null;
                int branch = numerator / members.Count;
                if (branch < 1) return null;
                vertices.Add(new Vertex(id, branch));
            }
        }
        var color = new int[n + 1];
        color[1] = 1;
        var visited = new List<int> { 1 };
        for (int i = 0; i < visited.Count; i++)
        {
            int c = visited[i];
            foreach (var map in symbol.Involutions)
            {
                int next = map[c];
                if (color[next] == 0) { color[next] = -color[c]; visited.Add(next); }
                else if (color[next] == color[c]) return null;
            }
        }
        if (visited.Count != n) return null;
        int[][] ends = [[2, 1], [0, 2], [1, 0]];
        var arcs = new List<Arc>();
        for (int c = 1; c <= n; c++)
        {
            if (color[c] != 1) continue;
            for (int k = 0; k < 3; k++)
                arcs.Add(new Arc(which[ends[k][0]][c], which[ends[k][1]][c], c, k));
        }
        return arcs.Count == 3 * n / 2 ? new Graph(vertices, which, arcs) : null;
    }

    private static int[]? AssignCyclicCharges(Graph graph, int sheets)
    {
        List<int>?[] states = new List<int>?[sheets];
        states[0] = [];
        foreach (var vertex in graph.Vertices)
        {
            int b = vertex.Branch;
            if (sheets % b != 0) return null;
            var options = new List<int>();
            for (int unit = 1; unit <= b; unit++)
                if (Gcd(unit, b) == 1) options.Add(Mod(sheets / b * unit, sheets));
            List<int>?[] next = new List<int>?[sheets];
            for (int residue = 0; residue < sheets; residue++)
            {
                if (states[residue] is not { } previous) continue;
                foreach (int charge in options)
                {
                    int target = Mod(residue + charge, sheets);
                    if (next[target] is not null) continue;
                    next[target] = [..previous, charge];
                }
            }
            states = next;
        }
        return states[0]?.ToArray();
    }

    private static bool AssignVoltages(Graph graph, IReadOnlyList<int> charges, int sheets)
    {
        int count = graph.Vertices.Count;
        var adjacency = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < graph.Arcs.Count; i++)
        {
            var arc = graph.Arcs[i];
            if (arc.Plus == arc.Minus) return false;
            adjacency[arc.Plus].Add(i);
            adjacency[arc.Minus].Add(i);
        }
        var parent = Enumerable.Repeat(-1, count).ToArray();
        var parentEdge = Enumerable.Repeat(-1, count).ToArray();
        parent[0] = 0;
        var traversal = new List<int> { 0 };
        for (int i = 0; i < traversal.Count; i++)
        {
            int here = traversal[i];
            foreach (int edgeId in adjacency[here])
            {
                var edge = graph.Arcs[edgeId];
                int next = edge.Plus == here ? edge.Minus : edge.Plus;
                if (parent[next] >= 0) continue;
                parent[next] = here;
                parentEdge[next] = edgeId;
                traversal.Add(next);
            }
        }
        if (traversal.Count != count) return false;
        int[] subtree = charges.Select(value => Mod(value, sheets)).ToArray();
        for (int i = traversal.Count - 1; i > 0; i--)
        {
            int node = traversal[i];
            var edge = graph.Arcs[parentEdge[node]];
            edge.Voltage = Mod((edge.Plus == node ? 1 : -1) * subtree[node], sheets);
            subtree[parent[node]] = Mod(subtree[parent[node]] + subtree[node], sheets);
        }
        if (subtree[0] != 0) return false;
        var flux = new int[count];
        foreach (var edge in graph.Arcs)
        {
            flux[edge.Plus] = Mod(flux[edge.Plus] + edge.Voltage, sheets);
            flux[edge.Minus] = Mod(flux[edge.Minus] - edge.Voltage, sheets);
        }
        return Enumerable.Range(0, count).All(i => flux[i] == Mod(charges[i], sheets));
    }

    public static DelaneyDressGeneralCoverResult Construct(string source, int chamberLimit = 1024)
    {
        static DelaneyDressGeneralCoverResult Unsupported(string reason) =>
            new(DelaneyDressCoverStatus.Unsupported, Reason: reason);
        static DelaneyDressGeneralCoverResult Inconclusive(string reason) =>
            new(DelaneyDressCoverStatus.Inconclusive, Reason: reason);
        if (chamberLimit is < 8 or > 2048)
            return Unsupported("Invalid bounded translation-cover chamber limit.");
        var inspected = DelaneyDressTopology.Inspect(source, chamberLimit);
        if (inspected.Status == DelaneyDressStatus.LimitExceeded)
            return Unsupported("Input Euclidean symbol exceeds the bounded chamber limit.");
        if (inspected.Status != DelaneyDressStatus.Euclidean)
            return new(DelaneyDressCoverStatus.Invalid, Reason: "A valid Euclidean D-symbol is required.");
        var original = inspected.Symbol!;
        var orient = DelaneyDressOrientationCover.Construct(original.Canonical, chamberLimit);
        if (orient.Status != DelaneyDressCoverStatus.Constructed)
            return orient.Status == DelaneyDressCoverStatus.Unsupported
                ? Unsupported(orient.Reason ?? "Orientation double exceeds capacity.")
                : Inconclusive(orient.Reason ?? "Orientation double is unresolved.");
        var orientInspection = DelaneyDressTopology.Inspect(orient.CoverSymbol!, chamberLimit);
        if (orientInspection.Status != DelaneyDressStatus.Euclidean)
            return Inconclusive("Connected orientation cover failed validation.");
        var oriented = orientInspection.Symbol!;
        var graph = ConstructOrbifoldGraph(oriented);
        if (graph is null)
            return Inconclusive("Unable to construct the oriented orbifold vertex graph.");
        int sheets = 1;
        foreach (var vertex in graph.Vertices)
        {
            sheets = sheets / Gcd(sheets, vertex.Branch) * vertex.Branch;
            if (sheets > 24)
                return Unsupported("Orbifold rotational sheet order exceeds 24.");
        }
        if ((long)sheets * oriented.ChamberCount > chamberLimit)
            return Unsupported("The torsion-free connected cover exceeds the bounded chamber limit.");
        var charges = AssignCyclicCharges(graph, sheets);
        if (charges is null)
            return Inconclusive("No cyclic holonomy assignment resolves every local stabilizer.");
        if (!AssignVoltages(graph, charges, sheets))
            return Inconclusive("The orbifold holonomy did not admit a consistent voltage assignment.");
        int n = oriented.ChamberCount;
        int[][] voltage = [new int[n + 1], new int[n + 1], new int[n + 1]];
        foreach (var arc in graph.Arcs)
        {
            int opposite = oriented.Involutions[arc.Involution][arc.Chamber];
            voltage[arc.Involution][arc.Chamber] = arc.Voltage;
            voltage[arc.Involution][opposite] = Mod(-arc.Voltage, sheets);
        }

        var pairs = new List<(int Chamber, int Sheet)> { (1, 0) };
        var which = new Dictionary<(int Chamber, int Sheet), int> { [(1, 0)] = 1 };
        int[][] maps = [new int[chamberLimit + 1], new int[chamberLimit + 1], new int[chamberLimit + 1]];
        for (int i = 0; i < pairs.Count; i++)
        {
            var (chamber, sheet) = pairs[i];
            for (int k = 0; k < 3; k++)
            {
                var nextPair = (
                    oriented.Involutions[k][chamber],
                    Mod(sheet + voltage[k][chamber], sheets));
                if (!which.TryGetValue(nextPair, out int target))
                {
                    if (pairs.Count >= chamberLimit)
                        return Unsupported("The connected cyclic cover exceeds the bounded chamber limit.");
                    target = pairs.Count + 1;
                    pairs.Add(nextPair);
                    which.Add(nextPair, target);
                }
                maps[k][i + 1] = target;
            }
        }
        var m01 = new int[pairs.Count + 1];
        var m12 = new int[pairs.Count + 1];
        for (int i = 0; i < pairs.Count; i++)
        {
            m01[i + 1] = oriented.M01[pairs[i].Chamber];
            m12[i + 1] = oriented.M12[pairs[i].Chamber];
        }
        var lifted = DelaneyDressTopology.Inspect(Serialize(pairs.Count, maps, m01, m12), chamberLimit);
        if (lifted.Status != DelaneyDressStatus.Euclidean || lifted.Symbol is null
            || !lifted.Symbol.FixedPointFree || !lifted.Symbol.WeaklyOrientable)
            return Inconclusive("The cyclic lift is not an orientable Euclidean chamber cover.");
        var after = ConstructOrbifoldGraph(lifted.Symbol);
        if (after is null || after.Vertices.Any(vertex => vertex.Branch != 1))
            return Inconclusive("Residual rotational or edge branching remains after cyclic unfolding.");
        var cover = DelaneyDressTorusCover.Construct(lifted.Symbol.Canonical, chamberLimit);
        if (cover.Status != DelaneyDressCoverStatus.Constructed || cover.Witness is null)
            return Inconclusive($"Unbranched lift failed primitive torus construction: {cover.Reason}");
        var projection = DelaneyDressTopology.ProjectChambers(lifted.Symbol, original);
        if (projection is null || DelaneyDressTopology.ProjectChambers(lifted.Symbol, oriented) is null)
            return Inconclusive("The final torus does not project onto both chamber quotients.");
        var witness = cover.Witness with
        {
            QuotientDsSymbol = original.Canonical,
            Provenance = "derived:connected-orientation-and-cyclic-holonomy-cover"
        };
        try
        {
            witness.ValidateAdjacency();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or OverflowException)
        {
            return Inconclusive($"The derived primitive topology witness is invalid: {error.Message}");
        }
        return new(
            DelaneyDressCoverStatus.Constructed,
            witness,
            sheets,
            oriented.ChamberCount,
            projection);
    }

    private static string Serialize(int size, int[][] maps, int[] m01, int[] m12)
    {
        var involutions = string.Join(",", maps.Select(map => string.Join(" ",
            Enumerable.Range(1, size).Where(c => c <= map[c]).Select(c => map[c]))));
        string Labels(int first, int[] values)
        {
            var seen = new bool[size + 1];
            var labels = new List<int>();
            for (int root = 1; root <= size; root++)
            {
                if (seen[root]) continue;
                labels.Add(values[root]);
                var queue = new List<int> { root };
                seen[root] = true;
                for (int i = 0; i < queue.Count; i++)
                    foreach (int k in new[] { first, first + 1 })
                    {
                        int next = maps[k][queue[i]];
                        if (seen[next]) continue;
                        seen[next] = true;
                        queue.Add(next);
                    }
            }
            return string.Join(" ", labels);
        }
        return $"<{size}:{involutions}:{Labels(0, m01)},{Labels(1, m12)}>";
    }
}
