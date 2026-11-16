using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Type 15's fixed convex prototile and 12-tile fundamental region from
    /// https://blogs.mathworks.com/graphics/2015/08/19/type-15-convex-pentagon/
    /// (2015, Mike Garrity). The source provides exact vertices, rotations,
    /// reflections and both translation generators. These coordinates were
    /// independently reconstructed; boundary T-junctions were subdivided
    /// before deriving the canonical translation D-symbol. Whole-lattice
    /// shifts put each tile near the origin for the bounded generic verifier.
    /// None of these is a runtime pentagon-specific algorithm.
    /// </summary>
    internal static TilingNameEntry MannMcLoudVonDerauType15()
    {
        const string symbol = "<128:2 7 6 10 12 15 18 20 21 24 25 28 30 29 33 35 37 40 42 44 46 47 49 51 53 54 58 60 63 62 65 68 69 72 74 73 78 77 81 84 85 88 90 91 94 95 97 98 101 102 107 106 111 110 113 115 116 118 119 121 124 125 127 128,3 5 9 12 13 14 17 19 23 22 27 25 29 32 34 36 38 39 41 43 45 48 50 51 54 55 57 59 61 64 67 66 70 71 73 76 77 80 82 83 86 87 89 92 93 96 98 99 102 103 105 108 109 112 114 113 117 116 120 122 123 126 127 128,4 6 8 11 12 16 18 19 22 24 26 28 30 31 33 34 37 39 42 41 45 47 52 49 53 56 59 60 62 64 66 68 70 72 74 75 78 79 81 83 85 87 89 91 93 95 97 100 101 104 106 108 110 112 114 115 117 118 119 121 125 124 128 127:5 5 5 5 6 6 6 6 5 5 5 5,4 4 3 3 3 3 3 3 3 3 3 3 3 3 3 3 3 3 4 4>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
            new PeriodicMotifCell("T15-00",
            [
                new(0, 0, "T15-01", new(0, 0), 3),
                new(1, 1, "T15-02", new(0, 1), 2),
                new(2, 2, "T15-03", new(0, 1), 5),
                new(3, 3, "T15-05", new(0, 0), 2),
                new(4, 4, "T15-03", new(0, 0), 1),
                new(5, 4, "T15-02", new(0, 0), 0)
            ]),
            new PeriodicMotifCell("T15-01",
            [
                new(0, 0, "T15-07", new(0, -1), 0),
                new(1, 1, "T15-07", new(0, 0), 1),
                new(2, 2, "T15-02", new(0, 1), 3),
                new(3, 3, "T15-00", new(0, 0), 0),
                new(4, 4, "T15-02", new(0, 0), 4)
            ]),
            new PeriodicMotifCell("T15-02",
            [
                new(0, 0, "T15-00", new(0, 0), 5),
                new(1, 1, "T15-03", new(0, 0), 0),
                new(2, 2, "T15-00", new(0, -1), 1),
                new(3, 3, "T15-01", new(0, -1), 2),
                new(4, 4, "T15-01", new(0, 0), 4)
            ]),
            new PeriodicMotifCell("T15-03",
            [
                new(0, 0, "T15-02", new(0, 0), 1),
                new(1, 1, "T15-00", new(0, 0), 4),
                new(2, 2, "T15-05", new(0, 0), 1),
                new(3, 3, "T15-04", new(0, -1), 0),
                new(4, 4, "T15-05", new(0, -1), 3),
                new(5, 4, "T15-00", new(0, -1), 2)
            ]),
            new PeriodicMotifCell("T15-04",
            [
                new(0, 0, "T15-03", new(0, 1), 3),
                new(1, 1, "T15-05", new(0, 1), 0),
                new(2, 2, "T15-10", new(0, 0), 2),
                new(3, 3, "T15-10", new(0, -1), 3),
                new(4, 4, "T15-05", new(0, 0), 4)
            ]),
            new PeriodicMotifCell("T15-05",
            [
                new(0, 0, "T15-04", new(0, -1), 1),
                new(1, 1, "T15-03", new(0, 0), 2),
                new(2, 2, "T15-00", new(0, 0), 3),
                new(3, 3, "T15-03", new(0, 1), 4),
                new(4, 4, "T15-04", new(0, 0), 4)
            ]),
            new PeriodicMotifCell("T15-06",
            [
                new(0, 0, "T15-07", new(0, 0), 3),
                new(1, 1, "T15-08", new(0, 0), 2),
                new(2, 2, "T15-09", new(0, 0), 5),
                new(3, 3, "T15-11", new(0, 0), 2),
                new(4, 4, "T15-09", new(0, 1), 1),
                new(5, 4, "T15-08", new(0, 1), 0)
            ]),
            new PeriodicMotifCell("T15-07",
            [
                new(0, 0, "T15-01", new(0, 1), 0),
                new(1, 1, "T15-01", new(0, 0), 1),
                new(2, 2, "T15-08", new(0, 0), 3),
                new(3, 3, "T15-06", new(0, 0), 0),
                new(4, 4, "T15-08", new(0, 1), 4)
            ]),
            new PeriodicMotifCell("T15-08",
            [
                new(0, 0, "T15-06", new(0, -1), 5),
                new(1, 1, "T15-09", new(0, 0), 0),
                new(2, 2, "T15-06", new(0, 0), 1),
                new(3, 3, "T15-07", new(0, 0), 2),
                new(4, 4, "T15-07", new(0, -1), 4)
            ]),
            new PeriodicMotifCell("T15-09",
            [
                new(0, 0, "T15-08", new(0, 0), 1),
                new(1, 1, "T15-06", new(0, -1), 4),
                new(2, 2, "T15-11", new(0, -1), 1),
                new(3, 3, "T15-10", new(1, 0), 0),
                new(4, 4, "T15-11", new(0, 0), 3),
                new(5, 4, "T15-06", new(0, 0), 2)
            ]),
            new PeriodicMotifCell("T15-10",
            [
                new(0, 0, "T15-09", new(-1, 0), 3),
                new(1, 1, "T15-11", new(-1, -1), 0),
                new(2, 2, "T15-04", new(0, 0), 2),
                new(3, 3, "T15-04", new(0, 1), 3),
                new(4, 4, "T15-11", new(-1, 0), 4)
            ]),
            new PeriodicMotifCell("T15-11",
            [
                new(0, 0, "T15-10", new(1, 1), 1),
                new(1, 1, "T15-09", new(0, 1), 2),
                new(2, 2, "T15-06", new(0, 0), 3),
                new(3, 3, "T15-09", new(0, 0), 4),
                new(4, 4, "T15-10", new(1, 0), 4)
            ])
            ],
            "Mann–McLoud–Von Derau Type 15; 12 congruent pentagons per periodic MathWorks motif.");
        var metric = new PeriodicMetricRealization(
            "world-unit",
            new((10 + 7 * Math.Sqrt(3)) / 4, (3 + 2 * Math.Sqrt(3)) / 4),
            new(-(1 + Math.Sqrt(3)) / 4, (3 + Math.Sqrt(3)) / 4),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["T15-00"] =
                [
                    new(0, 0),
                    new(-0.25, 0.4330127018922193),
                    new(-1.183012701892219, 0.6830127018922193),
                    new(-1.433012701892219, 0.25),
                    new(-1, 0),
                    new(-0.5, 0)
                ],
                ["T15-01"] =
                [
                    new(0.8660254037844386, -0.5),
                    new(0.8660254037844386, 0),
                    new(0.1830127018922192, 0.6830127018922192),
                    new(-0.25, 0.4330127018922192),
                    new(0, 0)
                ],
                ["T15-02"] =
                [
                    new(0, 0),
                    new(-0.5, 0),
                    new(-0.5, -0.5),
                    new(0.4330127018922193, -0.75),
                    new(0.8660254037844386, -0.5)
                ],
                ["T15-03"] =
                [
                    new(-0.5, -0.5),
                    new(-0.5, 0),
                    new(-1, 0),
                    new(-1.25, -0.9330127018922192),
                    new(-1, -1.366025403784438),
                    new(-0.75, -0.9330127018922192)
                ],
                ["T15-04"] =
                [
                    new(-1.683012701892219, -0.1830127018922192),
                    new(-1.933012701892219, 0.25),
                    new(-2.366025403784438, 0),
                    new(-2.116025403784438, -0.9330127018922192),
                    new(-1.683012701892219, -1.183012701892219)
                ],
                ["T15-05"] =
                [
                    new(-1.683012701892219, -1.183012701892219),
                    new(-1.25, -0.9330127018922192),
                    new(-1, 0),
                    new(-1.433012701892219, 0.25),
                    new(-1.683012701892219, -0.1830127018922192)
                ],
                ["T15-06"] =
                [
                    new(1.049038105676658, 0.6830127018922192),
                    new(1.299038105676658, 0.25),
                    new(2.232050807568877, 0),
                    new(2.482050807568877, 0.4330127018922194),
                    new(2.049038105676658, 0.6830127018922193),
                    new(1.549038105676658, 0.6830127018922192)
                ],
                ["T15-07"] =
                [
                    new(0.1830127018922192, 1.183012701892219),
                    new(0.1830127018922193, 0.6830127018922191),
                    new(0.8660254037844387, 0),
                    new(1.299038105676658, 0.25),
                    new(1.049038105676658, 0.6830127018922192)
                ],
                ["T15-08"] =
                [
                    new(1.732050807568878, -0.5),
                    new(2.232050807568877, -0.5),
                    new(2.232050807568877, 0),
                    new(1.299038105676658, 0.25),
                    new(0.8660254037844387, 0)
                ],
                ["T15-09"] =
                [
                    new(2.232050807568877, 0),
                    new(2.232050807568877, -0.5),
                    new(2.732050807568877, -0.5),
                    new(2.982050807568877, 0.4330127018922194),
                    new(2.732050807568877, 0.8660254037844387),
                    new(2.482050807568877, 0.4330127018922194)
                ],
                ["T15-10"] =
                [
                    new(-2.799038105676658, -0.75),
                    new(-2.549038105676658, -1.183012701892219),
                    new(-2.116025403784438, -0.9330127018922193),
                    new(-2.366025403784439, 0),
                    new(-2.799038105676658, 0.25)
                ],
                ["T15-11"] =
                [
                    new(2.732050807568878, 1.866025403784439),
                    new(2.299038105676658, 1.616025403784439),
                    new(2.049038105676658, 0.6830127018922194),
                    new(2.482050807568877, 0.4330127018922194),
                    new(2.732050807568878, 0.8660254037844388)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Mann–McLoud–Von Derau pentagons — Type 15", null,
            ["pentagonal tiling type 15", "Mann pentagon"],
            "A fixed-shape convex pentagonal tiling with a 12-cell periodic motif",
            "MathWorks Type 15 Convex Pentagon (Mike Garrity, 2015); source coordinate transformations",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-15",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
