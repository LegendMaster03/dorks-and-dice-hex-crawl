using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// Geometric symmetry of a separately verified exact polygon witness.
/// This is not evidence that an uploaded raster was segmented correctly.
/// </summary>
public sealed record MetricChamberSymmetryResult(
    string Status, string Reason,
    string? TranslationDsSymbol = null, string? MetricQuotientDsSymbol = null,
    int? MetricAutomorphisms = null, int? CombinatorialAutomorphisms = null,
    int? SourceChambers = null, int? QuotientChambers = null,
    double? MaximumResidual = null, string? Evidence = null);

public static class DelaneyDressMetricChamberSymmetry
{
    private readonly record struct Flag(
        TilingWorldPoint Vertex, TilingWorldPoint Midpoint, TilingWorldPoint Center,
        int Cell, int Side, LatticeDisplacement Shift);

    private static TilingWorldPoint Add(TilingWorldPoint a, TilingWorldPoint b) =>
        new(a.X + b.X, a.Y + b.Y);
    private static TilingWorldPoint Sub(TilingWorldPoint a, TilingWorldPoint b) =>
        new(a.X - b.X, a.Y - b.Y);
    private static TilingWorldPoint Mul(TilingWorldPoint a, double factor) =>
        new(a.X * factor, a.Y * factor);
    private static double Cross(TilingWorldPoint a, TilingWorldPoint b) =>
        a.X * b.Y - a.Y * b.X;
    private static double Dot(TilingWorldPoint a, TilingWorldPoint b) =>
        a.X * b.X + a.Y * b.Y;
    private static double Norm(TilingWorldPoint a) => Math.Sqrt(Dot(a, a));
    private static TilingWorldPoint Normal(TilingWorldPoint a) => new(-a.Y, a.X);

    /// <summary>
    /// Enumerate all candidate chamber automorphisms; admit only those whose
    /// rigid Euclidean lift maps every barycentric flag AND both primitive
    /// translation generators by a unimodular integer basis change. The exact
    /// reciprocal interface voltages are checked for every s2 crossing.
    /// </summary>
    public static MetricChamberSymmetryResult Verify(
        PeriodicTopologyWitness topology, PeriodicMetricRealization metric,
        double tolerance = 1e-7)
    {
        static MetricChamberSymmetryResult Unsupported(string reason) => new("unsupported", reason);
        static MetricChamberSymmetryResult Inconclusive(string reason) => new("inconclusive", reason);
        if (!double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 0.01)
            return Unsupported("Metric symmetry tolerance must be strict and bounded.");
        if (topology is null || metric is null)
            return Unsupported("Independently verified topology and metric geometry are required.");
        try
        {
            PeriodicMetricWitnessValidator.Validate(topology, metric);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException
            or OverflowException or KeyNotFoundException)
        {
            return Unsupported("The metric witness is not independently validated: " + error.Message);
        }

        var cells = topology.MotifCells;
        if (cells.Count == 0 || cells.Count > 24 || cells.Any(c => c.Boundary.Count > 32))
            return Unsupported("Metric symmetry motif complexity limit exceeded.");
        var a = metric.TranslationU;
        var b = metric.TranslationV;
        double determinant = Cross(a, b);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-9)
            return Unsupported("Translation periods are numerically degenerate.");
        TilingWorldPoint Lattice(long u, long v) => Add(Mul(a, u), Mul(b, v));
        (double U, double V) LatticeCoordinates(TilingWorldPoint p) =>
            (Cross(p, b) / determinant, Cross(a, p) / determinant);

        var starts = new int[cells.Count][];
        var flags = new List<Flag> { default };
        var byId = cells.Select((c, i) => (c.Id, i))
            .ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        for (int i = 0; i < cells.Count; i++)
        {
            var polygon = metric.Polygons[cells[i].Id];
            int sides = polygon.Count;
            var center = Mul(polygon.Aggregate(default(TilingWorldPoint), Add), 1d / sides);
            starts[i] = new int[sides];
            for (int side = 0; side < sides; side++)
            {
                var from = polygon[side];
                var to = polygon[(side + 1) % sides];
                var midpoint = Mul(Add(from, to), .5);
                var shift = cells[i].Boundary[side].TargetTranslation;
                starts[i][side] = flags.Count;
                flags.Add(new(from, midpoint, center, i, side, shift));
                flags.Add(new(to, midpoint, center, i, side, shift));
            }
        }
        int n = flags.Count - 1;
        if (n < 6 || n > 1024)
            return Unsupported("Bounded metric symmetry chamber limit exceeded.");

        int[][] maps = [new int[n + 1], new int[n + 1], new int[n + 1]];
        var m01 = new int[n + 1];
        var m12 = new int[n + 1];
        for (int cell = 0; cell < cells.Count; cell++)
        {
            int sides = cells[cell].Boundary.Count;
            for (int side = 0; side < sides; side++)
            {
                var edge = cells[cell].Boundary[side];
                int peerCell = byId[edge.TargetMotifCellId];
                int first = starts[cell][side], previous = starts[cell][(side + sides - 1) % sides];
                int next = starts[cell][(side + 1) % sides];
                int peer = starts[peerCell][edge.ReciprocalInterfaceIndex];
                maps[0][first] = first + 1;
                maps[0][first + 1] = first;
                maps[1][first] = previous + 1;
                maps[1][first + 1] = next;
                maps[2][first] = peer + 1;
                maps[2][first + 1] = peer;
                m01[first] = m01[first + 1] = sides;
            }
        }
        var visited = new bool[n + 1];
        for (int root = 1; root <= n; root++)
        {
            if (visited[root]) continue;
            visited[root] = true;
            var q = new List<int> { root };
            for (int i = 0; i < q.Count; i++)
                for (int k = 1; k <= 2; k++)
                {
                    int next = maps[k][q[i]];
                    if (next == 0 || next > n)
                        return Inconclusive("Metric witness has invalid chamber incidence.");
                    if (visited[next]) continue;
                    visited[next] = true;
                    q.Add(next);
                }
            if (q.Count % 2 != 0)
                return Inconclusive("Invalid metric vertex chamber orbit.");
            foreach (int flag in q) m12[flag] = q.Count / 2;
        }

        string Compressed(int[] map, int size) => string.Join(" ",
            Enumerable.Range(1, size).Where(i => i <= map[i]).Select(i => map[i]));
        string OrbitLabels(int[][] relations, int first, int[] labels, int size)
        {
            var seen = new bool[size + 1];
            var values = new List<int>();
            for (int i = 1; i <= size; i++)
            {
                if (seen[i]) continue;
                values.Add(labels[i]);
                var q = new List<int> { i };
                seen[i] = true;
                for (int index = 0; index < q.Count; index++)
                    for (int k = first; k <= first + 1; k++)
                    {
                        int next = relations[k][q[index]];
                        if (seen[next]) continue;
                        seen[next] = true; q.Add(next);
                    }
            }
            return string.Join(" ", values);
        }
        string BuildSymbol(int[][] relations, int[] faces, int[] vertices, int size) =>
            $"<{size}:{string.Join(",", relations.Select(r => Compressed(r, size)))}:"
            + $"{OrbitLabels(relations, 0, faces, size)},{OrbitLabels(relations, 1, vertices, size)}>";
        var derived = DelaneyDressTopology.Inspect(BuildSymbol(maps, m01, m12, n), 2048);
        if (derived.Status != DelaneyDressStatus.Euclidean
            || derived.Symbol!.Canonical != topology.TranslationDsSymbol)
            return Inconclusive("Metric flags disagree with the exact translation chamber witness.");
        var combinatorial = DelaneyDressChamberSymmetryReduction.Construct(topology.TranslationDsSymbol);
        if (combinatorial.Status != "reduced")
            return Inconclusive("Could not independently establish the combinatorial reference quotient.");

        int[] parent = Enumerable.Range(0, n + 1).ToArray();
        int Find(int p)
        {
            while (parent[p] != p)
            {
                parent[p] = parent[parent[p]];
                p = parent[p];
            }
            return p;
        }
        void Union(int x, int y)
        {
            x = Find(x); y = Find(y);
            if (x != y) parent[Math.Max(x, y)] = Math.Min(x, y);
        }

        var reference = flags[1];
        var d = Sub(reference.Midpoint, reference.Vertex);
        double edgeLength = Norm(d);
        if (edgeLength < 1e-9)
            return Unsupported("The barycentric reference flag has a zero-length side.");
        var u = Mul(d, 1 / edgeLength);
        var v = Normal(u);
        int metricAutomorphisms = 0;
        double maximumResidual = 0;
        for (int root = 1; root <= n; root++)
        {
            if (m01[1] != m01[root] || m12[1] != m12[root]) continue;
            var permutation = new int[n + 1];
            permutation[1] = root;
            var q = new List<int> { 1 };
            bool valid = true;
            for (int i = 0; i < q.Count && valid; i++)
            {
                int from = q[i], to = permutation[from];
                if (m01[from] != m01[to] || m12[from] != m12[to])
                {
                    valid = false; break;
                }
                foreach (var relation in maps)
                {
                    int next = relation[from], image = relation[to];
                    if (permutation[next] == 0)
                    {
                        permutation[next] = image; q.Add(next);
                    }
                    else if (permutation[next] != image)
                    {
                        valid = false; break;
                    }
                }
            }
            if (!valid || q.Count != n || permutation.Skip(1).Distinct().Count() != n)
                continue;
            var desired = flags[root];
            var e = Sub(desired.Midpoint, desired.Vertex);
            if (Math.Abs(Norm(e) - edgeLength) > tolerance) continue;
            var U = Mul(e, 1 / edgeLength);
            var V = Normal(U);
            foreach (int handedness in new[] { 1, -1 })
            {
                TilingWorldPoint Linear(TilingWorldPoint p) =>
                    Add(Mul(U, Dot(p, u)), Mul(V, handedness * Dot(p, v)));
                TilingWorldPoint Transform(TilingWorldPoint p) =>
                    Add(desired.Vertex, Linear(Sub(p, reference.Vertex)));
                var aa = LatticeCoordinates(Linear(a));
                var bb = LatticeCoordinates(Linear(b));
                double[] raw = [aa.U, aa.V, bb.U, bb.V];
                if (raw.Any(x => !double.IsFinite(x) || Math.Abs(x) > 1_000_000)) continue;
                long[] matrix = raw.Select(x => (long)Math.Round(x)).ToArray();
                if (raw.Where((x, i) => Math.Abs(x - matrix[i]) > 1e-6).Any()
                    || Math.Abs(matrix[0] * matrix[3] - matrix[1] * matrix[2]) != 1)
                    continue;

                var offsets = new LatticeDisplacement[n + 1];
                double worst = 0;
                bool works = true;
                for (int f = 1; f <= n; f++)
                {
                    var original = flags[f]; var target = flags[permutation[f]];
                    var fractional = LatticeCoordinates(Sub(Transform(original.Vertex), target.Vertex));
                    if (!double.IsFinite(fractional.U) || !double.IsFinite(fractional.V)
                        || Math.Abs(fractional.U) > 1_000_000 || Math.Abs(fractional.V) > 1_000_000)
                    {
                        works = false; break;
                    }
                    var shift = new LatticeDisplacement((long)Math.Round(fractional.U),
                        (long)Math.Round(fractional.V));
                    offsets[f] = shift;
                    var displacement = Lattice(shift.U, shift.V);
                    TilingWorldPoint[] from = [original.Vertex, original.Midpoint, original.Center];
                    TilingWorldPoint[] to = [target.Vertex, target.Midpoint, target.Center];
                    for (int h = 0; h < 3; h++)
                    {
                        double error = Norm(Sub(Transform(from[h]), Add(to[h], displacement)));
                        if (!double.IsFinite(error) || error > tolerance)
                        {
                            works = false; break;
                        }
                        worst = Math.Max(worst, error);
                    }
                    if (!works) break;
                }
                if (!works) continue;

                for (int f = 1; f <= n && works; f++)
                    for (int k = 0; k < 3; k++)
                    {
                        int next = maps[k][f];
                        var source = k == 2 ? flags[f].Shift : default;
                        var target = k == 2 ? flags[permutation[f]].Shift : default;
                        long appliedU = matrix[0] * source.U + matrix[2] * source.V;
                        long appliedV = matrix[1] * source.U + matrix[3] * source.V;
                        if (offsets[next].U + appliedU != offsets[f].U + target.U
                            || offsets[next].V + appliedV != offsets[f].V + target.V)
                        {
                            works = false; break;
                        }
                    }
                if (!works) continue;
                metricAutomorphisms++;
                maximumResidual = Math.Max(maximumResidual, worst);
                for (int f = 1; f <= n; f++) Union(f, permutation[f]);
                break;
            }
        }
        if (metricAutomorphisms == 0)
            return Inconclusive("The identity isometry did not pass metric verification.");

        var classIds = new int[n + 1];
        var representatives = new List<int> { 0 };
        var seenRoots = new Dictionary<int, int>();
        for (int f = 1; f <= n; f++)
        {
            int representative = Find(f);
            if (!seenRoots.TryGetValue(representative, out int id))
            {
                id = representatives.Count;
                seenRoots.Add(representative, id);
                representatives.Add(f);
            }
            classIds[f] = id;
        }
        int count = representatives.Count - 1;
        int[][] quotientMaps = maps.Select(relation =>
            representatives.Select((old, i) => i == 0 ? 0 : classIds[relation[old]]).ToArray())
            .ToArray();
        int[] q01 = representatives.Select((old, i) => i == 0 ? 0 : m01[old]).ToArray();
        int[] q12 = representatives.Select((old, i) => i == 0 ? 0 : m12[old]).ToArray();
        var quotient = DelaneyDressTopology.Inspect(
            BuildSymbol(quotientMaps, q01, q12, count), 2048);
        if (quotient.Status != DelaneyDressStatus.Euclidean
            || quotient.Symbol!.ChamberCount != count
            || DelaneyDressTopology.ProjectChambers(derived.Symbol, quotient.Symbol) is null)
            return Inconclusive("The metric isometry quotient did not independently validate.");
        return new("verified", "Full-periodic polygon isometry and lattice action verified.",
            topology.TranslationDsSymbol, quotient.Symbol.Canonical, metricAutomorphisms,
            combinatorial.AutomorphismCount, n, count, maximumResidual,
            "complete-periodic-polygon-witness");
    }
}
