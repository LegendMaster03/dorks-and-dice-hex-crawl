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
    public Phase19RasterRegistrationResult? Correction { get; init; }
}

/// <summary>
/// Advisory, read-only comparison. Candidate raster observations are never an
/// authoritative metric realization. Names do not participate in comparisons.
/// </summary>
public static class Phase19TilingCompatibility
{
    private static readonly TilingNomenclatureCatalog Names = CrawlTilingNames.Catalog;

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
                "The image does not establish a verified repeating pattern. "
                + (string.IsNullOrWhiteSpace(observed.Reason)
                    ? "The detector did not provide a specific reason. "
                    : $"Detector result ({observed.Status}): {observed.Reason.Trim()} ")
                + "The saved world and ruleset were not changed. Use manual placement or retry.",
                null, null, worldVersion, blockers, false);

        var observedName = DescribeObserved(candidate);
        var worldComparison = DelaneyDressTopology.Compare(
            world.Topology.QuotientDsSymbol, candidate.DsSymbol,
            world.Topology.TranslationDsSymbol);
        var procedureComparison = procedure is null ? null
            : DelaneyDressTopology.Compare(procedure.TilingDsSymbol, candidate.DsSymbol,
                world.Topology.TranslationDsSymbol);
        var worldMismatch = Incompatible(worldComparison.Status);
        var worldUncertain = Uncertain(worldComparison.Status);
        var procedureMismatch = procedureComparison is { Status: var ps }
            && Incompatible(ps);
        var procedureUncertain = procedureComparison is { Status: var pu }
            && Uncertain(pu);
        var mismatched = worldMismatch || procedureMismatch;
        var uncertain = worldUncertain || procedureUncertain;
        var explanation = worldMismatch
            ? $"The image appears to use {observedName}, while the world uses {worldName}. "
                + "Existing geometry and saved ruleset revisions remain unchanged."
            : worldUncertain
                ? $"The image appears to use {observedName}, but equivalence with the world has not been proven."
            : procedureMismatch
                ? $"The image appears structurally compatible with {worldName}, but the selected ruleset expects {procedureName}. "
                    + "The image can still be aligned to the unchanged world; that ruleset cannot start an expedition in it."
            : procedureUncertain
                ? $"The image appears structurally compatible with {worldName}, but the selected ruleset has not been proven compatible. "
                    + "Image placement and ruleset readiness are separate decisions."
            : $"The image appears structurally compatible with {worldName}."
                + " Its measured geometry still requires independent checks of "
                + "scale, rotation, phase, and existing registrations.";
        // Empty uncalibrated worlds can replace their unused spatial
        // identity in one guarded write. Pinned procedures that already
        // describe the observed pattern require no new revision; incompatible
        // selected procedures are never silently rewritten.
        var compatibleProcedure = procedureComparison is null || procedureComparison.Status is
            TilingComparisonStatus.ExactIdentity or TilingComparisonStatus.ChamberIsomorphic
                or TilingComparisonStatus.ProvenEquivalent;
        var replacement = worldMismatch && compatibleProcedure && blockers.Count == 0
            ? Phase19RasterRegistration.ConstructReplacement(world, observed)
            : null;
        var correction = replacement is null ? null
            : Phase19RasterRegistration.Assess(replacement, observed);
        bool canCorrect = correction is { Status: "candidates", Candidates.Count: > 0 };
        return new Phase19CompatibilityAssessment(
            mismatched ? "mismatch" : uncertain ? "inconclusive" : "structure-match",
            worldName, observedName, procedureName, explanation,
            worldComparison.Status.ToString(), procedureComparison?.Status.ToString(),
            worldVersion, blockers, canCorrect)
        {
            Correction = canCorrect ? correction : null,
            Registration = worldMismatch || worldUncertain || worldComparison.Status is not (
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
