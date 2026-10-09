using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// A proved periodic polygonal metric witness, or an explicit unresolved result.
/// This is an additive domain capability; no stored world is created or updated.
/// </summary>
public sealed record DelaneyDressHarmonicMetricResult(
    string Status,
    PeriodicTopologyWitness? Topology = null,
    PeriodicMetricRealization? Realization = null,
    string? Reason = null);

/// <summary>
/// Independently construct candidate periodic polygon coordinates from a general
/// bounded Euclidean D-symbol. The combinatorial torus is derived separately;
/// its vertex-gluing cocycles are checked exactly before solving a gauge-fixed
/// harmonic Laplacian. The public metric validator is the final authority.
/// A harmonic solution is never assumed to be nonoverlapping or admissible.
/// </summary>
public static class DelaneyDressHarmonicMetricRealization
{
    private const int MaximumCells = 24;
    private const int MaximumCorners = 192;
    private const int MaximumVertices = 96;

    private readonly record struct CornerArc(int Target, LatticeDisplacement Displacement);
    private static TilingWorldPoint Add(TilingWorldPoint a, TilingWorldPoint b) =>
        new(a.X + b.X, a.Y + b.Y);
    private static LatticeDisplacement Sub(LatticeDisplacement a, LatticeDisplacement b) =>
        a.Add(b.Opposite());
    private static TilingWorldPoint Transform(TilingWorldPoint p, double scale, double cosine, double sine) =>
        new(scale * (p.X * cosine - p.Y * sine), scale * (p.X * sine + p.Y * cosine));

    private static double[]? SolveReducedLaplacian(double[,] full, double[] rhs)
    {
        int n = rhs.Length - 1;
        if (n == 0) return [];
        var augmented = new double[n, n + 1];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++) augmented[r, c] = full[r + 1, c + 1];
            augmented[r, n] = rhs[r + 1];
        }
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < n; row++)
                if (Math.Abs(augmented[row, col]) > Math.Abs(augmented[pivot, col]))
                    pivot = row;
            if (Math.Abs(augmented[pivot, col]) < 1e-10) return null;
            if (pivot != col)
                for (int k = col; k <= n; k++)
                    (augmented[pivot, k], augmented[col, k]) = (augmented[col, k], augmented[pivot, k]);
            double lead = augmented[col, col];
            for (int k = col; k <= n; k++) augmented[col, k] /= lead;
            for (int row = col + 1; row < n; row++)
            {
                double factor = augmented[row, col];
                if (factor == 0) continue;
                for (int k = col; k <= n; k++)
                    augmented[row, k] -= factor * augmented[col, k];
            }
        }
        var solution = new double[n];
        for (int row = n - 1; row >= 0; row--)
        {
            double value = augmented[row, n];
            for (int col = row + 1; col < n; col++)
                value -= augmented[row, col] * solution[col];
            if (!double.IsFinite(value)) return null;
            solution[row] = value;
        }
        return solution;
    }

    public static DelaneyDressHarmonicMetricResult Construct(
        string source, double worldUnitsPerAbstractPeriod, string units,
        double rotationDegrees = 0, int chamberLimit = 1024,
        IReadOnlyList<TilingMetricConstraint>? constraints = null)
    {
        static DelaneyDressHarmonicMetricResult Unresolved(string reason) =>
            new("unresolved-geometry", Reason: reason);
        if (!double.IsFinite(worldUnitsPerAbstractPeriod)
            || worldUnitsPerAbstractPeriod is <= 1e-6 or > 1e6
            || string.IsNullOrWhiteSpace(units) || !double.IsFinite(rotationDegrees)
            || Math.Abs(rotationDegrees) > 3600)
            return Unresolved("A finite positive period scale, world unit and rotation are required.");

        var abstractCover = DelaneyDressGeneralQuotientUnfolding.Construct(source, chamberLimit);
        if (abstractCover.Status != DelaneyDressCoverStatus.Constructed
            || abstractCover.Witness is null)
            return new(
                abstractCover.Status is DelaneyDressCoverStatus.Invalid or DelaneyDressCoverStatus.Unsupported
                    ? "unsupported" : "unresolved-geometry",
                Reason: abstractCover.Reason ?? "No proved finite Euclidean translation cover.");

        var topology = abstractCover.Witness;
        var cells = topology.MotifCells;
        if (cells.Count is < 1 or > MaximumCells)
            return Unresolved("The metric witness validator supports 1–24 motif cells.");

        var offsets = new int[cells.Count + 1];
        for (int i = 0; i < cells.Count; i++)
        {
            int sides = cells[i].Boundary.Count;
            if (sides is < 3 or > 32) return Unresolved("The bounded harmonic solver requires 3–32 atomic sides per cell.");
            offsets[i + 1] = checked(offsets[i] + sides);
        }
        int cornerCount = offsets[^1];
        if (cornerCount > MaximumCorners) return Unresolved("The bounded harmonic solver requires at most 192 corners.");
        var byId = cells.Select((cell, i) => (cell.Id, i))
            .ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var arcs = Enumerable.Range(0, cornerCount)
            .Select(_ => new List<CornerArc>()).ToArray();
        void Connect(int a, int b, LatticeDisplacement shift)
        {
            arcs[a].Add(new CornerArc(b, shift.Opposite()));
            arcs[b].Add(new CornerArc(a, shift));
        }
        try
        {
            for (int c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                for (int side = 0; side < cell.Boundary.Count; side++)
                {
                    var edge = cell.Boundary[side];
                    if (!byId.TryGetValue(edge.TargetMotifCellId, out int target))
                        return Unresolved("A topological boundary has no target cell.");
                    int peer = edge.ReciprocalInterfaceIndex, otherSides = cells[target].Boundary.Count;
                    if (peer < 0 || peer >= otherSides)
                        return Unresolved("A topological boundary has no reciprocal side.");
                    int here = offsets[c] + side;
                    int next = offsets[c] + (side + 1) % cell.Boundary.Count;
                    int otherStart = offsets[target] + peer;
                    int otherEnd = offsets[target] + (peer + 1) % otherSides;
                    Connect(here, otherEnd, edge.TargetTranslation);
                    Connect(next, otherStart, edge.TargetTranslation);
                }
            }

            var vertexOf = Enumerable.Repeat(-1, cornerCount).ToArray();
            var potential = new LatticeDisplacement[cornerCount];
            int vertexCount = 0;
            for (int root = 0; root < cornerCount; root++)
            {
                if (vertexOf[root] >= 0) continue;
                int component = vertexCount++;
                vertexOf[root] = component;
                var queue = new List<int> { root };
                for (int i = 0; i < queue.Count; i++)
                {
                    int current = queue[i];
                    foreach (var arc in arcs[current])
                    {
                        var expected = potential[current].Add(arc.Displacement);
                        if (vertexOf[arc.Target] < 0)
                        {
                            vertexOf[arc.Target] = component;
                            potential[arc.Target] = expected;
                            queue.Add(arc.Target);
                        }
                        else if (vertexOf[arc.Target] != component || potential[arc.Target] != expected)
                            return Unresolved("Translated corner gluing has nonclosing deck holonomy.");
                    }
                }
            }
            if (vertexCount is < 1 or > MaximumVertices)
                return Unresolved("The periodic quotient exceeds 96 harmonic vertex orbits.");

            var laplacian = new double[vertexCount, vertexCount];
            var rhsX = new double[vertexCount];
            var rhsY = new double[vertexCount];
            for (int c = 0; c < cells.Count; c++)
                for (int side = 0; side < cells[c].Boundary.Count; side++)
                {
                    int a = offsets[c] + side, b = offsets[c] + (side + 1) % cells[c].Boundary.Count;
                    int first = vertexOf[a], second = vertexOf[b];
                    if (first == second) continue;
                    var shift = Sub(potential[b], potential[a]);
                    laplacian[first, first]++;
                    laplacian[second, second]++;
                    laplacian[first, second]--;
                    laplacian[second, first]--;
                    rhsX[first] += shift.U;
                    rhsX[second] -= shift.U;
                    rhsY[first] += shift.V;
                    rhsY[second] -= shift.V;
                }
            var solX = SolveReducedLaplacian(laplacian, rhsX);
            var solY = SolveReducedLaplacian(laplacian, rhsY);
            if (solX is null || solY is null)
                return Unresolved("The periodic quotient has a singular harmonic embedding.");
            var coordinates = Enumerable.Range(0, vertexCount).Select(i =>
                new TilingWorldPoint(i == 0 ? 0 : solX[i - 1], i == 0 ? 0 : solY[i - 1])).ToArray();

            var originOffsets = new LatticeDisplacement[cells.Count];
            var polygons = new Dictionary<string, IReadOnlyList<TilingWorldPoint>>(StringComparer.Ordinal);
            for (int c = 0; c < cells.Count; c++)
            {
                var vertices = new TilingWorldPoint[cells[c].Boundary.Count];
                for (int side = 0; side < vertices.Length; side++)
                {
                    int at = offsets[c] + side;
                    var basePoint = coordinates[vertexOf[at]];
                    vertices[side] = Add(basePoint,
                        new TilingWorldPoint(potential[at].U, potential[at].V));
                }
                double centerX = vertices.Average(p => p.X), centerY = vertices.Average(p => p.Y);
                if (!double.IsFinite(centerX) || !double.IsFinite(centerY)
                    || Math.Abs(centerX) > 10000 || Math.Abs(centerY) > 10000)
                    return Unresolved("Harmonic polygon representative lies outside the bounded lattice domain.");
                var offset = new LatticeDisplacement(
                    checked((long)Math.Floor(centerX + 0.5)),
                    checked((long)Math.Floor(centerY + 0.5)));
                originOffsets[c] = offset;
                polygons[cells[c].Id] = vertices.Select(p =>
                    new TilingWorldPoint(p.X - offset.U, p.Y - offset.V)).ToArray();
            }

            // Changing each polygon's integer representative is a gauge
            // transformation. It changes its edge voltages by the exact
            // difference of source and target representative offsets.
            var shiftedCells = new List<PeriodicMotifCell>(cells.Count);
            for (int c = 0; c < cells.Count; c++)
            {
                var revisedEdges = cells[c].Boundary.Select(edge =>
                {
                    int target = byId[edge.TargetMotifCellId];
                    var shifted = edge.TargetTranslation
                        .Add(originOffsets[target])
                        .Add(originOffsets[c].Opposite());
                    return edge with { TargetTranslation = shifted };
                }).ToArray();
                shiftedCells.Add(cells[c] with { Boundary = revisedEdges });
            }
            var shiftedTopology = topology with { MotifCells = shiftedCells };
            shiftedTopology.ValidateAdjacency();
            double radians = rotationDegrees * Math.PI / 180;
            double cosine = Math.Cos(radians), sine = Math.Sin(radians);
            var metric = new PeriodicMetricRealization(
                units,
                Transform(new TilingWorldPoint(1, 0), worldUnitsPerAbstractPeriod, cosine, sine),
                Transform(new TilingWorldPoint(0, 1), worldUnitsPerAbstractPeriod, cosine, sine),
                polygons.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<TilingWorldPoint>)pair.Value
                        .Select(p => Transform(p, worldUnitsPerAbstractPeriod, cosine, sine))
                        .ToArray(),
                    StringComparer.Ordinal),
                constraints ?? []);
            PeriodicMetricWitnessValidator.Validate(shiftedTopology, metric);
            return new("realized", shiftedTopology, metric);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException
            or OverflowException or DivideByZeroException)
        {
            return Unresolved($"The harmonic polygon motif is not a verified periodic embedding: {error.Message}");
        }
    }
}
