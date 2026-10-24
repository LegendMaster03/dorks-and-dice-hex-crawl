using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class WorldCellDisplayTests
{
    [Theory]
    [InlineData("face-0", 21, -3, "face-0@21,-3")]
    [InlineData("triangle-1", -2, 3, "triangle-1@-2,3")]
    public void HumanReadableCellKeepsMotifAndLatticeWithoutDiagnosticExpansion(
        string motif, long u, long v, string expected)
    {
        var tilingId = Guid.NewGuid();
        var cell = new WorldCellId(tilingId,
            new PeriodicCellAddress(motif, new LatticeDisplacement(u, v)));
        Assert.Equal(expected, $"{cell}");
        Assert.Equal(tilingId, cell.TilingId);
        Assert.NotEqual(cell, cell with { TilingId = Guid.NewGuid() });
    }
}
