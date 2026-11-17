using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Type 9: a periodic convex pentagon motif derived from published
    /// polygon-coordinate formulas. Rotations, edge pairing, and a primitive
    /// two-dimensional translation cover were independently verified.
    /// Mathematical data: Ed Pegg Jr., adapted by mathgrrl in
    /// pinkfish/openscad_boardgame_toolkit/pentagon_tilings.scad (Apache 2.0).
    /// Formula parameters: AA=0 degrees, b=1.573.
    /// </summary>
    internal static TilingNameEntry Type9()
    {
        const string symbol = "<80:2 7 6 10 14 16 18 20 21 24 26 29 30 32 31 36 38 42 43 40 46 48 49 52 53 55 57 59 62 64 67 68 70 72 74 73 76 77 78 80,3 5 9 12 13 15 17 19 23 25 28 30 26 31 34 37 39 41 40 44 45 47 51 49 54 55 58 60 61 65 66 68 70 73 71 75 74 78 79 80,4 6 8 11 14 15 18 19 22 24 27 29 30 33 35 36 37 40 42 44 46 45 50 52 56 53 57 58 63 64 67 69 71 72 73 74 76 77 79 80:5 5 5 5 5 5 5 5,3 3 4 3 3 4 3 4 4 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("P09-00",
                [
                    new(0, 0, "P09-05", new(1, 0), 4),
                    new(1, 1, "P09-01", new(0, 0), 2),
                    new(2, 2, "P09-03", new(0, 1), 2),
                    new(3, 3, "P09-02", new(0, 0), 1),
                    new(4, 4, "P09-03", new(0, 0), 4)
                ]),
                new PeriodicMotifCell("P09-01",
                [
                    new(0, 0, "P09-05", new(1, 1), 0),
                    new(1, 1, "P09-03", new(0, 1), 3),
                    new(2, 2, "P09-00", new(0, 0), 1),
                    new(3, 3, "P09-05", new(1, 0), 3),
                    new(4, 4, "P09-04", new(1, 0), 3)
                ]),
                new PeriodicMotifCell("P09-02",
                [
                    new(0, 0, "P09-07", new(0, 0), 0),
                    new(1, 1, "P09-00", new(0, 0), 3),
                    new(2, 2, "P09-03", new(0, 1), 1),
                    new(3, 3, "P09-07", new(0, 1), 3),
                    new(4, 4, "P09-06", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("P09-03",
                [
                    new(0, 0, "P09-07", new(0, 0), 4),
                    new(1, 1, "P09-02", new(0, -1), 2),
                    new(2, 2, "P09-00", new(0, -1), 2),
                    new(3, 3, "P09-01", new(0, -1), 1),
                    new(4, 4, "P09-00", new(0, 0), 4)
                ]),
                new PeriodicMotifCell("P09-04",
                [
                    new(0, 0, "P09-07", new(0, 0), 2),
                    new(1, 1, "P09-06", new(0, 0), 1),
                    new(2, 2, "P09-05", new(0, 1), 1),
                    new(3, 3, "P09-01", new(-1, 0), 4),
                    new(4, 4, "P09-06", new(0, -1), 4)
                ]),
                new PeriodicMotifCell("P09-05",
                [
                    new(0, 0, "P09-01", new(-1, -1), 0),
                    new(1, 1, "P09-04", new(0, -1), 2),
                    new(2, 2, "P09-06", new(0, -1), 0),
                    new(3, 3, "P09-01", new(-1, 0), 3),
                    new(4, 4, "P09-00", new(-1, 0), 0)
                ]),
                new PeriodicMotifCell("P09-06",
                [
                    new(0, 0, "P09-05", new(0, 1), 2),
                    new(1, 1, "P09-04", new(0, 0), 1),
                    new(2, 2, "P09-07", new(0, 0), 1),
                    new(3, 3, "P09-02", new(0, 0), 4),
                    new(4, 4, "P09-04", new(0, 1), 4)
                ]),
                new PeriodicMotifCell("P09-07",
                [
                    new(0, 0, "P09-02", new(0, 0), 0),
                    new(1, 1, "P09-06", new(0, 0), 2),
                    new(2, 2, "P09-04", new(0, 0), 0),
                    new(3, 3, "P09-02", new(0, -1), 3),
                    new(4, 4, "P09-03", new(0, 0), 0)
                ])
            ],
            "OpenSCAD polygon formula, Type 9; independently reconstructed periodic incidence.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(0.3075908738973344, -3.909205885330692), new(1.684313164125098, -0.729188242777608),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["P09-00"] =
                [
                    new(-0.6917000528224015, -0.9814535596490995),
                    new(-0.1917000528224015, -1.588563310847349),
                    new(0.2463065556513481, -1.462430652411603),
                    new(0.7463065556513481, -0.8553209012133537),
                    new(0, -0.6071097511982492)
                ],
                ["P09-01"] =
                [
                    new(0.4926131113026961, -2.317751553624956),
                    new(0.9926131113026961, -1.710641802426707),
                    new(0.2463065556513481, -1.462430652411603),
                    new(-0.1917000528224015, -1.588563310847349),
                    new(-0.2922407634092544, -2.368610627049827)
                ],
                ["P09-02"] =
                [
                    new(0.5, 0),
                    new(0, -0.6071097511982492),
                    new(0.7463065556513481, -0.8553209012133537),
                    new(1.184313164125098, -0.729188242777608),
                    new(1.28485387471195, 0.05085907342487128)
                ],
                ["P09-03"] =
                [
                    new(0, -0.6071097511982486),
                    new(-0.5, 0),
                    new(-0.9380066084737496, -0.1261326584357457),
                    new(-1.43800660847375, -0.7332424096339947),
                    new(-0.6917000528224015, -0.9814535596490994)
                ],
                ["P09-04"] =
                [
                    new(-0.3994592894131482, 0.7800473162024772),
                    new(0.2922407634092533, 0.4057035077516282),
                    new(0.3927814739961072, 1.185750823954107),
                    new(0.1850222374053617, 1.591454331705736),
                    new(-0.5998316373065888, 1.540595258280865)
                ],
                ["P09-05"] =
                [
                    new(-0.9992909267197361, 2.927752325681593),
                    new(-1.499290926719736, 2.320642574483344),
                    new(-1.291531690128991, 1.914939066731715),
                    new(-0.5998316373065891, 1.540595258280864),
                    new(-0.4992909267197361, 2.320642574483344)
                ],
                ["P09-06"] =
                [
                    new(1.084481526818509, 0.8114070155032559),
                    new(0.3927814739961073, 1.185750823954108),
                    new(0.2922407634092544, 0.4057035077516279),
                    new(0.5, 0),
                    new(1.28485387471195, 0.05085907342487128)
                ],
                ["P09-07"] =
                [
                    new(0, -0.6071097511982492),
                    new(0.5, 0),
                    new(0.2922407634092544, 0.4057035077516279),
                    new(-0.3994592894131471, 0.7800473162024787),
                    new(-0.5, 0)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Rice pentagonal tiling — Type 9", null,
            ["convex pentagonal tiling type 9"],
            "8 congruent convex pentagons in a periodic motif",
            "Ed Pegg Jr., Wolfram Demonstrations; mathgrrl OpenSCAD formula (Apache 2.0).",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-9",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
