using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Presentation;

/// <summary>Shared presentation vocabulary; not a restriction on valid periodic tilings.</summary>
public static class CrawlTilingNames
{
    public static readonly TilingNomenclatureCatalog Catalog = new([
        new("<1:1,1,1:3,6>", "Triangular tiling", null, ["triangular"],
            "Regular triangles", "Euclidean geometry",
            Geometry: new(3, EqualSideLengths: true, InteriorAngleDegrees: 60)),
        new("<1:1,1,1:4,4>", "Square tiling", null, ["square"],
            "Regular squares", "Euclidean geometry",
            Geometry: new(4, EqualSideLengths: true, InteriorAngleDegrees: 90)),
        new("<1:1,1,1:6,3>", "Hexagonal tiling", null, ["hexagonal"],
            "Regular hexagons", "Euclidean geometry",
            Geometry: new(6, EqualSideLengths: true, InteriorAngleDegrees: 120)),
        new("<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>",
            "Triangle and quadrilateral tiling", null,
            ["mixed triangle and four-sided cells"],
            "Periodic mixed-cell tessellation", "Phase 16 structural fixture",
            ShowInRulesetSelector: false),
        new("<24:2 7 6 10 14 16 18 20 17 19 24 23,3 5 9 12 13 15 17 19 21 22 24 23,4 6 8 11 14 15 18 21 22 19 23 24:4 4 4,3 6 3>",
            "Rhombille tiling", "Rhombille tessellation", ["rhombus tiling"],
            "A periodic tessellation of rhombi", "Phase 16 metric fixture",
            Geometry: new(4, EqualSideLengths: true))
    ]);

    /// <summary>
    /// Supplies a verified Euclidean period for a named regular tiling when no
    /// author-specified metric exists. Custom patterns still use the general
    /// harmonic solver. These are geometric defaults, not gameplay presets.
    /// </summary>
    public static (double U, double V, double AngleDegrees)? VerifiedDefaultPeriod(string symbol)
    {
        var entry = Catalog.ListEntries()
            .FirstOrDefault(value => value.CanonicalDsSymbol == symbol && value.Geometry is not null);
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
