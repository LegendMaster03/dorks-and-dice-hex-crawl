using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureResolutionHelperTests
{
    [Fact]
    public void AlexandrianHelperGeneratesExplicitResolvedInputsWithAutomaticProvenance()
    {
        var profile = CrawlProcedureProfile.AlexandrianAdvancedBaseline();
        var resolver = new ProcedureResolutionResolver(new SequenceRandomSource(4, 5, 13, 1, 6));
        var result = resolver.Resolve(
            profile,
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
        var baseline = CrawlProcedureProfile.AlexandrianAdvancedBaseline();
        var profile = baseline with
        {
            EncounterCadence = EncounterCheckCadence.None,
            ResolutionHelpers = new ProcedureResolutionHelperProfile(
                Navigation: baseline.ResolutionHelpers!.Navigation)
        };
        var resolver = new ProcedureResolutionResolver(new SequenceRandomSource(5));

        var result = resolver.Resolve(
            profile,
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
