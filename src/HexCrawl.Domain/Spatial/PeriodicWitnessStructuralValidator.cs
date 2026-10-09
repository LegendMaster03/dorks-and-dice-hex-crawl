using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HexCrawl.Domain.Spatial;

/// <summary>
/// Reconstructs the translation D-symbol from a finite, oriented boundary-cycle
/// witness and checks that its periodic voltages produce one primitive Z² cover.
/// Geometry and map registration are intentionally separate responsibilities.
/// </summary>
internal static class PeriodicWitnessStructuralValidator
{
    private readonly record struct Shift(BigInteger U, BigInteger V)
    {
        public static Shift operator +(Shift a, Shift b) => new(a.U + b.U, a.V + b.V);
        public static Shift operator -(Shift a, Shift b) => new(a.U - b.U, a.V - b.V);
    }
    private static InvalidOperationException Invalid(string reason) => new(reason);

    public static void Validate(PeriodicTopologyWitness witness)
    {
        var cells = witness.MotifCells;
        var offsets = new int[cells.Count + 1];
        for (int i = 0; i < cells.Count; i++)
            offsets[i + 1] = checked(offsets[i] + cells[i].Boundary.Count);
        int chamberCount = checked(2 * offsets[cells.Count]);
        if (chamberCount > 2048)
            throw Invalid("Translation chamber graph exceeds the 2048-flag safety limit.");

        var byId = cells.Select((cell, index) => (cell.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
        int[][] maps = [new int[chamberCount + 1], new int[chamberCount + 1], new int[chamberCount + 1]];
        var m01 = new int[chamberCount + 1];
        var s2Shift = new Shift[chamberCount + 1];
        var adjacency = Enumerable.Range(0, cells.Count)
            .Select(_ => new List<(int Target, Shift Shift)>()).ToArray();

        for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
        {
            var cell = cells[cellIndex];
            int sides = cell.Boundary.Count;
            for (int side = 0; side < sides; side++)
            {
                var edge = cell.Boundary[side];
                int target = byId[edge.TargetMotifCellId];
                var shift = new Shift(edge.TargetTranslation.U, edge.TargetTranslation.V);
                int a = checked(2 * (offsets[cellIndex] + side) + 1);
                int peer = checked(2 * (offsets[target] + edge.ReciprocalInterfaceIndex) + 1);
                int prev = checked(2 * (offsets[cellIndex] + (side + sides - 1) % sides) + 1);
                int next = checked(2 * (offsets[cellIndex] + (side + 1) % sides) + 1);
                maps[0][a] = a + 1; maps[0][a + 1] = a;
                maps[1][a] = prev + 1; maps[1][a + 1] = next;
                maps[2][a] = peer + 1; maps[2][a + 1] = peer;
                s2Shift[a] = shift; s2Shift[a + 1] = shift;
                m01[a] = sides; m01[a + 1] = sides;
                adjacency[cellIndex].Add((target, shift));
            }
        }

        var m12 = new int[chamberCount + 1];
        var visited = new bool[chamberCount + 1];
        for (int root = 1; root <= chamberCount; root++)
        {
            if (visited[root]) continue;
            visited[root] = true;
            var queue = new List<int> { root };
            var positions = new Dictionary<int, Shift> { [root] = default };
            for (int p = 0; p < queue.Count; p++)
            {
                int current = queue[p];
                foreach (int map in new[] { 1, 2 })
                {
                    int target = maps[map][current];
                    Shift translation = positions[current] + (map == 1 ? default : s2Shift[current]);
                    if (!positions.TryGetValue(target, out var known))
                    {
                        positions.Add(target, translation);
                        visited[target] = true;
                        queue.Add(target);
                    }
                    else if (known != translation)
                    {
                        throw Invalid("Vertex orbit does not close in the translation cover.");
                    }
                }
            }
            if (queue.Count % 2 != 0)
                throw Invalid("Vertex orbit contains an odd number of chambers.");
            foreach (int member in queue) m12[member] = queue.Count / 2;
        }

        string involutions = string.Join(",", maps.Select(map => string.Join(" ",
            Enumerable.Range(1, chamberCount).Where(i => i <= map[i]).Select(i => map[i]))));
        string LabelText(int first, int[] labels)
        {
            var seen = new bool[chamberCount + 1];
            var result = new List<int>();
            for (int start = 1; start <= chamberCount; start++)
            {
                if (seen[start]) continue;
                result.Add(labels[start]);
                var queue = new List<int> { start };
                seen[start] = true;
                for (int p = 0; p < queue.Count; p++)
                    foreach (int map in new[] { first, first + 1 })
                    {
                        int next = maps[map][queue[p]];
                        if (seen[next]) continue;
                        seen[next] = true;
                        queue.Add(next);
                    }
            }
            return string.Join(" ", result);
        }
        string derivedText = $"<{chamberCount}:{involutions}:{LabelText(0, m01)},{LabelText(1, m12)}>";
        var observed = DelaneyDressTopology.Inspect(derivedText, 2048);
        var asserted = DelaneyDressTopology.Inspect(witness.TranslationDsSymbol, 2048);
        if (observed.Status != DelaneyDressStatus.Euclidean
            || asserted.Status != DelaneyDressStatus.Euclidean
            || !string.Equals(observed.Symbol!.Canonical, asserted.Symbol!.Canonical, StringComparison.Ordinal))
            throw Invalid("Finite motif incidence does not match the declared translation D-symbol.");

        // The finite motif must be connected, and the closed-walk translation
        // subgroup must be all of Z², not a proper-index sublattice.
        var potentials = new Dictionary<int, Shift> { [0] = default };
        var reach = new List<int> { 0 };
        var cycles = new List<Shift>();
        BigInteger index = BigInteger.Zero;
        for (int p = 0; p < reach.Count; p++)
        {
            int cellIndex = reach[p];
            foreach (var (target, shift) in adjacency[cellIndex])
            {
                Shift location = potentials[cellIndex] + shift;
                if (!potentials.TryGetValue(target, out var known))
                {
                    potentials.Add(target, location);
                    reach.Add(target);
                    continue;
                }
                Shift cycle = location - known;
                if (cycle == default) continue;
                foreach (var previous in cycles)
                {
                    BigInteger determinant = cycle.U * previous.V - cycle.V * previous.U;
                    index = BigInteger.GreatestCommonDivisor(index, determinant);
                }
                cycles.Add(cycle);
            }
        }
        if (reach.Count != cells.Count || index != BigInteger.One)
            throw Invalid("Motif does not form one connected primitive Z² translation cover.");
    }
}
