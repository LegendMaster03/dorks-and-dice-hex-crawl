using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace HexCrawl.Domain.Spatial;

/// <summary>A finite 2D Delaney–Dress symmetry quotient. This is not an operational translation cover.</summary>
public sealed record DelaneyDressSymbol(
    string Canonical,
    int ChamberCount,
    int[][] Involutions,
    int[] M01,
    int[] M12,
    bool WeaklyOrientable,
    bool FixedPointFree);

public enum DelaneyDressStatus { Euclidean, NonEuclidean, SyntaxInvalid, StructureInvalid, LimitExceeded }
public sealed record DelaneyDressInspection(DelaneyDressStatus Status, DelaneyDressSymbol? Symbol, string? Reason = null);
public enum TilingComparisonStatus
{
    ExactIdentity, ChamberIsomorphic, ProvenEquivalent, MetricallyIncompatible,
    StructurallyIncompatible, Inconclusive, Invalid, LimitExceeded
}
public sealed record TilingComparison(TilingComparisonStatus Status, string Reason);
public sealed record TilingMetricConstraint(string Key, string Unit, double Min, double Max);

/// <summary>
/// Local mathematical validation and conservative compatibility. Never contacts Surveyor.
/// D-symbol identity is combinatorial and does not assert any particular embedding.
/// </summary>
public static class DelaneyDressTopology
{
    private static readonly Regex Grammar = new(
        @"^\s*<\s*(?:(\d+\.\d+)\s*:\s*)?(\d+)\s*:\s*([^:]+)\s*:\s*([^:]+)\s*>\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static DelaneyDressInspection Inspect(string? text, int chamberLimit = 256)
    {
        try
        {
            var match = Grammar.Match(text ?? "");
            if (!match.Success)
                throw new SymbolException(DelaneyDressStatus.SyntaxInvalid, "Expected <size:s0,s1,s2:m01,m12>.");
            int size = Positive(match.Groups[2].Value);
            if (chamberLimit < 1)
                throw new SymbolException(DelaneyDressStatus.SyntaxInvalid, "Invalid chamber limit.");
            if (size > chamberLimit)
                throw new SymbolException(DelaneyDressStatus.LimitExceeded, $"Symbol exceeds {chamberLimit} chambers.");

            var fields = match.Groups[3].Value.Split(',');
            if (fields.Length != 3) throw new SymbolException(DelaneyDressStatus.SyntaxInvalid, "Expected three involutions.");
            var maps = fields.Select((field, index) => ParseInvolution(field, size, index)).ToArray();
            var visited = new HashSet<int> { 1 };
            var queue = new List<int> { 1 };
            for (int p = 0; p < queue.Count; p++)
                foreach (var map in maps)
                    if (visited.Add(map[queue[p]])) queue.Add(map[queue[p]]);
            if (visited.Count != size)
                throw new SymbolException(DelaneyDressStatus.StructureInvalid, "Disconnected chamber graph.");
            for (int i = 1; i <= size; i++)
                if (maps[0][maps[2][i]] != maps[2][maps[0][i]])
                    throw new SymbolException(DelaneyDressStatus.StructureInvalid, "s0 and s2 do not commute.");

            var orbitLabels = match.Groups[4].Value.Split(',');
            if (orbitLabels.Length != 2)
                throw new SymbolException(DelaneyDressStatus.SyntaxInvalid, "Expected m01 and m12.");
            int[] m01 = ParseLabels(orbitLabels[0], maps, 0, size);
            int[] m12 = ParseLabels(orbitLabels[1], maps, 1, size);

            var sign = new int[size + 1];
            bool orientable = true;
            for (int root = 1; root <= size; root++)
            {
                if (sign[root] != 0) continue;
                sign[root] = 1;
                var q = new List<int> { root };
                for (int p = 0; p < q.Count; p++)
                    foreach (var map in maps)
                    {
                        int next = map[q[p]];
                        if (next == q[p]) continue;
                        if (sign[next] == 0) { sign[next] = -sign[q[p]]; q.Add(next); }
                        else if (sign[next] == sign[q[p]]) orientable = false;
                    }
            }
            var common = new BigInteger(2);
            for (int i = 1; i <= size; i++)
                foreach (int label in new[] { m01[i], m12[i] })
                    common = BigInteger.Abs(common / BigInteger.GreatestCommonDivisor(common, label) * label);
            BigInteger curvature = -size * (common / 2);
            for (int i = 1; i <= size; i++)
                curvature += common / m01[i] + common / m12[i];
            var symbol = new DelaneyDressSymbol(
                Canonical(size, maps, m01, m12), size, maps, m01, m12, orientable,
                maps.All(map => Enumerable.Range(1, size).All(c => map[c] != c)));
            return new(curvature.IsZero ? DelaneyDressStatus.Euclidean : DelaneyDressStatus.NonEuclidean,
                symbol, curvature.IsZero ? null : (curvature.Sign > 0 ? "Spherical curvature." : "Hyperbolic curvature."));
        }
        catch (SymbolException error)
        {
            return new(error.Status, null, error.Message);
        }
    }

    private sealed class SymbolException(DelaneyDressStatus status, string message) : Exception(message)
    {
        public DelaneyDressStatus Status { get; } = status;
    }
    private static int Positive(string text)
    {
        if (!Regex.IsMatch(text, @"^[1-9]\d*$", RegexOptions.CultureInvariant) || !int.TryParse(text, out int value))
            throw new SymbolException(DelaneyDressStatus.SyntaxInvalid, "Expected a positive integer within range.");
        return value;
    }
    private static int[] Entries(string raw) => string.IsNullOrWhiteSpace(raw)
        ? [] : Regex.Split(raw.Trim(), @"\s+").Select(Positive).ToArray();
    private static int[] ParseInvolution(string raw, int size, int index)
    {
        var values = Entries(raw);
        var map = new int[size + 1];
        int p = 0;
        for (int chamber = 1; chamber <= size; chamber++)
        {
            if (map[chamber] != 0) continue;
            int target = p < values.Length ? values[p++] : 0;
            if (target < chamber || target > size || map[target] != 0)
                throw new SymbolException(DelaneyDressStatus.StructureInvalid, $"Invalid s{index} involution.");
            map[chamber] = target;
            map[target] = chamber;
        }
        if (p != values.Length)
            throw new SymbolException(DelaneyDressStatus.SyntaxInvalid, $"Excess s{index} entries.");
        return map;
    }
    private static List<int> Orbit(int[][] maps, int first, int root)
    {
        var visited = new HashSet<int> { root };
        var q = new List<int> { root };
        for (int p = 0; p < q.Count; p++)
            foreach (int k in new[] { first, first + 1 })
                if (visited.Add(maps[k][q[p]])) q.Add(maps[k][q[p]]);
        return q;
    }
    private static int[] ParseLabels(string raw, int[][] maps, int first, int size)
    {
        var values = Entries(raw);
        var labels = new int[size + 1];
        int p = 0;
        for (int chamber = 1; chamber <= size; chamber++)
        {
            if (labels[chamber] != 0) continue;
            var members = Orbit(maps, first, chamber);
            int current = chamber, order = 0;
            do
            {
                current = maps[first + 1][maps[first][current]];
                if (++order > size)
                    throw new SymbolException(DelaneyDressStatus.StructureInvalid, "Invalid orbit rotation.");
            } while (current != chamber);
            int label = p < values.Length ? values[p++] : 0;
            if (label < 2 || label % order != 0)
                throw new SymbolException(DelaneyDressStatus.StructureInvalid, "Invalid orbit multiplicity.");
            foreach (int member in members) labels[member] = label;
        }
        if (p != values.Length)
            throw new SymbolException(DelaneyDressStatus.SyntaxInvalid, "Excess orbit labels.");
        return labels;
    }

    private static string Canonical(int size, int[][] maps, int[] m01, int[] m12)
    {
        string? best = null;
        for (int root = 1; root <= size; root++)
        {
            var oldToNew = new int[size + 1];
            var newToOld = new List<int> { 0, root };
            oldToNew[root] = 1;
            for (int p = 1; p <= size; p++)
                foreach (var map in maps)
                {
                    int old = map[newToOld[p]];
                    if (oldToNew[old] == 0) { oldToNew[old] = newToOld.Count; newToOld.Add(old); }
                }
            var translated = maps.Select(map => Enumerable.Range(0, size + 1)
                .Select(i => i == 0 ? 0 : oldToNew[map[newToOld[i]]]).ToArray()).ToArray();
            var involutions = string.Join(",", translated.Select(map => string.Join(" ", Enumerable.Range(1, size)
                .Where(i => i <= map[i]).Select(i => map[i]))));
            var labels = new[] { m01, m12 };
            var orbitText = string.Join(",", Enumerable.Range(0, 2).Select(first =>
            {
                var seen = new HashSet<int>();
                var values = new List<int>();
                for (int c = 1; c <= size; c++)
                {
                    if (seen.Contains(c)) continue;
                    values.Add(labels[first][newToOld[c]]);
                    foreach (int member in Orbit(translated, first, c)) seen.Add(member);
                }
                return string.Join(" ", values);
            }));
            string candidate = $"<{size}:{involutions}:{orbitText}>";
            if (best is null || StringComparer.Ordinal.Compare(candidate, best) < 0) best = candidate;
        }
        return best!;
    }

    public static int[]? ProjectChambers(DelaneyDressSymbol cover, DelaneyDressSymbol quotient)
    {
        if (cover.ChamberCount < quotient.ChamberCount) return null;
        for (int root = 1; root <= quotient.ChamberCount; root++)
        {
            var mapping = new int[cover.ChamberCount + 1];
            mapping[1] = root;
            var q = new List<int> { 1 };
            bool valid = true;
            for (int p = 0; p < q.Count && valid; p++)
            {
                int source = q[p], target = mapping[source];
                if (cover.M01[source] != quotient.M01[target] || cover.M12[source] != quotient.M12[target]) { valid = false; break; }
                for (int k = 0; k < 3; k++)
                {
                    int s = cover.Involutions[k][source], t = quotient.Involutions[k][target];
                    if (mapping[s] == 0) { mapping[s] = t; q.Add(s); }
                    else if (mapping[s] != t) { valid = false; break; }
                }
            }
            if (valid && q.Count == cover.ChamberCount && mapping.Skip(1).Distinct().Count() == quotient.ChamberCount)
                return mapping;
        }
        return null;
    }

    private static bool MetricConflict(IEnumerable<TilingMetricConstraint> left, IEnumerable<TilingMetricConstraint> right) =>
        left.Any(a => right.Any(b => a.Key == b.Key && a.Unit == b.Unit
            && double.IsFinite(a.Min) && double.IsFinite(a.Max) && a.Min <= a.Max
            && double.IsFinite(b.Min) && double.IsFinite(b.Max) && b.Min <= b.Max
            && (a.Max < b.Min || b.Max < a.Min)));

    public static TilingComparison Compare(
        string left, string right, string? commonCover = null,
        IEnumerable<TilingMetricConstraint>? leftMetric = null,
        IEnumerable<TilingMetricConstraint>? rightMetric = null)
    {
        var l = Inspect(left); var r = Inspect(right);
        if (l.Status == DelaneyDressStatus.LimitExceeded || r.Status == DelaneyDressStatus.LimitExceeded)
            return new(TilingComparisonStatus.LimitExceeded, "Chamber limit exceeded.");
        if (l.Status != DelaneyDressStatus.Euclidean || r.Status != DelaneyDressStatus.Euclidean)
            return new(TilingComparisonStatus.Invalid, "Expected two valid Euclidean symbols.");
        var a = l.Symbol!; var b = r.Symbol!;
        bool conflict = MetricConflict(leftMetric ?? [], rightMetric ?? []);
        if (a.Canonical == b.Canonical)
            return conflict ? new(TilingComparisonStatus.MetricallyIncompatible, "Metric constraint intervals do not overlap.")
                : string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal)
                    ? new(TilingComparisonStatus.ExactIdentity, "Identical symbols.")
                    : new(TilingComparisonStatus.ChamberIsomorphic, "Identical canonical chamber graph.");
        static string Degrees(int[] labels) => string.Join(",", labels.Skip(1).Distinct().OrderBy(x => x));
        if (Degrees(a.M01) != Degrees(b.M01) || Degrees(a.M12) != Degrees(b.M12))
            return new(TilingComparisonStatus.StructurallyIncompatible, "Polygon degree or vertex valence differs.");
        if (commonCover is not null)
        {
            var candidate = Inspect(commonCover, 2048);
            if (candidate.Status == DelaneyDressStatus.Euclidean
                && ProjectChambers(candidate.Symbol!, a) is not null
                && ProjectChambers(candidate.Symbol!, b) is not null)
                return conflict ? new(TilingComparisonStatus.MetricallyIncompatible, "Same topology but incompatible metric intervals.")
                    : new(TilingComparisonStatus.ProvenEquivalent, "A finite common chamber cover projects onto both symbols.");
        }
        return new(TilingComparisonStatus.Inconclusive, "No proven common chamber cover.");
    }
}
