using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Type 7: a periodic convex pentagon motif derived from published
    /// polygon-coordinate formulas. Rotations, edge pairing, and a primitive
    /// two-dimensional translation cover were independently verified.
    /// Mathematical data: Ed Pegg Jr., adapted by mathgrrl in
    /// pinkfish/openscad_boardgame_toolkit/pentagon_tilings.scad (Apache 2.0).
    /// Formula parameters: AA=150 degrees, b=0.
    /// </summary>
    internal static TilingNameEntry Type7()
    {
        const string symbol = "<80:2 7 6 10 14 16 18 20 21 24 26 29 30 32 31 36 38 42 43 40 46 48 49 52 53 55 57 59 62 64 67 68 70 72 74 73 76 77 78 80,3 5 9 12 13 15 17 19 23 25 28 30 26 31 34 37 39 41 40 44 45 47 51 49 54 55 58 60 61 65 66 68 70 73 71 75 74 78 79 80,4 6 8 11 14 15 18 19 22 24 27 29 30 33 35 36 37 40 42 44 46 45 50 52 56 53 57 58 63 64 67 69 71 72 73 74 76 77 79 80:5 5 5 5 5 5 5 5,3 3 4 3 3 4 3 4 4 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("P07-00",
                [
                    new(0, 0, "P07-06", new(0, 0), 1),
                    new(1, 1, "P07-04", new(0, 0), 1),
                    new(2, 2, "P07-02", new(0, 0), 0),
                    new(3, 3, "P07-01", new(0, 0), 0),
                    new(4, 4, "P07-04", new(0, 1), 4)
                ]),
                new PeriodicMotifCell("P07-01",
                [
                    new(0, 0, "P07-00", new(0, 0), 3),
                    new(1, 1, "P07-02", new(0, 0), 4),
                    new(2, 2, "P07-07", new(1, 0), 3),
                    new(3, 3, "P07-03", new(0, 1), 1),
                    new(4, 4, "P07-02", new(0, 1), 2)
                ]),
                new PeriodicMotifCell("P07-02",
                [
                    new(0, 0, "P07-00", new(0, 0), 2),
                    new(1, 1, "P07-04", new(0, 0), 0),
                    new(2, 2, "P07-01", new(0, -1), 4),
                    new(3, 3, "P07-03", new(0, 0), 0),
                    new(4, 4, "P07-01", new(0, 0), 1)
                ]),
                new PeriodicMotifCell("P07-03",
                [
                    new(0, 0, "P07-02", new(0, 0), 3),
                    new(1, 1, "P07-01", new(0, -1), 3),
                    new(2, 2, "P07-07", new(1, -1), 2),
                    new(3, 3, "P07-05", new(1, 0), 2),
                    new(4, 4, "P07-07", new(1, 0), 4)
                ]),
                new PeriodicMotifCell("P07-04",
                [
                    new(0, 0, "P07-02", new(0, 0), 1),
                    new(1, 1, "P07-00", new(0, 0), 1),
                    new(2, 2, "P07-06", new(0, 0), 0),
                    new(3, 3, "P07-05", new(0, 0), 0),
                    new(4, 4, "P07-00", new(0, -1), 4)
                ]),
                new PeriodicMotifCell("P07-05",
                [
                    new(0, 0, "P07-04", new(0, 0), 3),
                    new(1, 1, "P07-06", new(0, 0), 4),
                    new(2, 2, "P07-03", new(-1, 0), 3),
                    new(3, 3, "P07-07", new(0, -1), 1),
                    new(4, 4, "P07-06", new(0, -1), 2)
                ]),
                new PeriodicMotifCell("P07-06",
                [
                    new(0, 0, "P07-04", new(0, 0), 2),
                    new(1, 1, "P07-00", new(0, 0), 0),
                    new(2, 2, "P07-05", new(0, 1), 4),
                    new(3, 3, "P07-07", new(0, 0), 0),
                    new(4, 4, "P07-05", new(0, 0), 1)
                ]),
                new PeriodicMotifCell("P07-07",
                [
                    new(0, 0, "P07-06", new(0, 0), 3),
                    new(1, 1, "P07-05", new(0, 1), 3),
                    new(2, 2, "P07-03", new(-1, 1), 2),
                    new(3, 3, "P07-01", new(-1, 0), 2),
                    new(4, 4, "P07-03", new(-1, 0), 4)
                ])
            ],
            "OpenSCAD polygon formula, Type 7; independently reconstructed periodic incidence.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(-0.5952514300167683, -3.412011440134145), new(2.274674293422764, -0.3968342866778455),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["P07-00"] =
                [
                    new(1.289711431702997, 0.225),
                    new(0.3897114317029974, 0.225),
                    new(-0.3897114317029974, -0.225),
                    new(0.3897114317029974, -0.6749999999999999),
                    new(0.984962861719766, -0.6218342866778456)
                ],
                ["P07-01"] =
                [
                    new(0.984962861719766, -0.6218342866778456),
                    new(0.3897114317029974, -0.6749999999999999),
                    new(-0.2976257150083839, -1.256005720067072),
                    new(0.5492085716694611, -1.560754290050304),
                    new(1.434962861719766, -1.40125715008384)
                ],
                ["P07-02"] =
                [
                    new(0.3897114317029974, -0.6749999999999999),
                    new(-0.3897114317029974, -0.225),
                    new(-1.289711431702997, -0.225),
                    new(-0.8397114317029973, -1.004422863405995),
                    new(-0.2976257150083839, -1.256005720067072)
                ],
                ["P07-03"] =
                [
                    new(-0.2976257150083839, -1.256005720067072),
                    new(-0.8397114317029973, -1.004422863405995),
                    new(-1.725465721753302, -1.163920003372459),
                    new(-1.14446000168623, -1.851257150083841),
                    new(-0.2976257150083844, -2.156005720067073)
                ],
                ["P07-04"] =
                [
                    new(-1.289711431702997, -0.225),
                    new(-0.3897114317029974, -0.225),
                    new(0.3897114317029974, 0.225),
                    new(-0.3897114317029974, 0.6749999999999999),
                    new(-0.984962861719766, 0.6218342866778456)
                ],
                ["P07-05"] =
                [
                    new(-0.984962861719766, 0.6218342866778456),
                    new(-0.3897114317029974, 0.6749999999999999),
                    new(0.2976257150083839, 1.256005720067072),
                    new(-0.5492085716694621, 1.560754290050304),
                    new(-1.434962861719766, 1.40125715008384)
                ],
                ["P07-06"] =
                [
                    new(-0.3897114317029974, 0.6749999999999999),
                    new(0.3897114317029974, 0.225),
                    new(1.289711431702997, 0.225),
                    new(0.8397114317029973, 1.004422863405995),
                    new(0.2976257150083839, 1.256005720067072)
                ],
                ["P07-07"] =
                [
                    new(0.2976257150083839, 1.256005720067072),
                    new(0.8397114317029973, 1.004422863405995),
                    new(1.725465721753302, 1.163920003372459),
                    new(1.14446000168623, 1.851257150083841),
                    new(0.2976257150083844, 2.156005720067073)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Kershner pentagonal tiling — Type 7", null,
            ["convex pentagonal tiling type 7"],
            "8 congruent convex pentagons in a periodic motif",
            "Ed Pegg Jr., Wolfram Demonstrations; mathgrrl OpenSCAD formula (Apache 2.0).",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-7",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
