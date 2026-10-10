using System;
using System.Collections.Generic;
using System.Linq;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// Construct a proven primitive translation cover of a finite uniform Euclidean
/// multi-chamber symmetry quotient by a connected fiber product with the
/// corresponding reflection group's unbranched torus chamber action.
/// This has no image, pattern-name catalogue, or user-supplied motif. It does
/// not realize mixed-face/vertex-order symbols or choose metric geometry.
/// </summary>
public static class DelaneyDressUniformQuotientUnfolding
{
    // Barycentric chamber actions of primitive regular Euclidean reflection
    // tori. These are algebraic Coxeter-group seeds, not named map geometries.
    // Their fixed-point freedom, exact local orders, and torus structure are
    // verified anew before every use. Each is independently exercised by the
    // TypeScript geometric one-chamber reflection constructor.
    private const string TriangularTorus = "<12:2 4 6 8 10 12,6 3 5 12 9 11,10 9 12 11 8 7:3 3,6>";
    private const string SquareTorus = "<8:2 4 6 8,8 3 5 7,6 5 8 7:4,4>";
    private const string HexagonalTorus = "<12:2 4 6 8 10 12,12 3 5 7 9 11,8 7 10 9 12 11:6,3 3>";

    public static DelaneyDressCoverResult Construct(string source, int chamberLimit = 768)
    {
        static DelaneyDressCoverResult Unsupported(string reason) =>
            new(DelaneyDressCoverStatus.Unsupported, null, reason);
        static DelaneyDressCoverResult Inconclusive(string reason) =>
            new(DelaneyDressCoverStatus.Inconclusive, null, reason);
        if (chamberLimit is < 8 or > 1024)
            return Unsupported("Invalid bounded fiber-product chamber limit.");
        var inspected = DelaneyDressTopology.Inspect(source, chamberLimit);
        if (inspected.Status == DelaneyDressStatus.LimitExceeded)
            return Unsupported("Source symbol exceeds the unfolding limit.");
        if (inspected.Status != DelaneyDressStatus.Euclidean)
            return new(DelaneyDressCoverStatus.Invalid, null, "A valid Euclidean D-symbol is required.");
        var canonical = DelaneyDressTopology.Inspect(inspected.Symbol!.Canonical, chamberLimit);
        if (canonical.Status != DelaneyDressStatus.Euclidean)
            return Inconclusive("Canonical chamber normalization was inconsistent.");
        var symbol = canonical.Symbol!;
        int p = symbol.M01[1], q = symbol.M12[1];
        if ((long)(p - 2) * (q - 2) != 4
            || symbol.M01.Skip(1).Any(v => v != p)
            || symbol.M12.Skip(1).Any(v => v != q))
            return Unsupported("The quotient has nonuniform face or vertex orders; general orbifold unfolding is required.");

        string? torusSeed = (p, q) switch
        {
            (3, 6) => TriangularTorus,
            (4, 4) => SquareTorus,
            (6, 3) => HexagonalTorus,
            _ => null
        };
        if (torusSeed is null) return Unsupported("No bounded universal reflection torus for the supplied local orders.");
        var seed = DelaneyDressTopology.Inspect(torusSeed, chamberLimit);
        if (seed.Status != DelaneyDressStatus.Euclidean || seed.Symbol is null
            || !seed.Symbol.FixedPointFree || !seed.Symbol.WeaklyOrientable
            || DelaneyDressTorusCover.Construct(seed.Symbol.Canonical, chamberLimit).Status
                != DelaneyDressCoverStatus.Constructed)
            return Inconclusive("The universal regular reflection torus seed did not pass independent validation.");
        var torus = seed.Symbol;
        if ((long)torus.ChamberCount * symbol.ChamberCount > chamberLimit)
            return Unsupported("The bounded fiber-product chamber count is too large.");

        var pairs = new List<(int Torus, int Source)> { (1, 1) };
        var which = new Dictionary<(int Torus, int Source), int> { [(1, 1)] = 1 };
        int[][] maps = [new int[chamberLimit + 1], new int[chamberLimit + 1], new int[chamberLimit + 1]];
        for (int i = 0; i < pairs.Count; i++)
        {
            var (t, s) = pairs[i];
            for (int k = 0; k < 3; k++)
            {
                var pair = (torus.Involutions[k][t], symbol.Involutions[k][s]);
                if (!which.TryGetValue(pair, out int next))
                {
                    if (pairs.Count == chamberLimit)
                        return Unsupported("The connected unfolding exceeds its chamber limit.");
                    next = pairs.Count + 1;
                    pairs.Add(pair);
                    which.Add(pair, next);
                }
                maps[k][i + 1] = next;
            }
        }
        int n = pairs.Count;
        string expandedNotation = Serialize(n, maps, p, q);
        var expanded = DelaneyDressTopology.Inspect(expandedNotation, chamberLimit);
        if (expanded.Status != DelaneyDressStatus.Euclidean || expanded.Symbol is null
            || !expanded.Symbol.FixedPointFree || !expanded.Symbol.WeaklyOrientable)
            return Inconclusive("The connected fiber product was not an unbranched orientable Euclidean cover.");
        var candidate = DelaneyDressTorusCover.Construct(expanded.Symbol.Canonical, chamberLimit);
        if (candidate.Status != DelaneyDressCoverStatus.Constructed || candidate.Witness is null)
            return Inconclusive($"The fiber product is not a proved translational torus: {candidate.Reason}");
        if (DelaneyDressTopology.ProjectChambers(expanded.Symbol, symbol) is null
            || DelaneyDressTopology.ProjectChambers(expanded.Symbol, torus) is null)
            return Inconclusive("The fiber product does not project onto both quotient factors.");
        var witness = candidate.Witness with
        {
            QuotientDsSymbol = symbol.Canonical,
            Provenance = "derived:uniform-euclidean-reflection-fiber-product"
        };
        try
        {
            witness.ValidateAdjacency();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or OverflowException)
        {
            return Inconclusive($"Unfolded chamber incidence failed independent witness validation: {error.Message}");
        }
        return new(DelaneyDressCoverStatus.Constructed, witness);
    }

    private static string Serialize(int count, int[][] maps, int p, int q)
    {
        var involutions = string.Join(",", maps.Select(map => string.Join(" ",
            Enumerable.Range(1, count).Where(i => i <= map[i]).Select(i => map[i]))));
        string OrbitLabels(int first, int value)
        {
            var seen = new bool[count + 1];
            var labels = new List<int>();
            for (int start = 1; start <= count; start++)
            {
                if (seen[start]) continue;
                labels.Add(value);
                var queue = new List<int> { start };
                seen[start] = true;
                for (int i = 0; i < queue.Count; i++)
                {
                    foreach (int step in new[] { first, first + 1 })
                    {
                        int next = maps[step][queue[i]];
                        if (seen[next]) continue;
                        seen[next] = true;
                        queue.Add(next);
                    }
                }
            }
            return string.Join(" ", labels);
        }
        return $"<{count}:{involutions}:{OrbitLabels(0, p)},{OrbitLabels(1, q)}>";
    }
}
