using HexCrawl.Application;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

public sealed class Phase19MotifCompatibilityTests
{
    private const string Square = "<1:1,1,1:4,4>";
    private const string Triangle = "<1:1,1,1:3,6>";

    [Fact]
    public void MatchingCandidateDoesNotAuthorizeUnsafeWorldMutation()
    {
        var world = World();
        var assessed = Phase19TilingCompatibility.Assess(
            world, Observation(Square), null, ["A saved expedition exists."], 11);
        Assert.Equal("structure-match", assessed.Status);
        Assert.Equal(11, assessed.WorldVersion);
        Assert.Null(assessed.ProcedureComparison);
        Assert.Contains("Square", assessed.WorldShape);
        Assert.False(assessed.CanCorrectInPlace);
        Assert.Single(assessed.Blockers);
        Assert.DoesNotContain("<1:", assessed.Explanation);
    }

    [Fact]
    public void StructuralMismatchAndSelectedProcedureAreReportedWithoutChangingAuthority()
    {
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance")
            .MaterializeGeneric().Procedure with { TilingDsSymbol = Triangle };
        var assessed = Phase19TilingCompatibility.Assess(
            World(), Observation(Triangle), procedure, [], 1);
        Assert.Equal("mismatch", assessed.Status);
        Assert.Equal("StructurallyIncompatible", assessed.WorldComparison);
        Assert.NotNull(assessed.ProcedureComparison);
        Assert.False(assessed.CanCorrectInPlace);
        Assert.DoesNotContain("<1:", assessed.Explanation);
    }

    [Fact]
    public void SelectedIncompatibleProcedureDoesNotHideProvenImageWorldRegistration()
    {
        var (world, image) = Phase19RasterRegistrationTests.Fixture(Square);
        var incompatibleProcedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance")
            .MaterializeGeneric().Procedure with { TilingDsSymbol = Triangle };
        var reviewed = Phase19TilingCompatibility.Assess(
            world, image, incompatibleProcedure, [], 7);
        Assert.Equal("mismatch", reviewed.Status);
        Assert.Equal(7, reviewed.WorldVersion);
        Assert.True(reviewed.WorldComparison is "ExactIdentity" or "ChamberIsomorphic" or "ProvenEquivalent");
        Assert.Equal("StructurallyIncompatible", reviewed.ProcedureComparison);
        Assert.Equal("candidates", reviewed.Registration?.Status);
        Assert.NotEmpty(reviewed.Registration!.Candidates);
        Assert.False(reviewed.CanCorrectInPlace);
        Assert.Null(reviewed.Correction);
        Assert.Contains("selected ruleset", reviewed.Explanation);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("ambiguous")]
    [InlineData("inconclusive")]
    public void ObservationWithNoValidatedMetricRemainsAdvisory(string status)
    {
        var investigation = new PeriodicMotifInvestigation(
            status, "No qualified observation", false, "experimental",
            "v3", null, null, null, null);
        var assessed = Phase19TilingCompatibility.Assess(
            World(), investigation, null, [], 2);
        Assert.Equal(status == "unsupported" ? "unsupported" : "inconclusive", assessed.Status);
        Assert.Null(assessed.ObservedShape);
        Assert.False(assessed.CanCorrectInPlace);
        Assert.Contains("Detector result (" + status + "): No qualified observation",
            assessed.Explanation);
    }

    private static PeriodicWorldTiling World()
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            Square, 1.5, "world-unit");
        Assert.Equal("realized", realized.Status);
        var world = new PeriodicWorldTiling(
            Guid.NewGuid(), realized.Topology!, realized.Realization!,
            new WorldPoint(0, 0));
        world.Validate();
        return world;
    }

    private static PeriodicMotifInvestigation Observation(string symbol)
    {
        var corners = symbol == Square
            ? new[] { new MapAnalysisPoint(0, 0), new(2, 0), new(2, 2), new(0, 2) }
            : new[] { new MapAnalysisPoint(0, 0), new(2, 0), new(1, 1.732) };
        var metric = new PeriodicMotifMetricEvidence(
            "registered", 0.5, 0.1, 0.9, 4, 4, 4,
            new PeriodicMotifSourceProjectionEvidence("supported", 0.9, 0.9, 7, 7));
        return new PeriodicMotifInvestigation(
            "consistent-candidate", "Candidate", false, "experimental", "v3",
            new PeriodicMotifCandidate(symbol,
                [new MapAnalysisPoint(2, 0), new(0, 2)],
                [new ObservedMotifCell("observed", corners, [])]),
            new PeriodicMotifEvidence(7, 7, 0, 7, 0.9, 0.1, 0.1, metric),
            new MapAnalysisSource(256, 256, "image/png"),
            new MapAnalysisRaster(256, 256, 1, true));
    }
}
