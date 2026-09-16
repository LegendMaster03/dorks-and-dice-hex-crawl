using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class AffineRegistrationSolverTests
{
    [Fact]
    public void IdentityAffineMapsControlPointsAndInteriorPoint()
    {
        var transform = AffineRegistrationSolver.Solve(Points(
            (new(0, 0), new(0, 0)),
            (new(10, 0), new(10, 0)),
            (new(0, 8), new(0, 8))));
        Assert.Equal(new WorldPoint(4, 3), transform.ToWorld(new WorldPoint(4, 3)));
    }

    [Fact]
    public void TranslationAffineIsSolvedExactly()
    {
        var transform = AffineRegistrationSolver.Solve(Points(
            (new(0, 0), new(12, -7)),
            (new(2, 0), new(14, -7)),
            (new(0, 2), new(12, -5))));
        Assert.Equal(new WorldPoint(13, -6), transform.ToWorld(new WorldPoint(1, 1)));
    }

    [Fact]
    public void NonUniformScaleAffineIsSolvedExactly()
    {
        var transform = AffineRegistrationSolver.Solve(Points(
            (new(0, 0), new(0, 0)),
            (new(3, 0), new(6, 0)),
            (new(0, 4), new(0, 12))));
        Assert.Equal(new WorldPoint(4, 6), transform.ToWorld(new WorldPoint(2, 2)));
    }

    [Fact]
    public void RotationAffineIsSolvedExactly()
    {
        var transform = AffineRegistrationSolver.Solve(Points(
            (new(0, 0), new(0, 0)),
            (new(1, 0), new(0, 1)),
            (new(0, 1), new(-1, 0))));
        var mapped = transform.ToWorld(new WorldPoint(2, 3));
        Assert.Equal(-3, mapped.X, 10);
        Assert.Equal(2, mapped.Y, 10);
    }

    [Fact]
    public void EveryControlPointMapsBackToItsWorldPoint()
    {
        var points = Points(
            (new(10, 20), new(-4, 9)),
            (new(70, 10), new(11, 6)),
            (new(25, 90), new(2, 30)));
        var transform = AffineRegistrationSolver.Solve(points);
        foreach (var pair in points)
        {
            var mapped = transform.ToWorld(pair.SourcePixel);
            Assert.Equal(pair.WorldPoint.X, mapped.X, 9);
            Assert.Equal(pair.WorldPoint.Y, mapped.Y, 9);
        }
    }

    [Fact]
    public void DuplicateOrCollinearControlPointsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => AffineRegistrationSolver.Solve(Points(
            (new(0, 0), new(0, 0)),
            (new(0, 0), new(2, 0)),
            (new(0, 2), new(0, 2)))));
        Assert.Throws<ArgumentException>(() => AffineRegistrationSolver.Solve(Points(
            (new(0, 0), new(0, 0)),
            (new(1, 1), new(1, 1)),
            (new(2, 2), new(2, 2)))));
    }

    [Fact]
    public void DegenerateWorldMappingIsRejected()
    {
        Assert.Throws<ArgumentException>(() => AffineRegistrationSolver.Solve(Points(
            (new(0, 0), new(0, 0)),
            (new(1, 0), new(1, 1)),
            (new(0, 1), new(2, 2)))));
    }

    [Fact]
    public void CoverageMapsAllFourRasterCorners()
    {
        var transform = MapRegistrationTransform.Affine(2, 0, 0, 3, 10, -5);
        Assert.Equal(
            new[]
            {
                new WorldPoint(10, -5),
                new WorldPoint(18, -5),
                new WorldPoint(18, 13),
                new WorldPoint(10, 13)
            },
            AffineRegistrationSolver.Coverage(transform, 4, 6));
    }

    private static MapRegistrationControlPoint[] Points(
        params (WorldPoint Source, WorldPoint World)[] points) =>
        points.Select(point => new MapRegistrationControlPoint(point.Source, point.World)).ToArray();
}
