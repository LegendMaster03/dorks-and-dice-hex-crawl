using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureResolutionHelperTests
{
    [Fact]
    public void AlexandrianHelperGeneratesExplicitResolvedInputsWithAutomaticProvenance()
    {
        var procedure = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure;
        var runtime = GenericProcedureRuntime.Bind(procedure);
        var resolver = new ProcedureResolutionResolver(new SequenceRandomSource(4, 5, 13, 1, 6));
        var result = resolver.Resolve(
            runtime,
            Context(),
            State(),
            7,
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 7,
                ExpectedDistance = 10,
                NavigationDifficultyClass = 14,
                NavigationModifier = 2,
                FailureVeerSteps = 1
            });

        Assert.Equal(7, result.ExpeditionVersion);
        Assert.Null(result.GeneratedResolutionId);
        Assert.Null(result.AuditSequence);
        var travel = Assert.IsType<ProcedureResolvedTravel>(result.Travel);
        Assert.Equal(10d, travel.ExpectedDistance);
        Assert.Equal(12d, travel.ActualDistance, 8);
        Assert.Equal(ResolutionSource.AutomaticRoll, travel.Provenance.Source);

        var navigation = Assert.IsType<ProcedureResolvedNavigation>(result.Navigation);
        Assert.Equal(NavigationCheckOutcome.Succeeded, navigation.Outcome);
        Assert.Null(navigation.VeerSteps);
        Assert.Equal(ResolutionSource.AutomaticRoll, navigation.Provenance.Source);

        var encounter = Assert.IsType<ProcedureResolvedEncounter>(result.Encounter);
        Assert.Equal(EncounterOutcomeKind.WanderingEncounter, encounter.Kind);
        Assert.Equal(3d, encounter.OccursAtHours);
        Assert.Equal(ResolutionSource.AutomaticRoll, encounter.Provenance.Source);

        Assert.Equal(4, result.Rolls.Count);
        Assert.Equal("2d6+3", result.Rolls[0].Formula);
        Assert.Equal(new[] { 4, 5 }, result.Rolls[0].Dice);
        Assert.Equal(12, result.Rolls[0].Total);
        Assert.Equal("1d20", result.Rolls[1].Formula);
        Assert.Equal(13, result.Rolls[1].Total);
        Assert.Equal("1d8", result.Rolls[2].Formula);
        Assert.Equal(1, result.Rolls[2].Total);
        Assert.Equal("1d8", result.Rolls[3].Formula);
        Assert.Equal(6, result.Rolls[3].Total);
    }

    [Fact]
    public void FailedNavigationUsesDmConfirmedNonZeroVeer()
    {
        var baseline = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure;
        var modules = baseline.Modules.Select(module =>
        {
            if (module.Module.Key == GenericProcedureCatalog.EncounterCadenceModule)
            {
                return module with
                {
                    Parameters = new Dictionary<string, string>(module.Parameters, StringComparer.Ordinal)
                    {
                        ["cadence"] = EncounterCheckCadence.None.ToString()
                    }
                };
            }
            if (module.Module.Key == GenericProcedureCatalog.ResolutionHelpersModule)
            {
                var parameters = new Dictionary<string, string>(module.Parameters, StringComparer.Ordinal)
                {
                    ["travel.enabled"] = "false",
                    ["encounter.enabled"] = "false",
                    ["navigation.enabled"] = "true"
                };
                return module with { Parameters = parameters };
            }
            return module;
        }).ToArray();
        var runtime = GenericProcedureRuntime.Bind(baseline with { Modules = modules });
        var resolver = new ProcedureResolutionResolver(new SequenceRandomSource(5));

        var result = resolver.Resolve(
            runtime,
            Context(),
            State(),
            3,
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                NavigationDifficultyClass = 10,
                FailureVeerSteps = -1
            });

        Assert.Null(result.Travel);
        Assert.Null(result.Encounter);
        var navigation = Assert.IsType<ProcedureResolvedNavigation>(result.Navigation);
        Assert.Equal(NavigationCheckOutcome.Failed, navigation.Outcome);
        Assert.Equal(-1, navigation.VeerSteps);
        Assert.Contains("DM-confirmed failure veer=-1", navigation.Provenance.Note!);
    }

    [Fact]
    public void GeometricHelperKeepsExplicitDistanceUnitsAndAngularFailureVeer()
    {
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
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
        var procedure = GeometricNativeProcedureMaterializer.Materialize(
            "alexandrian-advanced").Procedure;
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

    private static ExpeditionState State() => new()
    {
        Id = Guid.NewGuid(),
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
    };

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
