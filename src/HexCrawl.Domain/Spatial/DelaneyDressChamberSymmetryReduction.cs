using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// A proved minimal COMBINATORIAL chamber quotient. This says nothing about
/// isometries of an individual metric realization or observed raster.
/// </summary>
public sealed record ChamberSymmetryReduction(
    string Status, string Reason, string? SourceDsSymbol = null,
    string? QuotientDsSymbol = null, int? SourceChambers = null,
    int? QuotientChambers = null, int? AutomorphismCount = null,
    string? Evidence = null);

public static class DelaneyDressChamberSymmetryReduction
{
    public static ChamberSymmetryReduction Construct(string source, int chamberLimit = 2048)
    {
        if (chamberLimit < 1 || chamberLimit > 2048)
            return new("unsupported-limit", "Invalid bounded chamber limit.");
        var parsed = DelaneyDressTopology.Inspect(source, chamberLimit);
        if (parsed.Status == DelaneyDressStatus.LimitExceeded)
            return new("unsupported-limit", "D-symbol exceeds bounded chamber limit.");
        if (parsed.Status != DelaneyDressStatus.Euclidean)
            return new("invalid", "A valid Euclidean D-symbol is required.");

        var canonical = DelaneyDressTopology.Inspect(parsed.Symbol!.Canonical, chamberLimit);
        if (canonical.Status != DelaneyDressStatus.Euclidean)
            return new("inconclusive", "Canonical D-symbol failed independent validation.");
        var symbol = canonical.Symbol!;
        int n = symbol.ChamberCount;
        int[][] maps = symbol.Involutions;
        var parent = Enumerable.Range(0, n + 1).ToArray();
        int Find(int chamber)
        {
            while (parent[chamber] != chamber)
            {
                parent[chamber] = parent[parent[chamber]];
                chamber = parent[chamber];
            }
            return chamber;
        }
        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }

        int automorphisms = 0;
        // Any color/label-preserving chamber automorphism of a connected
        // graph is determined by where it sends chamber 1. Enumerating those
        // n choices is exhaustive without factorial permutation search.
        for (int root = 1; root <= n; root++)
        {
            if (symbol.M01[1] != symbol.M01[root] || symbol.M12[1] != symbol.M12[root])
                continue;
            var mapping = new int[n + 1];
            mapping[1] = root;
            var queue = new List<int> { 1 };
            bool valid = true;
            for (int i = 0; i < queue.Count && valid; i++)
            {
                int from = queue[i], to = mapping[from];
                if (symbol.M01[from] != symbol.M01[to] || symbol.M12[from] != symbol.M12[to])
                {
                    valid = false;
                    break;
                }
                for (int k = 0; k < 3; k++)
                {
                    int next = maps[k][from], image = maps[k][to];
                    if (mapping[next] == 0)
                    {
                        mapping[next] = image;
                        queue.Add(next);
                    }
                    else if (mapping[next] != image)
                    {
                        valid = false;
                        break;
                    }
                }
            }
            if (!valid || queue.Count != n || mapping.Skip(1).Distinct().Count() != n)
                continue;
            automorphisms++;
            for (int i = 1; i <= n; i++) Union(i, mapping[i]);
        }
        if (automorphisms == 0)
            return new("inconclusive", "The identity automorphism was not found.");

        var representativeIds = new Dictionary<int, int>();
        var classes = new int[n + 1];
        var representatives = new List<int> { 0 };
        for (int chamber = 1; chamber <= n; chamber++)
        {
            int representative = Find(chamber);
            if (!representativeIds.TryGetValue(representative, out int id))
            {
                id = representatives.Count;
                representativeIds.Add(representative, id);
                representatives.Add(chamber);
            }
            classes[chamber] = id;
        }
        int count = representatives.Count - 1;
        var quotientMaps = new int[3][];
        for (int k = 0; k < 3; k++)
        {
            quotientMaps[k] = new int[count + 1];
            for (int i = 1; i <= count; i++)
                quotientMaps[k][i] = classes[maps[k][representatives[i]]];
        }
        var m01 = new int[count + 1];
        var m12 = new int[count + 1];
        for (int i = 1; i <= count; i++)
        {
            m01[i] = symbol.M01[representatives[i]];
            m12[i] = symbol.M12[representatives[i]];
        }
        string Compressed(int[] map) => string.Join(" ",
            Enumerable.Range(1, count).Where(i => i <= map[i]).Select(i => map[i]));
        string LabelText(int first, int[] labels)
        {
            var seen = new bool[count + 1];
            var values = new List<int>();
            for (int start = 1; start <= count; start++)
            {
                if (seen[start]) continue;
                values.Add(labels[start]);
                var queue = new List<int> { start };
                seen[start] = true;
                for (int i = 0; i < queue.Count; i++)
                    foreach (int k in new[] { first, first + 1 })
                    {
                        int next = quotientMaps[k][queue[i]];
                        if (seen[next]) continue;
                        seen[next] = true;
                        queue.Add(next);
                    }
            }
            return string.Join(" ", values);
        }
        string derived = $"<{count}:{string.Join(",", quotientMaps.Select(Compressed))}:{LabelText(0, m01)},{LabelText(1, m12)}>";
        var quotient = DelaneyDressTopology.Inspect(derived, chamberLimit);
        if (quotient.Status != DelaneyDressStatus.Euclidean
            || DelaneyDressTopology.ProjectChambers(symbol, quotient.Symbol!) is null
            || quotient.Symbol!.ChamberCount != count)
            return new("inconclusive", "Automorphism quotient failed independent D-symbol validation.");

        return new("reduced", "Combinatorial chamber automorphisms verified.",
            symbol.Canonical, quotient.Symbol.Canonical, n, count, automorphisms,
            "combinatorial-only");
    }
}
