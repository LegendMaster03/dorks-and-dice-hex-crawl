using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

public sealed class Phase19RasterRegistrationTests
{
    [Theory]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:3,6>")]
    [InlineData("<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>")]
    [InlineData("<24:2 7 6 10 14 16 18 20 17 19 24 23,3 5 9 12 13 15 17 19 21 22 24 23,4 6 8 11 14 15 18 21 22 19 23 24:4 4 4,3 6 3>")]
    [InlineData("<28:2 7 6 10 13 15 17 18 20 22 25 23 27 28,3 5 9 12 11 14 16 19 21 24 23 26 27 28,4 6 8 11 13 14 17 19 20 23 25 22 28 27:6 4 4,3 4 4 3>")]
    public void IndependentlyValidatesMetricPhaseAndReciprocalInterfaces(string symbol)
    {
        var (world, evidence) = Fixture(symbol);
        var registration = Phase19RasterRegistration.Assess(world, evidence);
        Assert.Equal("candidates", registration.Status);
        Assert.NotEmpty(registration.Candidates);
        Assert.All(registration.Candidates, candidate =>
        {
            Assert.InRange(candidate.MaximumResidualSourcePixels, 0, 1e-5);
            var candidateWorld = candidate.Alignment.ToWorld(
                new WorldPoint(evidence.Candidate!.MotifCells[0].PolygonSourcePixels[0].X,
                    evidence.Candidate.MotifCells[0].PolygonSourcePixels[0].Y));
            Assert.True(double.IsFinite(candidateWorld.X) && double.IsFinite(candidateWorld.Y));
        });
    }

    [Fact]
    public void RasterMismatchOrIncompleteMetricCannotBeAccepted()
    {
        var (world, observed) = Fixture("<1:1,1,1:4,4>");
        var missing = observed with { Status = "ambiguous", Candidate = null };
        Assert.Empty(Phase19RasterRegistration.Assess(world, missing).Candidates);
        var wrongWorld = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:3,6>", 2, "world-unit");
        var triangular = new PeriodicWorldTiling(
            Guid.NewGuid(), wrongWorld.Topology!, wrongWorld.Realization!,
            new WorldPoint(20, -10));
        Assert.Equal("unavailable",
            Phase19RasterRegistration.Assess(triangular, observed).Status);

        var candidate = observed.Candidate!;
        var offset = candidate.MotifCells.Select(cell => cell with
        {
            PolygonSourcePixels = cell.PolygonSourcePixels
                .Select((point, i) => i == 0
                    ? new MapAnalysisPoint(point.X + 60, point.Y + 60) : point).ToArray()
        }).ToArray();
        Assert.Empty(Phase19RasterRegistration.Assess(world,
            observed with { Candidate = candidate with { MotifCells = offset } }).Candidates);
    }

    internal static (PeriodicWorldTiling World, PeriodicMotifInvestigation Evidence) Fixture(string symbol)
    {
        var realized = DelaneyDressHarmonicMetricRealization.Construct(
            symbol, 2, "world-unit", rotationDegrees: 19);
        Assert.True(realized.Status == "realized", realized.Reason);
        var topology = realized.Topology!;
        var metric = realized.Realization!;
        var world = new PeriodicWorldTiling(
            Guid.NewGuid(), topology, metric, new WorldPoint(20, -10));
        world.Validate();
        const double factor = 160;
        var all = metric.Polygons.Values.SelectMany(p => p).ToArray();
        double minX = all.Min(x => x.X), minY = all.Min(y => y.Y);
        MapAnalysisPoint Pixel(TilingWorldPoint p) =>
            new(40 + (p.X - minX) * factor, 40 + (p.Y - minY) * factor);
        MapAnalysisPoint Basis(TilingWorldPoint p) => new(p.X * factor, p.Y * factor);
        var observed = new PeriodicMotifCandidate(topology.TranslationDsSymbol,
            [Basis(metric.TranslationU), Basis(metric.TranslationV)],
            topology.MotifCells.Select(cell => new ObservedMotifCell(
                cell.Id, metric.Polygons[cell.Id].Select(Pixel).ToArray(),
                cell.Boundary.Select(edge => new ObservedMotifBoundary(
                    edge.Index, edge.TargetMotifCellId,
                    edge.ReciprocalInterfaceIndex,
                    edge.TargetTranslation.U, edge.TargetTranslation.V, 6)).ToArray())).ToArray());
        var metricEvidence = new PeriodicMotifMetricEvidence(
            "registered", .1, .05, .99, 4, 4, 4,
            new PeriodicMotifSourceProjectionEvidence("supported", .99, .99, 7, 7));
        var evidence = new PeriodicMotifInvestigation(
            "consistent-candidate", "Synthetic metric observation", false, "experimental",
            "v3", observed,
            new PeriodicMotifEvidence(8, 8, 0, 6, .99, .1, .1, metricEvidence),
            new MapAnalysisSource(2048, 2048, "image/png"),
            new MapAnalysisRaster(2048, 2048, 1, true));
        return (world, evidence);
    }
}
