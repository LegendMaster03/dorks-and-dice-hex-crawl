using HexCrawl.Domain.Presentation;

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
            "Periodic mixed-cell tessellation", "Phase 16 structural fixture"),
        new("<24:2 7 6 10 14 16 18 20 17 19 24 23,3 5 9 12 13 15 17 19 21 22 24 23,4 6 8 11 14 15 18 21 22 19 23 24:4 4 4,3 6 3>",
            "Rhombille tiling", "Rhombille tessellation", ["rhombus tiling"],
            "A periodic tessellation of rhombi", "Phase 16 metric fixture",
            Geometry: new(4, EqualSideLengths: true))
    ]);
}
