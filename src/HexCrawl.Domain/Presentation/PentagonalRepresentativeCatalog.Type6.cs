using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Type 6: a periodic convex pentagon motif derived from published
    /// polygon-coordinate formulas. Rotations, edge pairing, and a primitive
    /// two-dimensional translation cover were independently verified.
    /// Mathematical data: Ed Pegg Jr., adapted by mathgrrl in
    /// pinkfish/openscad_boardgame_toolkit/pentagon_tilings.scad (Apache 2.0).
    /// Formula parameters: AA=120 degrees, b=0.
    /// </summary>
    internal static TilingNameEntry Type6()
    {
        const string symbol = "<40:2 7 6 10 14 16 18 20 21 24 26 29 30 32 31 36 38 39 37 40,3 5 9 12 13 15 17 19 23 25 28 30 26 31 34 37 39 36 40 38,4 6 8 11 14 15 18 19 22 24 27 29 30 33 35 36 37 40 39 38:5 5 5 5,3 3 4 3 3 4>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("P06-00",
                [
                    new(0, 0, "P06-03", new(0, 0), 3),
                    new(1, 1, "P06-02", new(1, 0), 1),
                    new(2, 2, "P06-03", new(0, 1), 1),
                    new(3, 3, "P06-01", new(0, 0), 4),
                    new(4, 4, "P06-02", new(0, 0), 4)
                ]),
                new PeriodicMotifCell("P06-01",
                [
                    new(0, 0, "P06-03", new(0, 1), 0),
                    new(1, 1, "P06-02", new(0, 1), 2),
                    new(2, 2, "P06-03", new(-1, 1), 2),
                    new(3, 3, "P06-02", new(0, 0), 0),
                    new(4, 4, "P06-00", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("P06-02",
                [
                    new(0, 0, "P06-01", new(0, 0), 3),
                    new(1, 1, "P06-00", new(-1, 0), 1),
                    new(2, 2, "P06-01", new(0, -1), 1),
                    new(3, 3, "P06-03", new(0, 0), 4),
                    new(4, 4, "P06-00", new(0, 0), 4)
                ]),
                new PeriodicMotifCell("P06-03",
                [
                    new(0, 0, "P06-01", new(0, -1), 0),
                    new(1, 1, "P06-00", new(0, -1), 2),
                    new(2, 2, "P06-01", new(1, -1), 2),
                    new(3, 3, "P06-00", new(0, 0), 0),
                    new(4, 4, "P06-02", new(0, 0), 3)
                ])
            ],
            "OpenSCAD polygon formula, Type 6; independently reconstructed periodic incidence.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(0.5499999999999997, 1.905255888325765), new(-1.65, 0.9526279441628824),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["P06-00"] =
                [
                    new(0.275, 0),
                    new(0.5499999999999999, 0.4763139720814413),
                    new(0, 1.428941916244324),
                    new(-0.55, 0.4763139720814413),
                    new(-0.275, 0)
                ],
                ["P06-01"] =
                [
                    new(-0.55, 0.4763139720814413),
                    new(-1.1, 0.4763139720814412),
                    new(-1.65, -0.4763139720814414),
                    new(-0.5499999999999999, -0.4763139720814413),
                    new(-0.275, 0)
                ],
                ["P06-02"] =
                [
                    new(-0.275, 0),
                    new(-0.5499999999999999, -0.4763139720814413),
                    new(0, -1.428941916244324),
                    new(0.55, -0.4763139720814413),
                    new(0.275, 0)
                ],
                ["P06-03"] =
                [
                    new(0.55, -0.4763139720814413),
                    new(1.1, -0.4763139720814412),
                    new(1.65, 0.4763139720814414),
                    new(0.5499999999999999, 0.4763139720814413),
                    new(0.275, 0)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Kershner pentagonal tiling — Type 6", null,
            ["convex pentagonal tiling type 6"],
            "4 congruent convex pentagons in a periodic motif",
            "Ed Pegg Jr., Wolfram Demonstrations; mathgrrl OpenSCAD formula (Apache 2.0).",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-6",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
