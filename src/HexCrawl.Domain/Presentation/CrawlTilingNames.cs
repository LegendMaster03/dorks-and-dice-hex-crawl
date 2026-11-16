using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

/// <summary>Shared presentation vocabulary; not a restriction on valid periodic tilings.</summary>
public static class CrawlTilingNames
{
    public static readonly TilingNomenclatureCatalog Catalog = new([
        new("<1:1,1,1:3,6>", "Triangular tiling", null, ["triangular"],
            "Regular triangles", "Euclidean geometry",
            Geometry: new(3, EqualSideLengths: true, InteriorAngleDegrees: 60),
            CategoryPath: ["Triangles"], PresetId: "regular-triangle"),
        new("<1:1,1,1:4,4>", "Square tiling", null, ["square"],
            "Regular squares", "Euclidean geometry",
            Geometry: new(4, EqualSideLengths: true, InteriorAngleDegrees: 90),
            CategoryPath: ["Squares"], PresetId: "regular-square"),
        new("<1:1,1,1:6,3>", "Hexagonal tiling", null, ["hexagonal"],
            "Regular hexagons", "Euclidean geometry",
            Geometry: new(6, EqualSideLengths: true, InteriorAngleDegrees: 120),
            CategoryPath: ["Hexagons"], PresetId: "regular-hexagon"),
        new("<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>",
            "Triangle and quadrilateral tiling", null,
            ["mixed triangle and four-sided cells"],
            "Periodic mixed-cell tessellation", "Phase 16 structural fixture",
            ShowInRulesetSelector: false,
            CategoryPath: ["Mixed polygons"], PresetId: "triangle-quadrilateral"),
        new("<24:2 7 6 10 14 16 18 20 17 19 24 23,3 5 9 12 13 15 17 19 21 22 24 23,4 6 8 11 14 15 18 21 22 19 23 24:4 4 4,3 6 3>",
            "Rhombille tiling", "Rhombille tessellation", ["rhombus tiling"],
            "A periodic tessellation of rhombi", "Phase 16 metric fixture",
            Geometry: new(4, EqualSideLengths: true),
            CategoryPath: ["Quadrilaterals", "Rhombi"], PresetId: "rhombille"),
        VerifiedPentagonType1(),
        PentagonalRepresentativeCatalog.CairoType4(),
        PentagonalRepresentativeCatalog.HexagonalSubdivisionType3()
    ]);

    /// <summary>
    /// Data-only Type 1 example: split each centrally symmetric regular
    /// hexagon at midpoints of opposite edges. Each of the two convex halves
    /// has five sides and the adjacent two 90° angles required by Type 1.
    /// All boundary contacts and translations use the general witness format.
    /// </summary>
    private static TilingNameEntry VerifiedPentagonType1()
    {
        const string symbol = "<20:2 7 6 10 12 14 16 17 19 20,3 5 9 12 11 13 15 18 19 20,4 6 8 11 12 13 16 17 20 19:5 5,3 3 4>";
        var topology = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            [
                new PeriodicMotifCell("pentagon-a",
                [
                    new(0, 0, "pentagon-a", new(1, 0), 3),
                    new(1, 1, "pentagon-b", new(0, 1), 1),
                    new(2, 2, "pentagon-b", new(-1, 1), 2),
                    new(3, 3, "pentagon-a", new(-1, 0), 0),
                    new(4, 4, "pentagon-b", new(0, 0), 4)
                ]),
                new PeriodicMotifCell("pentagon-b",
                [
                    new(0, 0, "pentagon-b", new(-1, 0), 3),
                    new(1, 1, "pentagon-a", new(0, -1), 1),
                    new(2, 2, "pentagon-a", new(1, -1), 2),
                    new(3, 3, "pentagon-b", new(1, 0), 0),
                    new(4, 4, "pentagon-a", new(0, 0), 4)
                ])
            ],
            "Periodic bisected regular-hexagon construction (convex pentagon Type 1).");
        double root3 = Math.Sqrt(3);
        var metric = new PeriodicMetricRealization(
            "world-unit", new(1.5, root3 / 2), new(0, root3),
            new Dictionary<string, IReadOnlyList<TilingWorldPoint>>
            {
                ["pentagon-a"] =
                [
                    new(.75, root3 / 4), new(.5, root3 / 2),
                    new(-.5, root3 / 2), new(-1, 0),
                    new(-.75, -root3 / 4)
                ],
                ["pentagon-b"] =
                [
                    new(-.75, -root3 / 4), new(-.5, -root3 / 2),
                    new(.5, -root3 / 2), new(1, 0),
                    new(.75, root3 / 4)
                ]
            }, []);
        return new TilingNameEntry(
            symbol, "Convex pentagonal tiling — Type 1", null,
            ["pentagonal tiling type 1"], "A periodic tiling by congruent convex pentagons",
            "Two congruent halves of a translated centrally symmetric regular hexagon",
            Geometry: new(5),
            CategoryPath: ["Pentagons", "Convex"],
            PresetId: "convex-pentagon-type-1",
            RepresentativeTopology: topology, RepresentativeRealization: metric);
    }

    /// <summary>
    /// Supplies a verified Euclidean period for a named regular tiling when no
    /// author-specified metric exists. Custom patterns still use the general
    /// harmonic solver. These are geometric defaults, not gameplay presets.
    /// </summary>
    public static (double U, double V, double AngleDegrees)? VerifiedDefaultPeriod(string symbol)
    {
        var entry = Catalog.ListEntries()
            .FirstOrDefault(value => value.CanonicalDsSymbol == symbol
                && value.Geometry is not null && value.RepresentativeRealization is null);
        if (entry is null) return null;
        var preview = RepresentativePreview(entry);
        if (preview is null) return null;
        var u = Math.Sqrt(preview.TranslationU.X * preview.TranslationU.X + preview.TranslationU.Y * preview.TranslationU.Y);
        var v = Math.Sqrt(preview.TranslationV.X * preview.TranslationV.X + preview.TranslationV.Y * preview.TranslationV.Y);
        var dot = preview.TranslationU.X * preview.TranslationV.X
            + preview.TranslationU.Y * preview.TranslationV.Y;
        var angle = Math.Acos(Math.Clamp(dot / (u * v), -1, 1)) * 180 / Math.PI;
        return (u, v, angle);
    }

    /// <summary>Finds a verified geometric representative; never infers regularity from topology alone.</summary>
    public static PeriodicMetricRealization? RepresentativePreview(TilingNameEntry entry)
    {
        if (entry.RepresentativeRealization is { } supplied) return supplied;
        var candidates = new (double U, double V, double Angle)[]
        {
            (1, 1, 90), (1, 1, 60), (1, 1, 120),
            (1, Math.Sqrt(3), 60), (Math.Sqrt(3), 1, 60),
            (1, Math.Sqrt(3), 90), (Math.Sqrt(3), 1, 90)
        };
        foreach (var (u, v, angle) in candidates)
        {
            TilingMetricConstraint[] constraints =
            [
                new("period-u-length", "unit", u, u),
                new("period-v-length", "unit", v, v),
                new("period-angle-degrees", "degrees", angle, angle)
            ];
            var candidate = DelaneyDressHarmonicMetricRealization.Construct(
                entry.CanonicalDsSymbol, 1, "unit", constraints: constraints);
            if (candidate.Status != "realized" || candidate.Realization is null) continue;
            var resolved = Catalog.Describe(entry.CanonicalDsSymbol, candidate.Realization);
            if (resolved is { IsNamed: true }
                && string.Equals(resolved.Label, entry.PreferredName, StringComparison.Ordinal))
                return candidate.Realization;
        }
        return null;
    }
}
