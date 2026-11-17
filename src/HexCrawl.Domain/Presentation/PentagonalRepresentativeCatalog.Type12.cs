using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

internal static partial class PentagonalRepresentativeCatalog
{
    /// <summary>
    /// Type 12: a periodic convex pentagon motif derived from published
    /// polygon-coordinate formulas. Rotations, edge pairing, and a primitive
    /// two-dimensional translation cover were independently verified.
    /// Mathematical data: Ed Pegg Jr., adapted by mathgrrl in
    /// pinkfish/openscad_boardgame_toolkit/pentagon_tilings.scad (Apache 2.0).
    /// Formula parameters: AA=125 degrees, b=0.
    /// </summary>
    internal static TilingNameEntry Type12()
    {
        const string symbol = "<96:2 7 6 10 14 16 18 20 21 24 26 29 32 34 36 39 41 43 38 42 47 50 53 56 51 54 59 55 63 65 67 66 71 72 74 76 78 79 82 85 86 83 89 91 90 92 95 96,3 5 9 12 13 15 17 19 23 25 28 30 31 33 35 38 40 42 46 44 49 48 52 54 55 57 58 61 62 64 67 69 70 73 75 76 77 80 81 84 87 88 91 90 93 94 95 96,4 6 8 11 14 15 18 19 22 24 27 29 32 33 37 39 40 44 45 42 48 50 51 53 56 57 59 60 62 65 66 68 70 72 75 76 77 79 83 85 86 82 90 92 89 91 96 95:6 6 6 6 6 6 6 6,3 3 3 3 3 3 3 3 3 3 3 3 3 3 3 3>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("P12-00",
                [
                    new(0, 0, "P12-01", new(0, -1), 4),
                    new(1, 1, "P12-03", new(0, 0), 2),
                    new(2, 2, "P12-04", new(0, 0), 5),
                    new(3, 2, "P12-05", new(0, 0), 5),
                    new(4, 3, "P12-07", new(0, 0), 3),
                    new(5, 4, "P12-04", new(0, -1), 2)
                ]),
                new PeriodicMotifCell("P12-01",
                [
                    new(0, 0, "P12-03", new(0, 0), 0),
                    new(1, 1, "P12-06", new(0, 0), 0),
                    new(2, 1, "P12-02", new(0, 0), 1),
                    new(3, 2, "P12-03", new(0, 1), 3),
                    new(4, 3, "P12-00", new(0, 1), 0),
                    new(5, 4, "P12-04", new(0, 0), 1)
                ]),
                new PeriodicMotifCell("P12-02",
                [
                    new(0, 0, "P12-03", new(0, 1), 4),
                    new(1, 1, "P12-01", new(0, 0), 2),
                    new(2, 2, "P12-06", new(0, 0), 5),
                    new(3, 2, "P12-07", new(1, 0), 5),
                    new(4, 3, "P12-05", new(1, 0), 3),
                    new(5, 4, "P12-06", new(0, 1), 2)
                ]),
                new PeriodicMotifCell("P12-03",
                [
                    new(0, 0, "P12-01", new(0, 0), 0),
                    new(1, 1, "P12-04", new(0, 0), 0),
                    new(2, 1, "P12-00", new(0, 0), 1),
                    new(3, 2, "P12-01", new(0, -1), 3),
                    new(4, 3, "P12-02", new(0, -1), 0),
                    new(5, 4, "P12-06", new(0, 0), 1)
                ]),
                new PeriodicMotifCell("P12-04",
                [
                    new(0, 0, "P12-03", new(0, 0), 1),
                    new(1, 1, "P12-01", new(0, 0), 5),
                    new(2, 1, "P12-00", new(0, 1), 5),
                    new(3, 2, "P12-07", new(0, 1), 2),
                    new(4, 3, "P12-05", new(0, 0), 0),
                    new(5, 4, "P12-00", new(0, 0), 2)
                ]),
                new PeriodicMotifCell("P12-05",
                [
                    new(0, 0, "P12-04", new(0, 0), 4),
                    new(1, 1, "P12-07", new(0, 1), 1),
                    new(2, 2, "P12-06", new(-1, 1), 3),
                    new(3, 2, "P12-02", new(-1, 0), 4),
                    new(4, 3, "P12-07", new(0, 0), 4),
                    new(5, 4, "P12-00", new(0, 0), 3)
                ]),
                new PeriodicMotifCell("P12-06",
                [
                    new(0, 0, "P12-01", new(0, 0), 1),
                    new(1, 1, "P12-03", new(0, 0), 5),
                    new(2, 1, "P12-02", new(0, -1), 5),
                    new(3, 2, "P12-05", new(1, -1), 2),
                    new(4, 3, "P12-07", new(1, 0), 0),
                    new(5, 4, "P12-02", new(0, 0), 2)
                ]),
                new PeriodicMotifCell("P12-07",
                [
                    new(0, 0, "P12-06", new(-1, 0), 4),
                    new(1, 1, "P12-05", new(0, -1), 1),
                    new(2, 2, "P12-04", new(0, -1), 3),
                    new(3, 2, "P12-00", new(0, 0), 4),
                    new(4, 3, "P12-05", new(0, 0), 4),
                    new(5, 4, "P12-02", new(-1, 0), 3)
                ])
            ],
            "OpenSCAD polygon formula, Type 12; independently reconstructed periodic incidence.");
        var metric = new PeriodicMetricRealization(
            "world-unit", new(3.75458794619909, -1.541684440378825), new(2.060386461833585, 1.250273197205544),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["P12-00"] =
                [
                    new(-2.060386461833585, -0.6002731972055443),
                    new(-1.153114083131688, -0.6002731972055443),
                    new(-0.4716785479160222, -0.2455403104971743),
                    new(-1.048235589481866, 0.05459628810559799),
                    new(-1.62479263104771, 0.3547328867083701),
                    new(-2.060386461833585, 0.04972680279445574)
                ],
                ["P12-01"] =
                [
                    new(0, 0),
                    new(0.4355938307858757, -0.3050060839139144),
                    new(0.9072723787018974, -0.05946577341674027),
                    new(1.588707913917563, 0.2952671132916299),
                    new(0.9072723787018974, 0.65),
                    new(0, 0.65)
                ],
                ["P12-02"] =
                [
                    new(2.49598029261946, 0.2952671132916299),
                    new(1.588707913917563, 0.2952671132916299),
                    new(0.9072723787018974, -0.05946577341674027),
                    new(1.483829420267741, -0.3596023720195123),
                    new(2.060386461833585, -0.6597389706222845),
                    new(2.49598029261946, -0.3547328867083702)
                ],
                ["P12-03"] =
                [
                    new(0.4355938307858747, -0.3050060839139144),
                    new(0, 0),
                    new(-0.4716785479160222, -0.2455403104971743),
                    new(-1.153114083131688, -0.6002731972055443),
                    new(-0.4716785479160222, -0.9550060839139145),
                    new(0.4355938307858747, -0.9550060839139145)
                ],
                ["P12-04"] =
                [
                    new(-0.4716785479160217, -0.245540310497174),
                    new(0, 0),
                    new(0, 0.65),
                    new(0, 1.3),
                    new(-0.6293038267898144, 0.8593567166575549),
                    new(-1.048235589481866, 0.05459628810559799)
                ],
                ["P12-05"] =
                [
                    new(-1.048235589481866, 0.05459628810559799),
                    new(-0.6293038267898144, 0.8593567166575549),
                    new(-0.6293038267898144, 1.627594837012901),
                    new(-1.25860765357963, 1.186951553670455),
                    new(-1.694201484365504, 0.8819454697565413),
                    new(-1.62479263104771, 0.3547328867083702)
                ],
                ["P12-06"] =
                [
                    new(0.907272378701897, -0.05946577341673986),
                    new(0.4355938307858753, -0.3050060839139137),
                    new(0.4355938307858747, -0.9550060839139145),
                    new(0.4355938307858753, -1.605006083913913),
                    new(1.06489765757569, -1.164362800571469),
                    new(1.483829420267741, -0.3596023720195123)
                ],
                ["P12-07"] =
                [
                    new(-2.270758525931349, 1.182082068359313),
                    new(-2.6896902886234, 0.3773216398073562),
                    new(-2.6896902886234, -0.3909164805479894),
                    new(-2.060386461833585, 0.04972680279445574),
                    new(-1.62479263104771, 0.3547328867083701),
                    new(-1.694201484365504, 0.8819454697565412)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Rice pentagonal tiling — Type 12", null,
            ["convex pentagonal tiling type 12"],
            "8 congruent convex pentagons in a periodic motif",
            "Ed Pegg Jr., Wolfram Demonstrations; mathgrrl OpenSCAD formula (Apache 2.0).",
            Geometry: new(5), CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-12",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }
}
