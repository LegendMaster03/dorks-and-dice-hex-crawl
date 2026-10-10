using HexCrawl.Application;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

public sealed record Phase19CompatibilityAssessment(
    string Status, string WorldShape, string? ObservedShape, string? ProcedureShape,
    string Explanation, string? WorldComparison, string? ProcedureComparison,
    long WorldVersion, IReadOnlyList<string> Blockers, bool CanCorrectInPlace)
{
    public Phase19RasterRegistrationResult? Registration { get; init; }
}

/// <summary>
/// Advisory, read-only comparison. Candidate raster observations are never an
/// authoritative metric realization. Names do not participate in comparisons.
/// </summary>
public static class Phase19TilingCompatibility
{
    private static readonly TilingNomenclatureCatalog Names = new([
        new("<1:1,1,1:3,6>", "Triangular tiling", null, ["triangular"],
            "Regular triangles", "Euclidean geometry",
            Geometry: new(3, EqualSideLengths: true, InteriorAngleDegrees: 60)),
        new("<1:1,1,1:4,4>", "Square tiling", null, ["square"],
            "Regular squares", "Euclidean geometry",
            Geometry: new(4, EqualSideLengths: true, InteriorAngleDegrees: 90)),
        new("<1:1,1,1:6,3>", "Hexagonal tiling", null, ["hexagonal"],
            "Regular hexagons", "Euclidean geometry",
            Geometry: new(6, EqualSideLengths: true, InteriorAngleDegrees: 120))
    ]);

    public static Phase19CompatibilityAssessment Assess(
        PeriodicWorldTiling world, PeriodicMotifInvestigation observed,
        CampaignProcedure? procedure, IReadOnlyList<string> blockers, long worldVersion)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(blockers);
        world.Validate();
        var worldName = Names.Describe(world.Topology.QuotientDsSymbol, world.Realization)?.Label
            ?? "a repeating pattern of polygonal cells";
        var procedureName = procedure is null ? null
            : Names.Describe(procedure.TilingDsSymbol)?.Label
                ?? "a repeating pattern of unspecified shape";
        var candidate = observed.Status == "consistent-candidate"
            && !observed.Authoritative
            && observed.Evidence?.MetricRegistration is
                { Status: "registered", SourceProjection.Status: "supported" }
            ? observed.Candidate : null;
        if (candidate is null)
            return new(
                observed.Status == "unsupported" ? "unsupported" : "inconclusive",
                worldName, null, procedureName,
                "The image does not establish a compatible repeating pattern. "
                + "The saved world and ruleset were not changed. Use manual placement or retry.",
                null, null, worldVersion, blockers, false);

        var observedName = DescribeObserved(candidate);
        var worldComparison = DelaneyDressTopology.Compare(
            world.Topology.QuotientDsSymbol, candidate.DsSymbol,
            world.Topology.TranslationDsSymbol);
        var procedureComparison = procedure is null ? null
            : DelaneyDressTopology.Compare(procedure.TilingDsSymbol, candidate.DsSymbol,
                world.Topology.TranslationDsSymbol);
        var mismatched = Incompatible(worldComparison.Status)
            || procedureComparison is { Status: var s1 } && Incompatible(s1);
        var uncertain = Uncertain(worldComparison.Status)
            || procedureComparison is { Status: var s2 } && Uncertain(s2);
        var explanation = mismatched
            ? $"The image appears to use {observedName}, while the world uses {worldName}"
                + (procedureName is null ? "." : $" and the selected ruleset expects {procedureName}.")
                + " Existing geometry and saved ruleset revisions remain unchanged."
            : uncertain
                ? $"The image appears to use {observedName}, but equivalence with the world "
                    + "or selected ruleset has not been proven."
                : $"The image appears structurally compatible with {worldName}."
                    + " Its measured geometry still requires independent checks of "
                    + "scale, rotation, phase, and existing registrations.";
        return new Phase19CompatibilityAssessment(
            mismatched ? "mismatch" : uncertain ? "inconclusive" : "structure-match",
            worldName, observedName, procedureName, explanation,
            worldComparison.Status.ToString(), procedureComparison?.Status.ToString(),
            worldVersion, blockers, false)
        {
            Registration = mismatched || uncertain || worldComparison.Status is not (
                    TilingComparisonStatus.ExactIdentity or TilingComparisonStatus.ChamberIsomorphic
                        or TilingComparisonStatus.ProvenEquivalent)
                ? null : Phase19RasterRegistration.Assess(world, observed)
        };
    }

    private static bool Incompatible(TilingComparisonStatus value) =>
        value is TilingComparisonStatus.StructurallyIncompatible
            or TilingComparisonStatus.MetricallyIncompatible;
    private static bool Uncertain(TilingComparisonStatus value) =>
        value is TilingComparisonStatus.Inconclusive or TilingComparisonStatus.Invalid
            or TilingComparisonStatus.LimitExceeded;

    private static string DescribeObserved(PeriodicMotifCandidate candidate)
    {
        var counts = candidate.MotifCells.Select(x => x.PolygonSourcePixels.Count).Distinct()
            .OrderBy(x => x).ToArray();
        if (counts.Length == 0) return "a repeating pattern of polygonal cells";
        static string Shape(int sides) => sides switch
        {
            3 => "triangles", 4 => "four-sided cells", 5 => "five-sided cells",
            6 => "six-sided cells", _ => $"{sides}-sided cells"
        };
        return "a repeating pattern of " + string.Join(" and ", counts.Select(Shape));
    }
}
