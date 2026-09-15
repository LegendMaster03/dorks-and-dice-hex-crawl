using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Tests;

public sealed class RuntimeEngineTests
{
    private readonly CrawlRuntimeEngine _engine = new();

    [Fact]
    public void FixedDistanceWatchUsesResolvedDistance()
    {
        var setup = CreateSetup();
        var profile = CrawlProcedureProfile.SimplifiedFixedDistance();

        var result = Advance(setup, profile, 4, continueAcrossBoundaries: true);

        Assert.Equal(4, result.Expedition.DistanceTraveled.Value, 6);
        Assert.Equal(4, result.Expedition.Traversal.Progress.Value, 6);
        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Null(result.PauseReason);
    }

    [Fact]
    public void AlexandrianVariableDistanceUsesExplicitD6Results()
    {
        var expected = Miles(12);
        var resolved = TravelDistanceResolver.AlexandrianVariable(
            expected,
            2,
            5,
            new ResolutionProvenance(ResolutionSource.ManualRoll, "physical dice"));

        Assert.Equal(12, resolved.ActualDistance!.Value.Value, 6);
        Assert.Equal(ResolutionSource.ManualRoll, resolved.Provenance.Source);
    }

    [Fact]
    public void NavigationSuccessKeepsIntendedCourse()
    {
        var setup = CreateSetup();
        var result = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            2,
            navigation: NavigationCheckOutcome.Succeeded,
            encounter: ResolvedEncounter.None);

        Assert.False(result.Expedition.Navigation.IsLost);
        Assert.Equal(new HexDirection(0), result.Expedition.IntendedDirection);
        Assert.Equal(new HexDirection(0), result.Expedition.ActualDirection);
    }

    [Fact]
    public void NavigationFailureMakesExpeditionLostAndAppliesVeer()
    {
        var setup = CreateSetup();
        var result = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            2,
            navigation: NavigationCheckOutcome.Failed,
            veerSteps: 1,
            encounter: ResolvedEncounter.None);

        Assert.True(result.Expedition.Navigation.IsLost);
        Assert.Equal(1, result.Expedition.Navigation.VeerSteps);
        Assert.Equal(new HexDirection(1), result.Expedition.ActualDirection);
        Assert.Contains(result.Events, item => item.Kind == CrawlRuntimeEventKind.ExpeditionBecameLost);
    }

    [Fact]
    public void PersistentVeerIgnoresSmallerSubsequentFailure()
    {
        var setup = CreateSetup(expedition: CreateExpedition(navigation: new NavigationRuntimeState(true, 2)));
        var result = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            1,
            navigation: NavigationCheckOutcome.Failed,
            veerSteps: 1,
            encounter: ResolvedEncounter.None);

        Assert.Equal(2, result.Expedition.Navigation.VeerSteps);
        Assert.Equal(new HexDirection(2), result.Expedition.ActualDirection);
    }

    [Fact]
    public void LostBoundaryCanBeRecognizedAndReoriented()
    {
        var setup = CreateSetup();
        var first = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            6,
            navigation: NavigationCheckOutcome.Failed,
            veerSteps: 1,
            encounter: ResolvedEncounter.None,
            continueAcrossBoundaries: true);

        Assert.Equal(RuntimePauseReason.LostRecognitionRequired, first.PauseReason);
        var resumed = _engine.Advance(
            setup.World,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            first.Expedition,
            first.Knowledge,
            Plan(0, true),
            new WatchAdvanceInputs(
                TravelDistanceResolver.Fixed(Miles(0)),
                BoundaryDecision: new BoundaryNavigationDecision(
                    true,
                    true,
                    new ResolutionProvenance(ResolutionSource.ManualRoll, "party realized the error"))));

        Assert.False(resumed.Expedition.Navigation.IsLost);
        Assert.Contains(resumed.Events, item => item.Kind == CrawlRuntimeEventKind.ExpeditionReoriented);
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(1, 12)]
    [InlineData(2, 6)]
    [InlineData(3, 6)]
    public void EntryContextProducesFarNearAndBackExitRequirements(int direction, double expectedMiles)
    {
        var traversal = new HexTraversalState
        {
            CurrentHex = new HexCoordinate(0, 0),
            EntryDirection = new HexDirection(0),
            Progress = Miles(0)
        };
        var setup = CreateSetup(expedition: CreateExpedition(traversal: traversal));

        var result = Advance(
            setup,
            CrawlProcedureProfile.SimplifiedFixedDistance() with { SupportsDeliberateDoubleBack = true },
            0,
            direction: direction,
            continueAcrossBoundaries: true);

        Assert.Equal(expectedMiles, result.Expedition.Traversal.CurrentExitRequirement!.Value.Value, 6);
    }

    [Fact]
    public void DirectionChangeCanConsumeAbstractProgress()
    {
        var traversal = new HexTraversalState
        {
            CurrentHex = new HexCoordinate(0, 0),
            EntryDirection = new HexDirection(0),
            LastTravelDirection = new HexDirection(0),
            Progress = Miles(5)
        };
        var setup = CreateSetup(expedition: CreateExpedition(traversal: traversal));
        var profile = CrawlProcedureProfile.AlexandrianAdvancedBaseline() with
        {
            UsesNavigationChecks = false,
            EncounterCadence = EncounterCheckCadence.None
        };

        var result = Advance(setup, profile, 0, direction: 1, continueAcrossBoundaries: true);

        Assert.Equal(3, result.Expedition.Traversal.Progress.Value, 6);
        Assert.Contains(result.Events, item => item.Kind == CrawlRuntimeEventKind.DirectionChanged);
    }

    [Fact]
    public void DeliberateDoubleBackRetracesProgressToEntryBoundary()
    {
        var traversal = new HexTraversalState
        {
            CurrentHex = new HexCoordinate(0, 0),
            EntryDirection = new HexDirection(0),
            LastTravelDirection = new HexDirection(0),
            Progress = Miles(4)
        };
        var setup = CreateSetup(expedition: CreateExpedition(traversal: traversal));
        var profile = CrawlProcedureProfile.AlexandrianAdvancedBaseline() with
        {
            UsesNavigationChecks = false,
            EncounterCadence = EncounterCheckCadence.None
        };
        var plan = Plan(3, true) with { DeliberateDoubleBack = true };

        var result = _engine.Advance(
            setup.World,
            profile,
            setup.Expedition,
            setup.Knowledge,
            plan,
            new WatchAdvanceInputs(TravelDistanceResolver.Fixed(Miles(4))));

        Assert.Equal(new HexCoordinate(-1, 0), result.Expedition.CurrentHex);
        Assert.Equal(RuntimePauseReason.BacktrackBoundaryReached, result.PauseReason);
    }

    [Fact]
    public void SingleBoundaryCrossingCarriesRemainingProgressIntoNextHex()
    {
        var setup = CreateSetup();
        var result = Advance(setup, CrawlProcedureProfile.SimplifiedFixedDistance(), 8, continueAcrossBoundaries: true);

        Assert.Equal(new HexCoordinate(1, 0), result.Expedition.CurrentHex);
        Assert.Equal(2, result.Expedition.Traversal.Progress.Value, 6);
        Assert.Single(result.Events.Where(item => item.Kind == CrawlRuntimeEventKind.HexEntered));
    }

    [Fact]
    public void OneWatchCanCrossMultipleHexes()
    {
        var setup = CreateSetup();
        var result = Advance(setup, CrawlProcedureProfile.SimplifiedFixedDistance(), 30, continueAcrossBoundaries: true);

        Assert.Equal(new HexCoordinate(3, 0), result.Expedition.CurrentHex);
        Assert.Equal(3, result.Events.Count(item => item.Kind == CrawlRuntimeEventKind.HexEntered));
        Assert.Equal(30, result.Expedition.DistanceTraveled.Value, 6);
    }

    [Fact]
    public void BoundaryPausePreservesWatchRemainderForConditionReview()
    {
        var setup = CreateSetup();
        var result = Advance(setup, CrawlProcedureProfile.SimplifiedFixedDistance(), 12, continueAcrossBoundaries: false);

        Assert.Equal(RuntimePauseReason.ConditionsReviewRequired, result.PauseReason);
        Assert.Equal(TimeSpan.FromHours(2), result.RemainingWatchTime);
        Assert.Equal(new HexCoordinate(1, 0), result.Expedition.CurrentHex);
        Assert.Equal(6, result.Expedition.DistanceTraveled.Value, 6);
    }

    [Fact]
    public void NoEncounterCompletesNormally()
    {
        var setup = CreateSetup();
        var result = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            0,
            navigation: NavigationCheckOutcome.Succeeded,
            encounter: ResolvedEncounter.None);

        Assert.Null(result.PauseReason);
        Assert.Contains(result.Events, item => item.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed);
        Assert.Contains(result.Events, item => item.Kind == CrawlRuntimeEventKind.WatchCompleted);
    }

    [Fact]
    public void WanderingEncounterPausesAtResolvedTime()
    {
        var setup = CreateSetup();
        var encounter = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter,
            TimeSpan.FromHours(2),
            null,
            "owlbear sign",
            new ResolutionProvenance(ResolutionSource.AutomaticRoll));

        var result = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            8,
            navigation: NavigationCheckOutcome.Succeeded,
            encounter: encounter,
            continueAcrossBoundaries: true);

        Assert.Equal(RuntimePauseReason.EncounterTriggered, result.PauseReason);
        Assert.Equal(TimeSpan.FromHours(2), result.RemainingWatchTime);
        Assert.Equal(4, result.Expedition.DistanceTraveled.Value, 6);
    }

    [Fact]
    public void KeyedLocationDiscoveryDoesNotRevealOtherHexContents()
    {
        var location = new Location(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Hidden ruin",
            "ruin",
            new WorldPoint(0, 0),
            LocationDiscoverability.Hidden,
            []);
        var feature = new PointFeature(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Unknown spring",
            "spring",
            new WorldPoint(0.2, 0.1));
        var setup = CreateSetup(world: CreateWorld(locations: [location], features: [feature]));
        var encounter = new ResolvedEncounter(
            EncounterOutcomeKind.KeyedLocationDiscovery,
            TimeSpan.Zero,
            location.Id,
            null,
            new ResolutionProvenance(ResolutionSource.ManualRoll));

        var result = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            0,
            navigation: NavigationCheckOutcome.Succeeded,
            encounter: encounter);

        Assert.Equal(KnowledgeState.Discovered, result.Knowledge.Entries[location.Id].State);
        Assert.False(result.Knowledge.Entries.ContainsKey(feature.Id));
        Assert.Single(result.Knowledge.Entries);
    }

    [Fact]
    public void EnteringHexDoesNotAutomaticallyDiscoverItsKeyedLocation()
    {
        var world = CreateWorld();
        var neighborCenter = HexGeometry.HexToWorld(world.Grid, new HexCoordinate(1, 0));
        var location = new Location(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Keyed cave",
            "cave",
            neighborCenter,
            LocationDiscoverability.Obvious,
            []);
        world = world with { Locations = [location] };
        var setup = CreateSetup(world: world);

        var result = Advance(setup, CrawlProcedureProfile.SimplifiedFixedDistance(), 8, continueAcrossBoundaries: true);

        Assert.Equal(new HexCoordinate(1, 0), result.Expedition.CurrentHex);
        Assert.Empty(result.Knowledge.Entries);
    }

    [Fact]
    public void DisabledNavigationIgnoresNavigationRollMechanics()
    {
        var setup = CreateSetup();
        var result = Advance(setup, CrawlProcedureProfile.SimplifiedFixedDistance(), 1);

        Assert.False(result.Expedition.Navigation.IsLost);
        Assert.Equal(result.Expedition.IntendedDirection, result.Expedition.ActualDirection);
    }

    [Fact]
    public void SimplifiedHexStepProfileCanCrossMultipleHexes()
    {
        var setup = CreateSetup();
        var profile = CrawlProcedureProfile.SimplifiedHexStep();
        var result = _engine.Advance(
            setup.World,
            profile,
            setup.Expedition,
            setup.Knowledge,
            Plan(0, true),
            new WatchAdvanceInputs(ResolvedTravelAmount.Steps(2, ResolutionProvenance.ProcedureDefault)));

        Assert.Equal(new HexCoordinate(2, 0), result.Expedition.CurrentHex);
        Assert.Equal(24, result.Expedition.DistanceTraveled.Value, 6);
        Assert.Equal(1, result.Expedition.CompletedWatches);
    }

    [Fact]
    public void DmOverrideIsExplicitInHistory()
    {
        var setup = CreateSetup();
        var travel = TravelDistanceResolver.Override(Miles(12), Miles(3), "DM set travel to 3 miles");
        var result = _engine.Advance(
            setup.World,
            CrawlProcedureProfile.SimplifiedFixedDistance(),
            setup.Expedition,
            setup.Knowledge,
            Plan(0, true),
            new WatchAdvanceInputs(travel, DmOverrideNote: "mudslide ruling"));

        Assert.Contains(result.Events, item => item.Kind == CrawlRuntimeEventKind.DmOverrideApplied && item.Message == "mudslide ruling");
        Assert.Equal(3, result.Expedition.DistanceTraveled.Value, 6);
    }

    [Fact]
    public void IdenticalInputsReplayDeterministically()
    {
        var setup = CreateSetup();
        var profile = CrawlProcedureProfile.AlexandrianAdvancedBaseline();
        var encounter = new ResolvedEncounter(
            EncounterOutcomeKind.WanderingEncounter,
            TimeSpan.FromHours(3),
            null,
            "test encounter",
            new ResolutionProvenance(ResolutionSource.ExternalSystem, "fixture"));

        var first = Advance(setup, profile, 7, NavigationCheckOutcome.Succeeded, encounter: encounter, continueAcrossBoundaries: true);
        var second = Advance(setup, profile, 7, NavigationCheckOutcome.Succeeded, encounter: encounter, continueAcrossBoundaries: true);

        Assert.Equal(first.Expedition, second.Expedition);
        Assert.Equal(first.Knowledge, second.Knowledge);
        Assert.Equal(first.Events, second.Events);
        Assert.Equal(first.PauseReason, second.PauseReason);
        Assert.Equal(first.RemainingWatchTime, second.RemainingWatchTime);
    }

    [Fact]
    public void RuntimeHistoryDoesNotDuplicateEventsAcrossInternalResolutionStages()
    {
        var setup = CreateSetup();
        var result = Advance(
            setup,
            CrawlProcedureProfile.AlexandrianAdvancedBaseline(),
            1,
            NavigationCheckOutcome.Succeeded,
            encounter: ResolvedEncounter.None);

        Assert.Equal(result.Events.Count, result.Expedition.History.Count);
        Assert.Equal(result.Expedition.History.Count, result.Expedition.History.Select(item => item.Sequence).Distinct().Count());
    }

    private WatchAdvanceResult Advance(
        Setup setup,
        CrawlProcedureProfile profile,
        double miles,
        NavigationCheckOutcome navigation = NavigationCheckOutcome.NotRequired,
        ResolvedEncounter? encounter = null,
        int direction = 0,
        int veerSteps = 1,
        bool continueAcrossBoundaries = false)
    {
        var resolvedNavigation = navigation == NavigationCheckOutcome.NotRequired
            ? null
            : new ResolvedNavigation(
                navigation,
                navigation == NavigationCheckOutcome.Failed ? veerSteps : null,
                new ResolutionProvenance(ResolutionSource.ManualRoll));

        return _engine.Advance(
            setup.World,
            profile,
            setup.Expedition,
            setup.Knowledge,
            Plan(direction, continueAcrossBoundaries),
            new WatchAdvanceInputs(
                TravelDistanceResolver.Fixed(Miles(miles)),
                resolvedNavigation,
                encounter));
    }

    private static WatchTravelPlan Plan(int direction, bool continueAcrossBoundaries) => new(
        new HexDirection(direction),
        TravelModeSelection.Normal,
        NavigationAidSelection.None,
        false,
        continueAcrossBoundaries);

    private static Setup CreateSetup(
        OverworldDefinition? world = null,
        ExpeditionState? expedition = null,
        PlayerKnowledgeState? knowledge = null)
    {
        world ??= CreateWorld();
        expedition ??= CreateExpedition(world: world);
        knowledge ??= new PlayerKnowledgeState
        {
            ScopeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            OverworldId = world.Id
        };
        return new Setup(world, expedition, knowledge);
    }

    private static OverworldDefinition CreateWorld(
        IReadOnlyList<Location>? locations = null,
        IReadOnlyList<SpatialFeature>? features = null) => new()
    {
        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Name = "Runtime fixture",
        Grid = new HexGridDefinition
        {
            Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            NeighborCenterDistance = Miles(12)
        },
        Locations = locations ?? [],
        Features = features ?? []
    };

    private static ExpeditionState CreateExpedition(
        OverworldDefinition? world = null,
        HexTraversalState? traversal = null,
        NavigationRuntimeState? navigation = null)
    {
        world ??= CreateWorld();
        traversal ??= HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles);
        return new ExpeditionState
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            OverworldId = world.Id,
            Position = HexGeometry.HexToWorld(world.Grid, traversal.CurrentHex),
            PositionPrecision = WorldPositionPrecision.HexAnchor,
            Traversal = traversal,
            Navigation = navigation ?? new NavigationRuntimeState(false, 0),
            DistanceTraveled = Miles(0)
        };
    }

    private static DistanceMeasure Miles(double value) => new(value, DistanceUnit.Miles);

    private sealed record Setup(
        OverworldDefinition World,
        ExpeditionState Expedition,
        PlayerKnowledgeState Knowledge);
}
