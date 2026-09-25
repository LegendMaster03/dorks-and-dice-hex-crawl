using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class DistanceMeasureTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void CustomDistanceUnitRejectsInvalidConversionFactors(double metersPerUnit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DistanceUnit.Custom("hex", metersPerUnit));
    }

    [Fact]
    public void CustomDistanceUnitAllowsUnknownPhysicalConversion()
    {
        var unit = DistanceUnit.Custom("hex");

        Assert.Equal("hex", unit.Symbol);
        Assert.Null(unit.MetersPerUnit);
    }
}
