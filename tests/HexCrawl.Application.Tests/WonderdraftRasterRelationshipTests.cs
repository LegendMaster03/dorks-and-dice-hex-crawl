using HexCrawl.Infrastructure.Wonderdraft;

namespace HexCrawl.Application.Tests;

public sealed class WonderdraftRasterRelationshipTests
{
    [Fact]
    public void ExactDimensionsAreSafeForProjectCoordinateMapping()
    {
        var relationship = WonderdraftRasterRelationship.Analyze(2048, 1536, 2048, 1536);

        Assert.Equal(WonderdraftRasterRelationshipKind.Exact, relationship.Kind);
        Assert.True(relationship.CanMapProjectCoordinates);
        Assert.Equal(1, relationship.UniformScale);
        Assert.Equal(0, relationship.WidthResidualPixels);
        Assert.Equal(0, relationship.HeightResidualPixels);
    }

    [Fact]
    public void ProportionalExportAtDifferentResolutionIsSafe()
    {
        var relationship = WonderdraftRasterRelationship.Analyze(1024, 768, 2048, 1536);

        Assert.Equal(WonderdraftRasterRelationshipKind.ProportionalScale, relationship.Kind);
        Assert.True(relationship.CanMapProjectCoordinates);
        Assert.Equal(2, relationship.UniformScale);
        Assert.Equal(0, relationship.WidthResidualPixels, 8);
        Assert.Equal(0, relationship.HeightResidualPixels, 8);
    }

    [Fact]
    public void IntegerRoundingWithinOnePixelDoesNotRejectProportionalExport()
    {
        var relationship = WonderdraftRasterRelationship.Analyze(1000, 777, 667, 518);

        Assert.Equal(WonderdraftRasterRelationshipKind.ProportionalScale, relationship.Kind);
        Assert.True(relationship.CanMapProjectCoordinates);
        Assert.NotNull(relationship.UniformScale);
    }

    [Fact]
    public void MagnostephisDimensionsAreRejectedAsUnresolvedRatherThanStretched()
    {
        var relationship = WonderdraftRasterRelationship.Analyze(2107, 1536, 1403, 1026);

        Assert.Equal(WonderdraftRasterRelationshipKind.UnresolvedDimensionMismatch, relationship.Kind);
        Assert.False(relationship.CanMapProjectCoordinates);
        Assert.Null(relationship.UniformScale);
        Assert.True(Math.Abs(relationship.WidthResidualPixels) > 0.75);
        Assert.True(Math.Abs(relationship.HeightResidualPixels) > 0.75);
        Assert.Contains("cropping, padding, or nonuniform scaling", relationship.Explanation);
    }
}
