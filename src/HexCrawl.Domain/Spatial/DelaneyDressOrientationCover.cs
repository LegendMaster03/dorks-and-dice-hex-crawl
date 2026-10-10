using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// A finite orientation double cover, not necessarily a translation torus:
/// local rotational/cone-point branching may still remain.
/// </summary>
public sealed record DelaneyDressOrientationCoverResult(
    DelaneyDressCoverStatus Status,
    string? SourceSymbol = null,
    string? CoverSymbol = null,
    int ChamberCount = 0,
    IReadOnlyList<int>? SourceProjection = null,
    int RemainingBranchedOrbits = 0,
    bool IsTrivial = false,
    string? Reason = null);

/// <summary>
/// Strip orientation-reversing chamber identifications from a general
/// Euclidean D-symbol by adjoining one parity bit to every chamber.
/// The connected component is retained, so already orientable symbols
/// are not doubled artificially. The quotient projection is proved.
/// This does not claim unbranched polygon vertices or a metric embedding.
/// </summary>
public static class DelaneyDressOrientationCover
{
    public static DelaneyDressOrientationCoverResult Construct(string source, int chamberLimit = 1024)
    {
        static DelaneyDressOrientationCoverResult Unsupported(string reason) =>
            new(DelaneyDressCoverStatus.Unsupported, Reason: reason);
        static DelaneyDressOrientationCoverResult Inconclusive(string reason) =>
            new(DelaneyDressCoverStatus.Inconclusive, Reason: reason);
        if (chamberLimit < 1 || chamberLimit > 2048)
            return Unsupported("Invalid bounded orientation-cover chamber limit.");
        var inspected = DelaneyDressTopology.Inspect(source, chamberLimit);
        if (inspected.Status == DelaneyDressStatus.LimitExceeded)
            return Unsupported("Input D-symbol exceeds the bounded orientation-cover capacity.");
        if (inspected.Status != DelaneyDressStatus.Euclidean)
            return new(DelaneyDressCoverStatus.Invalid, Reason: "A valid Euclidean D-symbol is required.");
        var checkedSource = DelaneyDressTopology.Inspect(inspected.Symbol!.Canonical, chamberLimit);
        if (checkedSource.Status != DelaneyDressStatus.Euclidean)
            return Inconclusive("Canonical D-symbol numbering was inconsistent.");
        var symbol = checkedSource.Symbol!;
        if (2L * symbol.ChamberCount > chamberLimit && !(symbol.FixedPointFree && symbol.WeaklyOrientable))
            return Unsupported("The orientation double would exceed the bounded chamber capacity.");

        var pairs = new List<(int Chamber, int Parity)> { (1, 0) };
        var which = new Dictionary<(int Chamber, int Parity), int> { [(1, 0)] = 1 };
        int[][] maps = [new int[chamberLimit + 1], new int[chamberLimit + 1], new int[chamberLimit + 1]];
        for (int i = 0; i < pairs.Count; i++)
        {
            var (chamber, parity) = pairs[i];
            for (int k = 0; k < 3; k++)
            {
                var pair = (symbol.Involutions[k][chamber], 1 - parity);
                if (!which.TryGetValue(pair, out int target))
                {
                    if (pairs.Count >= chamberLimit)
                        return Unsupported("The connected orientation cover exceeds its chamber limit.");
                    target = pairs.Count + 1;
                    pairs.Add(pair);
                    which.Add(pair, target);
                }
                maps[k][i + 1] = target;
            }
        }

        int[] m01 = new int[pairs.Count + 1], m12 = new int[pairs.Count + 1];
        for (int i = 0; i < pairs.Count; i++)
        {
            m01[i + 1] = symbol.M01[pairs[i].Chamber];
            m12[i + 1] = symbol.M12[pairs[i].Chamber];
        }
        var lifted = DelaneyDressTopology.Inspect(Serialize(pairs.Count, maps, m01, m12), chamberLimit);
        if (lifted.Status != DelaneyDressStatus.Euclidean
            || lifted.Symbol is null || !lifted.Symbol.FixedPointFree || !lifted.Symbol.WeaklyOrientable)
            return Inconclusive("The orientation double did not preserve Euclidean curvature and orientability.");
        var projection = DelaneyDressTopology.ProjectChambers(lifted.Symbol, symbol);
        if (projection is null)
            return Inconclusive("The connected orientation cover does not project onto its input D-symbol.");
        return new(
            DelaneyDressCoverStatus.Constructed,
            symbol.Canonical,
            lifted.Symbol.Canonical,
            lifted.Symbol.ChamberCount,
            projection,
            CountBranchedOrbits(lifted.Symbol),
            lifted.Symbol.Canonical == symbol.Canonical);
    }

    private static int CountBranchedOrbits(DelaneyDressSymbol symbol)
    {
        int count = 0;
        foreach (int first in new[] { 0, 1, 2 })
        {
            var visited = new bool[symbol.ChamberCount + 1];
            int second = first == 2 ? 2 : first + 1;
            int firstMap = first == 2 ? 0 : first;
            for (int root = 1; root <= symbol.ChamberCount; root++)
            {
                if (visited[root]) continue;
                var orbit = new List<int> { root };
                visited[root] = true;
                for (int i = 0; i < orbit.Count; i++)
                    foreach (int k in new[] { firstMap, second })
                    {
                        int next = symbol.Involutions[k][orbit[i]];
                        if (visited[next]) continue;
                        visited[next] = true;
                        orbit.Add(next);
                    }
                int expected = first == 0 ? 2 * symbol.M01[root]
                    : first == 1 ? 2 * symbol.M12[root] : 4;
                if (orbit.Count != expected) count++;
            }
        }
        return count;
    }

    private static string Serialize(int size, int[][] maps, int[] m01, int[] m12)
    {
        string inv = string.Join(",", maps.Select(map => string.Join(" ",
            Enumerable.Range(1, size).Where(c => c <= map[c]).Select(c => map[c]))));
        string Labels(int first, int[] values)
        {
            var visited = new bool[size + 1];
            var result = new List<int>();
            for (int root = 1; root <= size; root++)
            {
                if (visited[root]) continue;
                result.Add(values[root]);
                var q = new List<int> { root };
                visited[root] = true;
                for (int i = 0; i < q.Count; i++)
                    foreach (int k in new[] { first, first + 1 })
                    {
                        int next = maps[k][q[i]];
                        if (visited[next]) continue;
                        visited[next] = true;
                        q.Add(next);
                    }
            }
            return string.Join(" ", result);
        }
        return $"<{size}:{inv}:{Labels(0, m01)},{Labels(1, m12)}>";
    }
}
