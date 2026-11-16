using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Formula-derived 6-cell convex pentagon Type 5 fixture.
    /// Coordinates are evaluated, normalized and independently matched against
    /// the source's two-dimensional periodic lattice and all edge contacts.
    /// Mathematical formulas: Maxie D. Schmidt, WXML Tilings, based on
    /// Ed Pegg Jr's numbered pentagon tilings (2016):
    /// https://github.com/maxieds/WXMLTilingsHOWTO/blob/master/Pentagons.py
    /// Parameters AA=2.356 radians; b=0.5. No custom engine behavior.
    /// </summary>
    internal static TilingNameEntry Type5()
    {
        const string symbol = "<60:2 7 6 10 14 16 18 20 21 24 26 29 31 33 35 37 39 41 43 45 47 49 51 53 52 48 57 58 56 60,3 5 9 12 13 15 17 19 23 25 28 30 26 32 34 38 40 42 44 41 46 48 50 54 52 55 56 57 59 60,4 6 8 11 14 15 18 19 22 24 27 29 31 32 36 37 38 42 43 46 47 49 52 53 51 48 58 57 59 60:5 5 5 5 5 5,3 3 3 3 3 6 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("W05-00",
                [
                    new(0, 0, "W05-05", new(0, 0), 4),
                    new(1, 1, "W05-02", new(1, 0), 2),
                    new(2, 2, "W05-04", new(1, 1), 1),
                    new(3, 3, "W05-03", new(1, 1), 3),
                    new(4, 4, "W05-01", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("W05-01",
                [
                    new(0, 0, "W05-00", new(0, 0), 4),
                    new(1, 1, "W05-03", new(1, 1), 2),
                    new(2, 2, "W05-05", new(0, 1), 1),
                    new(3, 3, "W05-04", new(0, 1), 3),
                    new(4, 4, "W05-02", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("W05-02",
                [
                    new(0, 0, "W05-01", new(0, 0), 4),
                    new(1, 1, "W05-04", new(0, 1), 2),
                    new(2, 2, "W05-00", new(-1, 0), 1),
                    new(3, 3, "W05-05", new(-1, 0), 3),
                    new(4, 4, "W05-03", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("W05-03",
                [
                    new(0, 0, "W05-02", new(0, 0), 4),
                    new(1, 1, "W05-05", new(-1, 0), 2),
                    new(2, 2, "W05-01", new(-1, -1), 1),
                    new(3, 3, "W05-00", new(-1, -1), 3),
                    new(4, 4, "W05-04", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("W05-04",
                [
                    new(0, 0, "W05-03", new(0, 0), 4),
                    new(1, 1, "W05-00", new(-1, -1), 2),
                    new(2, 2, "W05-02", new(0, -1), 1),
                    new(3, 3, "W05-01", new(0, -1), 3),
                    new(4, 4, "W05-05", new(0, 0), 0)
                ]),
                new PeriodicMotifCell("W05-05",
                [
                    new(0, 0, "W05-04", new(0, 0), 4),
                    new(1, 1, "W05-01", new(0, -1), 2),
                    new(2, 2, "W05-03", new(1, 0), 1),
                    new(3, 3, "W05-02", new(1, 0), 3),
                    new(4, 4, "W05-00", new(0, 0), 0)
                ])
            ],
            "WXML Pentagon5 mathematical formula, independently derived periodic witness.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(2.336472694132859, -0.6417188457659189), new(-0.6124915245459158, 2.344304131250684),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["W05-00"] =
                [
                    new(0, 0),
                    new(1, 0),
                    new(1.353484621239935, 0.3536221465729488),
                    new(1.223981169586944, 0.8365598817003268),
                    new(0.5, 0.8660254037844386)
                ],
                ["W05-01"] =
                [
                    new(0, 0),
                    new(0.5, 0.8660254037844386),
                    new(0.3704965483470093, 1.348963138911817),
                    new(-0.1124915245459158, 1.478278727466246),
                    new(-0.5, 0.8660254037844386)
                ],
                ["W05-02"] =
                [
                    new(0, 0),
                    new(-0.5, 0.8660254037844386),
                    new(-0.9829880728929252, 0.9953409923388677),
                    new(-1.33647269413286, 0.6417188457659189),
                    new(-1, 0)
                ],
                ["W05-03"] =
                [
                    new(0, 0),
                    new(-1, 0),
                    new(-1.353484621239935, -0.3536221465729488),
                    new(-1.223981169586944, -0.8365598817003268),
                    new(-0.5, -0.8660254037844386)
                ],
                ["W05-04"] =
                [
                    new(0, 0),
                    new(-0.5, -0.8660254037844386),
                    new(-0.3704965483470093, -1.348963138911817),
                    new(0.1124915245459158, -1.478278727466246),
                    new(0.5, -0.8660254037844386)
                ],
                ["W05-05"] =
                [
                    new(0, 0),
                    new(0.5, -0.8660254037844386),
                    new(0.9829880728929252, -0.9953409923388677),
                    new(1.33647269413286, -0.6417188457659189),
                    new(1, 0)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Reinhardt Type 5 pentagonal tiling", null,
            ["convex pentagonal tiling type 5"],
            "Verified periodic convex monohedral pentagon example with 6 cells per lattice motif",
            "Maxie D. Schmidt, WXML Tilings formula-based example; Ed Pegg Jr, Wolfram Demonstrations.",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-5",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
