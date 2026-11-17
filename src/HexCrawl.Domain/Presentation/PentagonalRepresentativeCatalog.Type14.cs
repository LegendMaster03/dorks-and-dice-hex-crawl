using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Fixed-shape Type 14 (Stein). Reconstructed using algebraic side
    /// lengths rather than the rounded OpenSCAD reference coordinates:
    /// a=(sqrt(57)-3)/8, k=sqrt(1-a²), h=sqrt(4-(3a-1)²).
    /// The source's decimal quantities 0.822525, 1.87118, 3.51623, etc.
    /// are the rounded values of k, h, h+2k and their linear combinations.
    /// Source: mathgrrl's Apache-2.0-licensed pentagon_tilings.scad
    /// (based on Ed Pegg Jr's Wolfram Demonstrations).
    /// No shape-specific logic is added to the periodic runtime.
    /// </summary>
    internal static TilingNameEntry SteinType14()
    {
        const string symbol = "<72:2 7 6 10 14 16 18 20 21 24 26 29 31 33 35 37 39 41 44 46 48 50 52 54 55 57 60 61 63 66 64 65 67 70 72 71,3 5 9 12 13 15 17 19 23 25 28 30 26 32 34 38 40 43 45 41 47 49 51 53 56 59 55 62 65 61 67 63 69 71 72 70,4 6 8 11 14 15 18 19 22 24 27 29 31 32 36 37 38 42 44 47 48 50 53 54 49 58 60 64 66 61 63 65 68 71 72 70:5 5 6 6 7 7,3 3 3 3 3 3 3 3 3 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("P14-00",
                [
                    new(0, 0, "P14-01", new(0, 0), 5),
                    new(1, 1, "P14-05", new(0, 0), 3),
                    new(2, 2, "P14-02", new(0, 0), 2),
                    new(3, 3, "P14-04", new(1, 0), 2),
                    new(4, 4, "P14-03", new(1, 0), 0)
                ]),
                new PeriodicMotifCell("P14-01",
                [
                    new(0, 0, "P14-02", new(0, 1), 4),
                    new(1, 0, "P14-05", new(0, 1), 1),
                    new(2, 1, "P14-04", new(0, 1), 5),
                    new(3, 1, "P14-03", new(0, 0), 3),
                    new(4, 2, "P14-05", new(0, 0), 4),
                    new(5, 3, "P14-00", new(0, 0), 0),
                    new(6, 4, "P14-03", new(1, 0), 6)
                ]),
                new PeriodicMotifCell("P14-02",
                [
                    new(0, 0, "P14-03", new(1, -1), 5),
                    new(1, 1, "P14-04", new(1, 0), 3),
                    new(2, 2, "P14-00", new(0, 0), 2),
                    new(3, 3, "P14-05", new(0, 0), 2),
                    new(4, 4, "P14-01", new(0, -1), 0)
                ]),
                new PeriodicMotifCell("P14-03",
                [
                    new(0, 0, "P14-00", new(-1, 0), 4),
                    new(1, 0, "P14-04", new(0, 0), 1),
                    new(2, 1, "P14-05", new(0, 0), 5),
                    new(3, 1, "P14-01", new(0, 0), 3),
                    new(4, 2, "P14-04", new(0, 1), 4),
                    new(5, 3, "P14-02", new(-1, 1), 0),
                    new(6, 4, "P14-01", new(-1, 0), 6)
                ]),
                new PeriodicMotifCell("P14-04",
                [
                    new(0, 0, "P14-05", new(0, 0), 0),
                    new(1, 1, "P14-03", new(0, 0), 1),
                    new(2, 2, "P14-00", new(-1, 0), 3),
                    new(3, 3, "P14-02", new(-1, 0), 1),
                    new(4, 3, "P14-03", new(0, -1), 4),
                    new(5, 4, "P14-01", new(0, -1), 2)
                ]),
                new PeriodicMotifCell("P14-05",
                [
                    new(0, 0, "P14-04", new(0, 0), 0),
                    new(1, 1, "P14-01", new(0, -1), 1),
                    new(2, 2, "P14-02", new(0, 0), 3),
                    new(3, 3, "P14-00", new(0, 0), 1),
                    new(4, 3, "P14-01", new(0, 0), 4),
                    new(5, 4, "P14-03", new(0, 0), 2)
                ])
            ],
            "Stein Type 14: exact algebraic restoration of source's rounded six-pentagon motif.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(0.9278696191462994, 2.742655696514814),
            new(-1.702956953170395, 2.256150005417596),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["P14-00"] =
                [
                    new(0, 1.400724256602157),
                    new(0, 0),
                    new(0.52, 0),
                    new(0.8872177148777962, 0.9730113821944375),
                    new(0.2957392382925987, 1.828437131009876)
                ],
                ["P14-01"] =
                [
                    new(-0.2957392382925987, 1.828437131009876),
                    new(-0.5914784765851975, 1.400724256602157),
                    new(-0.8872177148777962, 0.9730113821944375),
                    new(-0.7036088574388981, 0.4865056910972188),
                    new(-0.52, 0),
                    new(0, 0),
                    new(0, 1.400724256602157)
                ],
                ["P14-02"] =
                [
                    new(1.407217714877796, -0.4277128744077193),
                    new(1.407217714877796, 0.9730113821944375),
                    new(0.8872177148777962, 0.9730113821944375),
                    new(0.52, 0),
                    new(1.111478476585197, -0.8554257488154386)
                ],
                ["P14-03"] =
                [
                    new(-0.9278696191462994, -1.341931439912657),
                    new(-0.6321303808537007, -0.9142185655049382),
                    new(-0.3363911425611019, -0.4865056910972188),
                    new(-0.52, 0),
                    new(-0.7036088574388981, 0.4865056910972188),
                    new(-1.223608857438898, 0.4865056910972188),
                    new(-1.223608857438898, -0.9142185655049381)
                ],
                ["P14-04"] =
                [
                    new(0.8157392382925988, -1.283138623223158),
                    new(-0.3363911425611019, -0.4865056910972188),
                    new(-0.6321303808537007, -0.9142185655049382),
                    new(-0.04065190426850318, -1.769644314320377),
                    new(0.4793480957314968, -1.769644314320377),
                    new(0.9993480957314969, -1.769644314320377)
                ],
                ["P14-05"] =
                [
                    new(-0.3363911425611019, -0.4865056910972188),
                    new(0.8157392382925988, -1.283138623223158),
                    new(1.111478476585197, -0.8554257488154386),
                    new(0.52, 0),
                    new(0, 0),
                    new(-0.52, 0)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Stein pentagonal tiling — Type 14", null,
            ["pentagonal tiling type 14", "Stein pentagon"],
            "Six congruent rigid convex pentagons in a two-dimensional periodic motif",
            "Exact algebraic reconstruction of Ed Pegg Jr. / mathgrrl Type 14 tiling data.",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-14",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
