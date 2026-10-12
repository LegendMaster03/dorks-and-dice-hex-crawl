using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureResolutionHelperTests
{
    [Fact]
    public void GeometricHelperKeepsExplicitDistanceUnitsAndAngularFailureVeer()
    {
        var procedure = CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve("alexandrian-advanced")).Procedure;
        var runtime = GenericProcedureRuntime.Bind(procedure);
        var mapless = Context();
        var state = AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(), mapless, new HexCoordinate(0, 0)).Expedition;
        var resolver = new ProcedureResolutionResolver(
            new SequenceRandomSource(1, 1, 1, 1, 2));
        var result = resolver.ResolveCells(
            runtime, mapless, state, 1,
            new CellProcedureResolutionHelperCommand
            {
                ExpectedVersion = 1,
                ExpectedDistance = new DistanceMeasure(2, DistanceUnit.Miles),
                NavigationDifficultyClass = 20,
                FailureVeerDegrees = 37.5
            });

        Assert.Equal(2d, result.Travel!.ExpectedDistance!.Value.Value);
        Assert.Equal(DistanceUnit.Miles, result.Travel.ExpectedDistance.Value.Unit);
        Assert.Equal(1d, result.Travel.ActualDistance!.Value.Value, 8);
        Assert.Equal(DistanceUnit.Miles, result.Travel.ActualDistance.Value.Unit);
        Assert.Equal(ResolutionSource.AutomaticRoll, result.Travel.Provenance.Source);
        Assert.Equal(NavigationCheckOutcome.Failed, result.Navigation!.Outcome);
        Assert.Equal(37.5, result.Navigation.VeerDegreesOnFailure);
        Assert.Contains("degrees", result.Navigation.Provenance.Note!);
        Assert.Equal(EncounterOutcomeKind.WanderingEncounter, result.Encounter!.Kind);
        Assert.Equal(TimeSpan.FromHours(1), result.Encounter.OccursAt);
        Assert.Equal(4, result.Rolls.Count);
    }

    [Fact]
    public void GeometricHelperRejectsFailedNavigationWithoutConfirmedAngularError()
    {
        var procedure = CampaignProcedureMaterializer.MaterializeExecutableSpatial(CrawlProcedureCatalog.Resolve("alexandrian-advanced")).Procedure;
        var mapless = Context();
        var state = AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(), mapless, new HexCoordinate(0, 0)).Expedition;
        var resolver = new ProcedureResolutionResolver(
            new SequenceRandomSource(1, 1, 1));
        Assert.Throws<InvalidOperationException>(() => resolver.ResolveCells(
            GenericProcedureRuntime.Bind(procedure),
            mapless, state, 1,
            new CellProcedureResolutionHelperCommand
            {
                ExpectedVersion = 1,
                ExpectedDistance = new DistanceMeasure(2, DistanceUnit.Miles),
                NavigationDifficultyClass = 20,
                FailureVeerDegrees = null
            }));
        Assert.Empty(state.History);
        Assert.Null(state.ActiveWatch);
    }

    private static AbstractHexCrawlSessionContext Context() => new(
        "Helper test",
        HexOrientation.PointyTop,
        new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));

    private sealed class SequenceRandomSource(params int[] values) : IProcedureResolutionRandomSource
    {
        private readonly Queue<int> _values = new(values);

        public int NextInt32(int minInclusive, int maxExclusive)
        {
            var value = _values.Dequeue();
            Assert.InRange(value, minInclusive, maxExclusive - 1);
            return value;
        }
    }
}
