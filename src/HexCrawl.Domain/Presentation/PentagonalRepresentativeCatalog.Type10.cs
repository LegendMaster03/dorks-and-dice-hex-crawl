using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Formula-derived 6-cell convex pentagon Type 10 fixture.
    /// Coordinates are evaluated, normalized and independently matched against
    /// the source's two-dimensional periodic lattice and all edge contacts.
    /// Mathematical formulas: Maxie D. Schmidt, WXML Tilings, based on
    /// Ed Pegg Jr's numbered pentagon tilings (2016):
    /// https://github.com/maxieds/WXMLTilingsHOWTO/blob/master/Pentagons.py
    /// Parameters AA=1.4 radians; b=0. No custom engine behavior.
    /// </summary>
    internal static TilingNameEntry Type10()
    {
        const string symbol = "<68:2 7 6 10 14 16 18 20 21 24 26 29 30 33 35 38 40 42 43 45 48 49 52 54 56 58 55 61 63 65 64 67 62 68,3 5 9 12 13 15 17 19 23 25 28 30 31 32 34 37 39 41 44 47 46 51 53 49 55 56 60 62 64 65 67 66 63 68,4 6 8 11 14 15 18 19 22 24 27 29 30 32 36 38 39 41 43 46 48 50 52 54 57 58 59 61 62 66 67 64 63 68:7 7 5 5 5 5,3 3 3 3 3 3 3 3 3 4 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("W10-00",
                [
                    new(0, 0, "W10-05", new(1, 0), 5),
                    new(1, 1, "W10-04", new(0, 1), 3),
                    new(2, 2, "W10-05", new(0, 0), 1),
                    new(3, 3, "W10-01", new(0, 0), 4),
                    new(4, 4, "W10-03", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("W10-01",
                [
                    new(0, 0, "W10-05", new(0, 0), 0),
                    new(1, 1, "W10-03", new(-1, 0), 1),
                    new(2, 2, "W10-04", new(-1, 0), 6),
                    new(3, 3, "W10-02", new(0, 0), 4),
                    new(4, 4, "W10-00", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("W10-02",
                [
                    new(0, 0, "W10-04", new(-1, 0), 5),
                    new(1, 1, "W10-05", new(0, -1), 3),
                    new(2, 2, "W10-04", new(0, 0), 1),
                    new(3, 3, "W10-03", new(0, 0), 4),
                    new(4, 4, "W10-01", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("W10-03",
                [
                    new(0, 0, "W10-04", new(0, 0), 0),
                    new(1, 1, "W10-01", new(1, 0), 1),
                    new(2, 2, "W10-05", new(1, 0), 6),
                    new(3, 3, "W10-00", new(0, 0), 4),
                    new(4, 4, "W10-02", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("W10-04",
                [
                    new(0, 0, "W10-03", new(0, 0), 0),
                    new(1, 0, "W10-02", new(0, 0), 2),
                    new(2, 1, "W10-05", new(0, -1), 2),
                    new(3, 2, "W10-00", new(0, -1), 1),
                    new(4, 3, "W10-05", new(1, -1), 4),
                    new(5, 4, "W10-02", new(1, 0), 0),
                    new(6, 4, "W10-01", new(1, 0), 2)
                ]),
                new PeriodicMotifCell("W10-05",
                [
                    new(0, 0, "W10-01", new(0, 0), 0),
                    new(1, 0, "W10-00", new(0, 0), 2),
                    new(2, 1, "W10-04", new(0, 1), 2),
                    new(3, 2, "W10-02", new(0, 1), 1),
                    new(4, 3, "W10-04", new(-1, 1), 4),
                    new(5, 4, "W10-00", new(-1, 0), 0),
                    new(6, 4, "W10-03", new(-1, 0), 2)
                ])
            ],
            "WXML Pentagon10 mathematical formula, independently derived periodic witness.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(1.259374728193121, -1.682424191760719), new(1.238532050655818, 2.414791601081823),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["W10-00"] =
                [
                    new(1, 0),
                    new(1.063158166432811, 0.3661837046605513),
                    new(0.6192660253279086, 0.8931910235325698),
                    new(0, 1),
                    new(0, 0)
                ],
                ["W10-01"] =
                [
                    new(0, 1),
                    new(-0.3661837046605513, 1.063158166432811),
                    new(-0.8931910235325698, 0.6192660253279086),
                    new(-1, 0),
                    new(0, 0)
                ],
                ["W10-02"] =
                [
                    new(-1, 0),
                    new(-1.063158166432811, -0.3661837046605513),
                    new(-0.6192660253279086, -0.8931910235325698),
                    new(0, -1),
                    new(0, 0)
                ],
                ["W10-03"] =
                [
                    new(0, -1),
                    new(0.3661837046605513, -1.063158166432811),
                    new(0.8931910235325698, -0.6192660253279086),
                    new(1, 0),
                    new(0, 0)
                ],
                ["W10-04"] =
                [
                    new(0.3661837046605512, -1.063158166432811),
                    new(0, -1),
                    new(-0.619266025327909, -0.8931910235325702),
                    new(-0.6192660253279089, -1.521600577549253),
                    new(-0.1753738842230068, -2.048607896421272),
                    new(0.1962165617603104, -2.048607896421272),
                    new(0.2593747281931209, -1.682424191760719)
                ],
                ["W10-05"] =
                [
                    new(-0.3661837046605512, 1.063158166432811),
                    new(0, 1),
                    new(0.619266025327909, 0.8931910235325702),
                    new(0.6192660253279089, 1.521600577549253),
                    new(0.1753738842230068, 2.048607896421272),
                    new(-0.1962165617603104, 2.048607896421272),
                    new(-0.2593747281931209, 1.682424191760719)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "James Type 10 pentagonal tiling", null,
            ["convex pentagonal tiling type 10"],
            "Verified periodic convex monohedral pentagon example with 6 cells per lattice motif",
            "Maxie D. Schmidt, WXML Tilings formula-based example; Ed Pegg Jr, Wolfram Demonstrations.",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-10",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
