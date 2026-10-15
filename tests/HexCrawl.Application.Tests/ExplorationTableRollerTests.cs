using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class ExplorationTableRollerTests
{
    private sealed class FixedRandom(int value) : IProcedureResolutionRandomSource
    {
        public int Calls { get; private set; }
        public int NextInt32(int minInclusive, int maxExclusive)
        {
            Calls++;
            Assert.InRange(value, minInclusive, maxExclusive - 1);
            return value;
        }
    }

    private static ExplorationTableRollRequest Table(int sides = 6) =>
        new(ExplorationTableScope.Location, "location", sides, "GM-authored fixture",
        [
            new(1, 2, "Low result"),
            new(3, 5, "Middle result"),
            new(6, 6, "High result")
        ]);

    [Fact]
    public void ResolvesSuppliedRowsThroughExistingRandomSourceAndRetainsRollAudit()
    {
        var source = new FixedRandom(4);
        var result = Assert.Single(new ExplorationTableRoller(source).Roll([Table()]));
        Assert.Equal(ExplorationTableScope.Location, result.Scope);
        Assert.Equal("location", result.Result.TableKey);
        Assert.Equal("Middle result", result.Result.ResultText);
        Assert.Equal("1d6=4", result.Result.Roll);
        Assert.Equal("GM-authored fixture", result.Result.SourceReference);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void RejectsUncoveredAndOverlappingRollTablesWithoutCallingRng()
    {
        var source = new FixedRandom(2);
        var roller = new ExplorationTableRoller(source);
        Assert.Throws<InvalidOperationException>(() =>
            roller.Roll([Table() with { Rows = [new(1, 2, "A"), new(4, 6, "B")] }]));
        Assert.Throws<InvalidOperationException>(() =>
            roller.Roll([Table() with { Rows = [new(1, 3, "A"), new(3, 6, "B")] }]));
        Assert.Throws<InvalidOperationException>(() =>
            roller.Roll([Table() with { Rows = [new(1, 7, "A")] }]));
        Assert.Throws<InvalidOperationException>(() =>
            roller.Roll([Table() with { SourceReference = " " }]));
        Assert.Throws<InvalidOperationException>(() =>
            roller.Roll([Table(1001)]));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void RejectsAmbiguousDuplicateTablesPerScope()
    {
        var roller = new ExplorationTableRoller(new FixedRandom(2));
        Assert.Throws<InvalidOperationException>(() =>
            roller.Roll([Table(), Table() with { TableKey = "LOCATION" }]));
        Assert.Equal(2, roller.Roll([
            Table(), Table() with { Scope = ExplorationTableScope.Region }
        ]).Count);
    }
}
