using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Type 13: a periodic convex pentagon motif derived from published
    /// polygon-coordinate formulas. Rotations, edge pairing, and a primitive
    /// two-dimensional translation cover were independently verified.
    /// Mathematical data: Ed Pegg Jr., adapted by mathgrrl in
    /// pinkfish/openscad_boardgame_toolkit/pentagon_tilings.scad (Apache 2.0).
    /// Formula parameters: AA=135 degrees, b=0.
    /// </summary>
    internal static TilingNameEntry Type13()
    {
        const string symbol = "<88:2 7 6 10 14 16 18 20 21 24 26 29 30 32 34 36 38 40 42 44 46 48 49 52 53 55 58 57 62 64 65 68 70 73 75 76 78 80 79 83 82 85 86 88,3 5 9 12 13 15 17 19 23 25 28 30 26 31 33 37 39 41 40 43 45 47 51 50 54 55 57 60 61 63 66 67 72 71 74 75 77 79 82 81 84 86 87 88,4 6 8 11 14 15 18 19 22 24 27 29 30 31 35 36 37 40 43 44 46 45 50 52 56 53 58 59 63 64 65 69 71 73 74 77 78 80 81 83 82 85 87 88:5 6 6 5 6 5 6 5,3 3 3 3 3 4 3 3 4 3 3 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("P13-00",
                [
                    new(0, 0, "P13-03", new(0, -1), 0),
                    new(1, 1, "P13-02", new(0, 0), 4),
                    new(2, 2, "P13-04", new(0, 0), 2),
                    new(3, 3, "P13-02", new(0, -1), 0),
                    new(4, 4, "P13-01", new(0, -1), 5)
                ]),
                new PeriodicMotifCell("P13-01",
                [
                    new(0, 0, "P13-03", new(0, -1), 3),
                    new(1, 1, "P13-07", new(0, -1), 1),
                    new(2, 2, "P13-05", new(1, 0), 1),
                    new(3, 3, "P13-07", new(0, 0), 3),
                    new(4, 3, "P13-03", new(0, 0), 1),
                    new(5, 4, "P13-00", new(0, 1), 4)
                ]),
                new PeriodicMotifCell("P13-02",
                [
                    new(0, 0, "P13-00", new(0, 1), 3),
                    new(1, 1, "P13-04", new(0, 1), 1),
                    new(2, 2, "P13-06", new(0, 0), 1),
                    new(3, 3, "P13-04", new(0, 0), 3),
                    new(4, 3, "P13-00", new(0, 0), 1),
                    new(5, 4, "P13-03", new(0, -1), 4)
                ]),
                new PeriodicMotifCell("P13-03",
                [
                    new(0, 0, "P13-00", new(0, 1), 0),
                    new(1, 1, "P13-01", new(0, 0), 4),
                    new(2, 2, "P13-07", new(0, 0), 2),
                    new(3, 3, "P13-01", new(0, 1), 0),
                    new(4, 4, "P13-02", new(0, 1), 5)
                ]),
                new PeriodicMotifCell("P13-04",
                [
                    new(0, 0, "P13-06", new(0, -1), 2),
                    new(1, 0, "P13-02", new(0, -1), 1),
                    new(2, 1, "P13-00", new(0, 0), 2),
                    new(3, 2, "P13-02", new(0, 0), 3),
                    new(4, 3, "P13-06", new(0, 0), 0),
                    new(5, 4, "P13-05", new(0, -1), 4)
                ]),
                new PeriodicMotifCell("P13-05",
                [
                    new(0, 0, "P13-07", new(-1, 0), 4),
                    new(1, 1, "P13-01", new(-1, 0), 2),
                    new(2, 2, "P13-07", new(-1, -1), 0),
                    new(3, 3, "P13-06", new(0, 0), 3),
                    new(4, 4, "P13-04", new(0, 1), 5)
                ]),
                new PeriodicMotifCell("P13-06",
                [
                    new(0, 0, "P13-04", new(0, 0), 4),
                    new(1, 1, "P13-02", new(0, 0), 2),
                    new(2, 2, "P13-04", new(0, 1), 0),
                    new(3, 3, "P13-05", new(0, 0), 3),
                    new(4, 4, "P13-07", new(-1, -1), 5)
                ]),
                new PeriodicMotifCell("P13-07",
                [
                    new(0, 0, "P13-05", new(1, 1), 2),
                    new(1, 0, "P13-01", new(0, 1), 1),
                    new(2, 1, "P13-03", new(0, 0), 2),
                    new(3, 2, "P13-01", new(0, 0), 3),
                    new(4, 3, "P13-05", new(1, 0), 0),
                    new(5, 4, "P13-06", new(1, 1), 4)
                ])
            ],
            "OpenSCAD polygon formula, Type 13; independently reconstructed periodic incidence.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(-5.069848480983499, -2.099999999999999), new(0.6999999999999998, -1.689949493661166),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["P13-00"] =
                [
                    new(-1.689949493661166, 0.9899494936611666),
                    new(-0.7, 0),
                    new(0, 0),
                    new(0, 0.7),
                    new(-1.544974746830583, 1.339949493661166)
                ],
                ["P13-01"] =
                [
                    new(-0.8449747468305829, -0.3499999999999999),
                    new(-2.389949493661166, 0.2899494936611668),
                    new(-2.884924240491749, -0.2050252531694163),
                    new(-2.389949493661167, -0.6999999999999997),
                    new(-1.689949493661166, -0.6999999999999997),
                    new(-0.9899494936611665, -0.6999999999999997)
                ],
                ["P13-02"] =
                [
                    new(-0.8449747468305829, -0.3499999999999999),
                    new(0.6999999999999998, -0.9899494936611664),
                    new(1.194974746830583, -0.4949747468305832),
                    new(0.7, 0),
                    new(0, 0),
                    new(-0.7000000000000001, 0)
                ],
                ["P13-03"] =
                [
                    new(0, -1.689949493661166),
                    new(-0.9899494936611661, -0.6999999999999997),
                    new(-1.689949493661166, -0.6999999999999997),
                    new(-1.689949493661166, -1.399999999999999),
                    new(-0.1449747468305831, -2.039949493661166)
                ],
                ["P13-04"] =
                [
                    new(0.9899494936611666, 1.689949493661166),
                    new(0.4949747468305833, 1.194974746830583),
                    new(0, 0.7),
                    new(0, 0),
                    new(0.7, 0),
                    new(1.339949493661166, 1.544974746830583)
                ],
                ["P13-05"] =
                [
                    new(2.039949493661166, -0.1449747468305835),
                    new(2.679898987322333, 1.399999999999999),
                    new(2.18492424049175, 1.894974746830583),
                    new(1.689949493661167, 1.4),
                    new(1.689949493661167, 0)
                ],
                ["P13-06"] =
                [
                    new(1.339949493661166, 1.544974746830583),
                    new(0.7, 0),
                    new(1.194974746830583, -0.4949747468305833),
                    new(1.689949493661166, 0),
                    new(1.689949493661166, 1.4)
                ],
                ["P13-07"] =
                [
                    new(-2.679898987322332, -2.389949493661165),
                    new(-2.18492424049175, -1.894974746830583),
                    new(-1.689949493661166, -1.399999999999999),
                    new(-1.689949493661166, -0.6999999999999997),
                    new(-2.389949493661165, -0.6999999999999997),
                    new(-3.029898987322332, -2.244974746830582)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Rice pentagonal tiling — Type 13", null,
            ["convex pentagonal tiling type 13"],
            "8 congruent convex pentagons in a periodic motif",
            "Ed Pegg Jr., Wolfram Demonstrations; mathgrrl OpenSCAD formula (Apache 2.0).",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-13",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
