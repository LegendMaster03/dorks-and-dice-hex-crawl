using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

/// <summary>
/// Explicit mathematical example data. A pentagon is never a runtime mode:
/// these witnesses enter the same generic periodic topology and metric
/// admission paths as every other catalog polygon.
/// </summary>
internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// The square-period Cairo pattern. Eight congruent convex pentagons form
    /// an ordinary translational motif; their two nonadjacent right angles and
    /// the equal pairs of sides meeting there meet the Type 4 conditions.
    /// This symmetric Cairo example also belongs to Type 2, illustrating
    /// that the mathematical families overlap.
    /// Reference: https://doi.org/10.1038/s41598-025-17010-4
    /// </summary>
    internal static TilingNameEntry CairoType4()
    {
        const string symbol = "<80:2 7 6 10 14 16 18 20 21 24 26 29 31 33 36 38 40 43 46 49 50 52 55 58 59 61 63 65 66 67 64 69 71 72 73 70 75 77 78 80,3 5 9 12 13 15 17 19 23 25 28 30 26 32 35 39 42 45 48 47 43 53 54 57 56 62 64 58 66 68 63 70 72 74 69 76 73 78 79 80,4 6 8 11 14 15 18 19 22 24 27 29 31 34 37 38 41 44 47 49 51 52 56 58 60 61 54 65 57 67 68 71 73 74 69 75 72 77 79 80:5 5 5 5 5 5 5 5,3 3 4 4 4 4 3 3 3 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
            new PeriodicMotifCell("H00",
            [
                new(0, 0, "V01", new(0, -1), 2),
                new(1, 1, "V11", new(0, -1), 2),
                new(2, 2, "V10", new(0, 0), 4),
                new(3, 3, "H01", new(0, 0), 1),
                new(4, 4, "V00", new(0, 0), 0)
            ]),
            new PeriodicMotifCell("H01",
            [
                new(0, 0, "V00", new(0, 0), 1),
                new(1, 1, "H00", new(0, 0), 3),
                new(2, 2, "V10", new(0, 0), 3),
                new(3, 3, "V11", new(0, 0), 4),
                new(4, 4, "V01", new(0, 0), 0)
            ]),
            new PeriodicMotifCell("H10",
            [
                new(0, 0, "V11", new(0, -1), 1),
                new(1, 1, "H11", new(0, -1), 3),
                new(2, 2, "V01", new(1, -1), 3),
                new(3, 3, "V00", new(1, 0), 4),
                new(4, 4, "V10", new(0, 0), 0)
            ]),
            new PeriodicMotifCell("H11",
            [
                new(0, 0, "V10", new(0, 0), 2),
                new(1, 1, "V00", new(1, 0), 2),
                new(2, 2, "V01", new(1, 0), 4),
                new(3, 3, "H10", new(0, 1), 1),
                new(4, 4, "V11", new(0, 0), 0)
            ]),
            new PeriodicMotifCell("V00",
            [
                new(0, 0, "H00", new(0, 0), 4),
                new(1, 1, "H01", new(0, 0), 0),
                new(2, 2, "H11", new(-1, 0), 1),
                new(3, 3, "V10", new(-1, 0), 1),
                new(4, 4, "H10", new(-1, 0), 3)
            ]),
            new PeriodicMotifCell("V01",
            [
                new(0, 0, "H01", new(0, 0), 4),
                new(1, 1, "V11", new(0, 0), 3),
                new(2, 2, "H00", new(0, 1), 0),
                new(3, 3, "H10", new(-1, 1), 2),
                new(4, 4, "H11", new(-1, 0), 2)
            ]),
            new PeriodicMotifCell("V10",
            [
                new(0, 0, "H10", new(0, 0), 4),
                new(1, 1, "V00", new(1, 0), 3),
                new(2, 2, "H11", new(0, 0), 0),
                new(3, 3, "H01", new(0, 0), 2),
                new(4, 4, "H00", new(0, 0), 2)
            ]),
            new PeriodicMotifCell("V11",
            [
                new(0, 0, "H11", new(0, 0), 4),
                new(1, 1, "H10", new(0, 1), 0),
                new(2, 2, "H00", new(0, 1), 1),
                new(3, 3, "V01", new(0, 0), 1),
                new(4, 4, "H01", new(0, 0), 3)
            ])
            ],
            "Cairo tiling: alternating square-centered bars, edge-derived periodic motif.");
        // Adjacent small-bar endpoints are placed so that the other four
        // sides have equal length; the pentagon has 90°,120°,90°,120°,120°
        // interior angles. All values are geometric data, not runtime rules.
        double offset = (1 - 1 / Math.Sqrt(3)) / 2;
        var metric = new PeriodicMetricRealization(
            "world-unit", new(2, 0), new(0, 2),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["H00"] =
                [
                    new(0, 0),
                    new(0.5, -0.5 + offset),
                    new(1, 0),
                    new(0.5 + offset, 0.5),
                    new(0.5 - offset, 0.5)
                ],
                ["H01"] =
                [
                    new(0, 1),
                    new(0.5 - offset, 0.5),
                    new(0.5 + offset, 0.5),
                    new(1, 1),
                    new(0.5, 1.5 - offset)
                ],
                ["H10"] =
                [
                    new(1, 0),
                    new(1.5 - offset, -0.5),
                    new(1.5 + offset, -0.5),
                    new(2, 0),
                    new(1.5, 0.5 - offset)
                ],
                ["H11"] =
                [
                    new(1, 1),
                    new(1.5, 0.5 + offset),
                    new(2, 1),
                    new(1.5 + offset, 1.5),
                    new(1.5 - offset, 1.5)
                ],
                ["V00"] =
                [
                    new(0, 0),
                    new(0.5 - offset, 0.5),
                    new(0, 1),
                    new(-0.5, 0.5 + offset),
                    new(-0.5, 0.5 - offset)
                ],
                ["V01"] =
                [
                    new(0, 1),
                    new(0.5, 1.5 - offset),
                    new(0.5, 1.5 + offset),
                    new(0, 2),
                    new(-0.5 + offset, 1.5)
                ],
                ["V10"] =
                [
                    new(1, 0),
                    new(1.5, 0.5 - offset),
                    new(1.5, 0.5 + offset),
                    new(1, 1),
                    new(0.5 + offset, 0.5)
                ],
                ["V11"] =
                [
                    new(1, 1),
                    new(1.5 - offset, 1.5),
                    new(1, 2),
                    new(0.5, 1.5 + offset),
                    new(0.5, 1.5 - offset)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Cairo pentagonal tiling — Type 4", null,
            ["Cairo tiling", "pentagonal tiling type 4"],
            "Periodic tiling by eight congruent convex Cairo pentagons",
            "Journal of Scientific Reports, 2025: 4-fold Cairo pattern; derivation from alternating square-centered bars",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-4",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
