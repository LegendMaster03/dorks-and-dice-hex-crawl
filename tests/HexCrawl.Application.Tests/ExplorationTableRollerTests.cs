using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureRangeTableTests
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

    private static ProcedureRangeTable Table(int sides = 6) =>
        new("location", sides, "GM-authored fixture",
        [
            new(1, 2, "Low result"),
            new(3, 5, "Middle result"),
            new(6, 6, "High result")
        ]);

    [Fact]
    public void SharedProcedureResolverEvaluatesRangeTablesWithExistingDiceSource()
    {
        var source = new FixedRandom(4);
        var result = Assert.Single(new ProcedureResolutionResolver(source)
            .ResolveRangeTables([Table()]));
        Assert.Equal("location", result.TableKey);
        Assert.Equal("Middle result", result.ResultText);
        Assert.Equal(6, result.DieSides);
        Assert.Equal(4, result.RolledFace);
        Assert.Equal("GM-authored fixture", result.SourceReference);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void RejectsUncoveredOverlappingAndUnattributedTablesWithoutRolling()
    {
        var source = new FixedRandom(2);
        var resolver = new ProcedureResolutionResolver(source);
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveRangeTables(
            [Table() with { Rows = [new(1, 2, "A"), new(4, 6, "B")] }]));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveRangeTables(
            [Table() with { Rows = [new(1, 3, "A"), new(3, 6, "B")] }]));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveRangeTables(
            [Table() with { Rows = [new(1, 7, "A")] }]));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveRangeTables(
            [Table() with { SourceReference = " " }]));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveRangeTables([Table(1001)]));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void ExplorationSubjectMappingRemainsAThinWrapperOverGenericTables()
    {
        var location = new ExplorationTableRollRequest(
            ExplorationTableScope.Location, "location", 6, "GM-authored fixture", Table().Rows);
        var region = location with { Scope = ExplorationTableScope.Region };
        ExplorationTableRollRequest.ValidateCollection([location, region]);
        Assert.Throws<InvalidOperationException>(() =>
            ExplorationTableRollRequest.ValidateCollection([location, location with { TableKey = "LOCATION" }]));
        var generic = location.ToProcedureRangeTable();
        Assert.Equal(Table(), generic);
    }

    [Fact]
    public void IdenticalResolutionRunsForHexAndSquareRulesetProcedures()
    {
        // The resolver has no preset or tiling-specific dispatch: only the
        // selected procedure differs while generic table execution is identical.
        var hex = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure;
        var square = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.WastelandWarfarePresetKey)
            .MaterializeGeneric().Procedure;
        Assert.NotEqual(hex.TilingDsSymbol, square.TilingDsSymbol);

        foreach (var procedure in new[] { hex, square })
        {
            procedure.Validate();
            var source = new FixedRandom(4);
            var outcome = Assert.Single(
                new ProcedureResolutionResolver(source).ResolveRangeTables([Table()]));
            Assert.Equal("Middle result", outcome.ResultText);
            Assert.Equal(4, outcome.RolledFace);
            Assert.Equal(1, source.Calls);
        }
    }
}
