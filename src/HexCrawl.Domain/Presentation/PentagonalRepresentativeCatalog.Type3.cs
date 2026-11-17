using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Type 3 is a three-pentagon subdivision of a regular hexagon: consecutive
    /// sectors are separated at alternating edge midpoints. Every tile has
    /// 5 geometric sides, but its long outer side has two atomic interfaces.
    /// The latter is exactly the general non-edge-to-edge contract, not a
    /// pentagon-specific exception.
    /// Independent construction reference:
    /// https://commons.wikimedia.org/wiki/File:Pent-Hex-Type3-3.svg
    /// </summary>
    internal static TilingNameEntry HexagonalSubdivisionType3()
    {
        const string symbol = "<36:2 7 6 10 14 16 18 20 21 24 26 29 30 28 27 34 35 36,3 5 9 12 13 15 17 19 23 25 28 30 31 32 33 34 35 36,4 6 8 11 14 15 18 19 22 24 27 29 30 32 26 35 36 34:6 6 6,3 3 3 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("A",
                [
                    new(0, 0, "C", new(0, 0), 5),
                    new(1, 1, "B", new(1, 0), 2),
                    new(2, 2, "C", new(0, 1), 1),
                    new(3, 2, "B", new(0, 1), 4),
                    new(4, 3, "C", new(-1, 1), 3),
                    new(5, 4, "B", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("B",
                [
                    new(0, 0, "A", new(0, 0), 5),
                    new(1, 1, "C", new(-1, 1), 2),
                    new(2, 2, "A", new(-1, 0), 1),
                    new(3, 2, "C", new(-1, 0), 4),
                    new(4, 3, "A", new(0, -1), 3),
                    new(5, 4, "C", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("C",
                [
                    new(0, 0, "B", new(0, 0), 5),
                    new(1, 1, "A", new(0, -1), 2),
                    new(2, 2, "B", new(1, -1), 1),
                    new(3, 2, "A", new(1, -1), 4),
                    new(4, 3, "B", new(1, 0), 3),
                    new(5, 4, "A", new(0, 0), 0)
                ])
            ],
            "Three congruent convex pentagons per regular hexagon, alternating edge-midpoint subdivision.");
        double y = Math.Sqrt(3) / 2;
        var metric = new PeriodicMetricRealization(
            "world-unit", new(1.5, y), new(0, 2 * y),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["A"] =
                [
                    new(0, 0), new(.75, y / 2), new(.5, y),
                    new(0, y), new(-.5, y), new(-.75, y / 2)
                ],
                ["B"] =
                [
                    new(0, 0), new(-.75, y / 2), new(-1, 0),
                    new(-.75, -y / 2), new(-.5, -y), new(0, -y)
                ],
                ["C"] =
                [
                    new(0, 0), new(0, -y), new(.5, -y),
                    new(.75, -y / 2), new(1, 0), new(.75, y / 2)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Hexagon subdivision — Type 3 pentagons", null,
            ["pentagonal tiling type 3"],
            "Three congruent convex pentagons in each regular hexagonal motif",
            "Derived from the publicly documented three-pentagon hexagon decomposition, P5-type3",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-3",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
