using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class SourceMapRepresentationTests
{
    [Fact]
    public void AffineRegistrationMapsSourcePixelsIntoOverworldCoordinates()
    {
        var transform = MapRegistrationTransform.Affine(2, 0, 0, 3, 10, -5);
        Assert.Equal(new WorldPoint(18, 13), transform.ToWorld(new WorldPoint(4, 6)));
    }

    [Fact]
    public void ProjectiveRegistrationSupportsFuturePerspectiveCorrection()
    {
        var transform = new MapRegistrationTransform(
            MapRegistrationTransformKind.Projective,
            1, 0, 0,
            0, 1, 0,
            .1, 0);

        var projected = transform.ToWorld(new WorldPoint(10, 4));
        Assert.Equal(5, projected.X, 8);
        Assert.Equal(2, projected.Y, 8);
    }
}
