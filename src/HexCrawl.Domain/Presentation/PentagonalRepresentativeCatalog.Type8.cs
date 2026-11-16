using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Formula-derived 8-cell convex pentagon Type 8 fixture.
    /// Coordinates are evaluated, normalized and independently matched against
    /// the source's two-dimensional periodic lattice and all edge contacts.
    /// Mathematical formulas: Maxie D. Schmidt, WXML Tilings, based on
    /// Ed Pegg Jr's numbered pentagon tilings (2016):
    /// https://github.com/maxieds/WXMLTilingsHOWTO/blob/master/Pentagons.py
    /// Parameters AA=1.5707963267948966 radians; b=0. No custom engine behavior.
    /// </summary>
    internal static TilingNameEntry Type8()
    {
        const string symbol = "<80:2 7 6 10 14 16 18 20 21 24 26 29 31 33 36 38 40 43 46 49 50 52 55 58 59 61 63 65 66 67 64 69 71 72 73 70 75 77 78 80,3 5 9 12 13 15 17 19 23 25 28 30 26 32 35 39 42 45 48 47 43 53 54 57 56 62 64 58 66 68 63 70 72 74 69 76 73 78 79 80,4 6 8 11 14 15 18 19 22 24 27 29 31 34 37 38 41 44 47 49 51 52 56 58 60 61 54 65 57 67 68 71 73 74 69 75 72 77 79 80:5 5 5 5 5 5 5 5,3 3 4 4 4 4 3 3 3 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("W08-00",
                [
                    new(0, 0, "W08-01", new(0, 0), 3),
                    new(1, 1, "W08-03", new(0, -1), 1),
                    new(2, 2, "W08-06", new(0, 0), 4),
                    new(3, 3, "W08-02", new(0, 0), 3),
                    new(4, 4, "W08-04", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("W08-01",
                [
                    new(0, 0, "W08-07", new(0, 0), 4),
                    new(1, 1, "W08-02", new(0, -1), 1),
                    new(2, 2, "W08-03", new(0, -1), 2),
                    new(3, 3, "W08-00", new(0, 0), 0),
                    new(4, 4, "W08-05", new(-1, 0), 1)
                ]),
                new PeriodicMotifCell("W08-02",
                [
                    new(0, 0, "W08-03", new(0, 0), 3),
                    new(1, 1, "W08-01", new(0, 1), 1),
                    new(2, 2, "W08-04", new(0, 0), 4),
                    new(3, 3, "W08-00", new(0, 0), 3),
                    new(4, 4, "W08-06", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("W08-03",
                [
                    new(0, 0, "W08-05", new(0, 0), 4),
                    new(1, 1, "W08-00", new(0, 1), 1),
                    new(2, 2, "W08-01", new(0, 1), 2),
                    new(3, 3, "W08-02", new(0, 0), 0),
                    new(4, 4, "W08-07", new(1, 0), 1)
                ]),
                new PeriodicMotifCell("W08-04",
                [
                    new(0, 0, "W08-07", new(0, 1), 3),
                    new(1, 1, "W08-06", new(-1, 1), 1),
                    new(2, 2, "W08-05", new(-1, 0), 2),
                    new(3, 3, "W08-00", new(0, 0), 4),
                    new(4, 4, "W08-02", new(0, 0), 2)
                ]),
                new PeriodicMotifCell("W08-05",
                [
                    new(0, 0, "W08-07", new(1, 0), 0),
                    new(1, 1, "W08-01", new(1, 0), 4),
                    new(2, 2, "W08-04", new(1, 0), 2),
                    new(3, 3, "W08-06", new(0, 1), 0),
                    new(4, 4, "W08-03", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("W08-06",
                [
                    new(0, 0, "W08-05", new(0, -1), 3),
                    new(1, 1, "W08-04", new(1, -1), 1),
                    new(2, 2, "W08-07", new(1, 0), 2),
                    new(3, 3, "W08-02", new(0, 0), 4),
                    new(4, 4, "W08-00", new(0, 0), 2)
                ]),
                new PeriodicMotifCell("W08-07",
                [
                    new(0, 0, "W08-05", new(-1, 0), 0),
                    new(1, 1, "W08-03", new(-1, 0), 4),
                    new(2, 2, "W08-06", new(-1, 0), 2),
                    new(3, 3, "W08-04", new(0, -1), 0),
                    new(4, 4, "W08-01", new(0, 0), 0)
                ])
            ],
            "WXML Pentagon8 mathematical formula, independently derived periodic witness.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(4.264557334297549, 2.669692724167661), new(0.9251718859622269, 3.541953143270379),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["W08-00"] =
                [
                    new(-1.207106781186547, -0.7071067811865475),
                    new(-0.4625859429811134, -1.270976571635189),
                    new(0.4999999999999999, -1),
                    new(0.5, 0),
                    new(-0.5, 0)
                ],
                ["W08-01"] =
                [
                    new(-1.696148457597416, -1.579367200289264),
                    new(-1.425171885962227, -2.541953143270378),
                    new(-0.4625859429811136, -2.270976571635189),
                    new(-0.4625859429811134, -1.270976571635189),
                    new(-1.207106781186547, -0.7071067811865475)
                ],
                ["W08-02"] =
                [
                    new(1.207106781186547, 0.7071067811865475),
                    new(0.4625859429811134, 1.270976571635189),
                    new(-0.4999999999999999, 1),
                    new(-0.5, 0),
                    new(0.5, 0)
                ],
                ["W08-03"] =
                [
                    new(1.696148457597416, 1.579367200289265),
                    new(1.425171885962227, 2.541953143270378),
                    new(0.4625859429811136, 2.270976571635189),
                    new(0.4625859429811134, 1.270976571635189),
                    new(1.207106781186547, 0.7071067811865475)
                ],
                ["W08-04"] =
                [
                    new(-0.4999999999999999, 1),
                    new(-1.425171885962227, 0.872260419102717),
                    new(-1.914213562373095, 0),
                    new(-1.207106781186547, -0.7071067811865475),
                    new(-0.5, 0)
                ],
                ["W08-05"] =
                [
                    new(1.696148457597416, 1.579367200289265),
                    new(2.568408876700133, 1.090325523878396),
                    new(3.057450553111001, 1.962585942981113),
                    new(2.350343771924454, 2.669692724167661),
                    new(1.425171885962227, 2.541953143270378)
                ],
                ["W08-06"] =
                [
                    new(0.4999999999999999, -1),
                    new(1.425171885962227, -0.872260419102717),
                    new(1.914213562373095, 0),
                    new(1.207106781186547, 0.7071067811865475),
                    new(0.5, 0)
                ],
                ["W08-07"] =
                [
                    new(-1.696148457597416, -1.579367200289265),
                    new(-2.568408876700133, -1.090325523878396),
                    new(-3.057450553111001, -1.962585942981113),
                    new(-2.350343771924454, -2.669692724167661),
                    new(-1.425171885962227, -2.541953143270378)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Kershner Type 8 pentagonal tiling", null,
            ["convex pentagonal tiling type 8"],
            "Verified periodic convex monohedral pentagon example with 8 cells per lattice motif",
            "Maxie D. Schmidt, WXML Tilings formula-based example; Ed Pegg Jr, Wolfram Demonstrations.",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-8",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
