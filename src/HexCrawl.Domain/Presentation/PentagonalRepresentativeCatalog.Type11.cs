using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Formula-derived 8-cell convex pentagon Type 11 fixture.
    /// Coordinates are evaluated, normalized and independently matched against
    /// the source's two-dimensional periodic lattice and all edge contacts.
    /// Mathematical formulas: Maxie D. Schmidt, WXML Tilings, based on
    /// Ed Pegg Jr's numbered pentagon tilings (2016):
    /// https://github.com/maxieds/WXMLTilingsHOWTO/blob/master/Pentagons.py
    /// Parameters AA=2.113 radians; b=0. No custom engine behavior.
    /// </summary>
    internal static TilingNameEntry Type11()
    {
        const string symbol = "<96:2 7 6 10 14 16 18 20 21 24 26 29 31 33 35 37 39 41 44 46 48 50 52 54 55 57 60 61 63 66 67 70 71 74 77 78 82 83 84 81 87 88 89 86 91 93 94 96,3 5 9 12 13 15 17 19 23 25 28 30 26 32 34 38 40 43 45 41 47 49 51 53 56 59 55 62 65 61 69 68 73 76 75 80 81 77 83 85 86 88 90 92 89 94 95 96,4 6 8 11 14 15 18 19 22 24 27 29 31 32 36 37 38 42 44 47 48 50 53 54 49 58 60 64 66 61 68 70 72 75 77 79 82 76 84 85 87 89 90 91 88 93 95 96:5 5 7 7 7 7 5 5,3 3 3 3 3 3 3 3 3 3 3 3 3 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("W11-00",
                [
                    new(0, 0, "W11-01", new(0, 0), 5),
                    new(1, 1, "W11-03", new(0, -1), 3),
                    new(2, 2, "W11-06", new(1, 0), 5),
                    new(3, 3, "W11-02", new(0, 0), 3),
                    new(4, 4, "W11-04", new(0, 0), 4)
                ]),
                new PeriodicMotifCell("W11-01",
                [
                    new(0, 0, "W11-07", new(0, 0), 1),
                    new(1, 1, "W11-05", new(0, -1), 4),
                    new(2, 1, "W11-04", new(0, -1), 6),
                    new(3, 1, "W11-02", new(0, -1), 1),
                    new(4, 2, "W11-03", new(0, -1), 4),
                    new(5, 3, "W11-00", new(0, 0), 0),
                    new(6, 4, "W11-04", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("W11-02",
                [
                    new(0, 0, "W11-03", new(0, 0), 5),
                    new(1, 1, "W11-01", new(0, 1), 3),
                    new(2, 2, "W11-04", new(0, 0), 5),
                    new(3, 3, "W11-00", new(0, 0), 3),
                    new(4, 4, "W11-06", new(1, 0), 4)
                ]),
                new PeriodicMotifCell("W11-03",
                [
                    new(0, 0, "W11-05", new(1, 0), 1),
                    new(1, 1, "W11-07", new(1, 1), 4),
                    new(2, 1, "W11-06", new(1, 1), 6),
                    new(3, 1, "W11-00", new(0, 1), 1),
                    new(4, 2, "W11-01", new(0, 1), 4),
                    new(5, 3, "W11-02", new(0, 0), 0),
                    new(6, 4, "W11-06", new(1, 0), 3)
                ]),
                new PeriodicMotifCell("W11-04",
                [
                    new(0, 0, "W11-05", new(0, 0), 3),
                    new(1, 1, "W11-06", new(0, 0), 1),
                    new(2, 2, "W11-07", new(0, 0), 2),
                    new(3, 2, "W11-01", new(0, 0), 6),
                    new(4, 2, "W11-00", new(0, 0), 4),
                    new(5, 3, "W11-02", new(0, 0), 2),
                    new(6, 4, "W11-01", new(0, 1), 2)
                ]),
                new PeriodicMotifCell("W11-05",
                [
                    new(0, 0, "W11-07", new(0, 1), 0),
                    new(1, 1, "W11-03", new(-1, 0), 0),
                    new(2, 2, "W11-06", new(0, 0), 2),
                    new(3, 3, "W11-04", new(0, 0), 0),
                    new(4, 4, "W11-01", new(0, 1), 1)
                ]),
                new PeriodicMotifCell("W11-06",
                [
                    new(0, 0, "W11-07", new(0, 0), 3),
                    new(1, 1, "W11-04", new(0, 0), 1),
                    new(2, 2, "W11-05", new(0, 0), 2),
                    new(3, 2, "W11-03", new(-1, 0), 6),
                    new(4, 2, "W11-02", new(-1, 0), 4),
                    new(5, 3, "W11-00", new(-1, 0), 2),
                    new(6, 4, "W11-03", new(-1, -1), 2)
                ]),
                new PeriodicMotifCell("W11-07",
                [
                    new(0, 0, "W11-05", new(0, -1), 0),
                    new(1, 1, "W11-01", new(0, 0), 0),
                    new(2, 2, "W11-04", new(0, 0), 2),
                    new(3, 3, "W11-06", new(0, 0), 0),
                    new(4, 4, "W11-03", new(-1, -1), 1)
                ])
            ],
            "WXML Pentagon11 mathematical formula, independently derived periodic witness.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(1.276792584431506, 2.25975825194841), new(-2.216604843697037, 1.252410351685683),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["W11-00"] =
                [
                    new(0, 0),
                    new(1.360031231715941, 0),
                    new(1.740600153644651, 0.2292657847931804),
                    new(0.884026541663555, 0.7452906361885934),
                    new(0, 0.2778539307039097)
                ],
                ["W11-01"] =
                [
                    new(0, -0.2778539307039097),
                    new(0.884026541663555, -0.7452906361885934),
                    new(1.122028886689748, -0.6019111028874771),
                    new(1.360031231715941, -0.4585315695863608),
                    new(1.740600153644651, -0.2292657847931804),
                    new(1.360031231715941, 0),
                    new(0, 0)
                ],
                ["W11-02"] =
                [
                    new(0.8840265416635558, 1.023144566892503),
                    new(-0.476004690052386, 1.023144566892503),
                    new(-0.856573611981096, 0.7938787820993225),
                    new(0, 0.2778539307039094),
                    new(0.8840265416635558, 0.7452906361885931)
                ],
                ["W11-03"] =
                [
                    new(0.8840265416635558, 1.300998497596413),
                    new(0, 1.768435203081096),
                    new(-0.2380023450261926, 1.62505566977998),
                    new(-0.476004690052386, 1.481676136478864),
                    new(-0.856573611981096, 1.252410351685683),
                    new(-0.476004690052386, 1.023144566892503),
                    new(0.8840265416635558, 1.023144566892503)
                ],
                ["W11-04"] =
                [
                    new(-1.094575957007289, 0.6504992487982062),
                    new(-0.3927660427679496, -0.5144676157598169),
                    new(0, -0.7221460692960906),
                    new(0, -0.2778539307039097),
                    new(0, 0),
                    new(0, 0.2778539307039094),
                    new(-0.856573611981096, 0.7938787820993225)
                ],
                ["W11-05"] =
                [
                    new(-1.332578302033482, 0.5071197154970899),
                    new(-1.276792584431504, -0.4913230488673141),
                    new(-0.3927660427679496, -0.9587597543519977),
                    new(-0.3927660427679496, -0.5144676157598169),
                    new(-1.094575957007289, 0.6504992487982062)
                ],
                ["W11-06"] =
                [
                    new(0.7018099142393392, -1.887112933854114),
                    new(0, -0.7221460692960902),
                    new(-0.3927660427679506, -0.5144676157598169),
                    new(-0.3927660427679498, -0.9587597543519977),
                    new(-0.3927660427679498, -1.236613685055907),
                    new(-0.3927660427679506, -1.514467615759817),
                    new(0.4638075692131458, -2.03049246715523)
                ],
                ["W11-07"] =
                [
                    new(0.9398122592655322, -1.743733400552997),
                    new(0.8840265416635551, -0.7452906361885934),
                    new(0, -0.2778539307039096),
                    new(0, -0.7221460692960902),
                    new(0.7018099142393392, -1.887112933854114)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Rice Type 11 pentagonal tiling", null,
            ["convex pentagonal tiling type 11"],
            "Verified periodic convex monohedral pentagon example with 8 cells per lattice motif",
            "Maxie D. Schmidt, WXML Tilings formula-based example; Ed Pegg Jr, Wolfram Demonstrations.",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-11",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
