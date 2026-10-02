using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class EnvironmentContextTests
{
    [Fact]
    public void EnvironmentFactsPreserveOpenDimensionsTagsMeasurementsAndNotesAreOptional()
    {
        var terrain = Tag("terrain", "swamp");
        var route = Tag("route", "good-road");
        var weather = Tag("weather", "heavy-rain");
        var visibility = Tag("visibility", "fog");
        var elevation = Measurement("elevation", 9000, "ft");
        var depth = Measurement("depth", 12, "m");
        var water = Tag("water", "river");
        var current = Measurement("current", 3.5, "knots");
        var temperature = Measurement("temperature", -10, "C");
        var hazard = Tag("hazard", "extreme-cold");
        var regional = Tag("regional-effect", "antimagic-zone");
        var custom = Tag("custom-environment-dimension", "source-defined-value");

        foreach (var fact in new[] { terrain, route, weather, visibility, elevation, depth, water, current, temperature, hazard, regional, custom })
        {
            fact.Validate();
            Assert.Null(fact.Note);
        }

        Assert.Equal(-10, temperature.Measurement!.Value);
        Assert.Equal("C", temperature.Measurement.Unit);
        Assert.Equal("source-defined-value", custom.Tag);
    }

    [Fact]
    public void WorldHexRegionAndLineFactsResolveButCategoryAloneDoesNot()
    {
        var region = new RegionFeature(Guid.NewGuid(), "Blackwood", "forest-looking-category",
        [
            new WorldPoint(-0.4, -0.4), new WorldPoint(0.4, -0.4),
            new WorldPoint(0.4, 0.4), new WorldPoint(-0.4, 0.4)
        ]);
        var road = new LinearFeature(Guid.NewGuid(), "King's Road", "road",
            [new WorldPoint(-0.6, 0), new WorldPoint(0.6, 0)]);
        var far = new LinearFeature(Guid.NewGuid(), "Far Trail", "road",
            [new WorldPoint(100, 100), new WorldPoint(101, 101)]);
        var world = World([region, road, far],
        [
            Annotation(WorldScope(), Tag("weather", "clear")),
            Annotation(HexScope(0, 0), Tag("terrain", "hills")),
            Annotation(FeatureScope(region.Id), Tag("terrain", "forest")),
            Annotation(FeatureScope(road.Id), Tag("route", "good-road")),
            Annotation(FeatureScope(far.Id), Tag("route", "far-road"))
        ]);
        var expedition = WorldExpedition(world, State(0, 0));

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var effective = EffectiveTags(context);

        Assert.Contains(("weather", "clear"), effective);
        Assert.Contains(("terrain", "hills"), effective);
        Assert.Contains(("terrain", "forest"), effective);
        Assert.Contains(("route", "good-road"), effective);
        Assert.DoesNotContain(("route", "far-road"), effective);
        Assert.DoesNotContain(effective, value => value.Item2 == "road");
        Assert.Equal(EnvironmentContextStatus.Resolved, context.Status);
    }

    [Fact]
    public void MovingHexRecomputesWorldTruthInsteadOfCopyingItIntoExpeditionState()
    {
        var world = World([], [
            Annotation(HexScope(0, 0), Tag("terrain", "forest")),
            Annotation(HexScope(1, 0), Tag("terrain", "swamp"))
        ]);
        var a = WorldExpedition(world, State(0, 0));
        var b = a with { Runtime = State(1, 0) };

        var first = EffectiveTags(EnvironmentContextResolver.Resolve(a, world));
        var second = EffectiveTags(EnvironmentContextResolver.Resolve(b, world));

        Assert.Contains(("terrain", "forest"), first);
        Assert.DoesNotContain(("terrain", "swamp"), first);
        Assert.Contains(("terrain", "swamp"), second);
        Assert.Empty(a.Environment.CurrentFacts);
        Assert.Empty(a.Environment.Overrides);
    }

    [Fact]
    public void AbstractAndNonSpatialContextsUseCurrentEnvironmentWithoutOverworldOrProvider()
    {
        var abstractExpedition = AbstractExpedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey)) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [Tag("terrain", "difficult")] }
        };
        var nonSpatial = NonSpatialExpedition(Procedure(CrawlProcedureCatalog.OneRing2ePresetKey)) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [Tag("weather", "rain")] }
        };

        var abstractContext = EnvironmentContextResolver.Resolve(abstractExpedition);
        var nonSpatialContext = EnvironmentContextResolver.Resolve(nonSpatial);

        Assert.Contains(("terrain", "difficult"), EffectiveTags(abstractContext));
        Assert.Contains(("weather", "rain"), EffectiveTags(nonSpatialContext));
        Assert.Equal(EnvironmentContextStatus.Resolved, abstractContext.Status);
        Assert.Equal(EnvironmentContextStatus.Resolved, nonSpatialContext.Status);
    }

    [Fact]
    public void OverridePrecedenceSupersedesLowerFactsWhileCompatibleTagsCoexistAtSameAuthority()
    {
        var world = World([], [Annotation(WorldScope(), Tag("terrain", "plains"))]);
        var expedition = WorldExpedition(world) with
        {
            Environment = new ExpeditionEnvironmentState
            {
                CurrentFacts = [Tag("terrain", "forest"), Tag("terrain", "hills")],
                Overrides = [Tag("terrain", "swamp")]
            }
        };

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var effective = EffectiveTags(context);

        Assert.Equal([("terrain", "swamp")], effective);
        Assert.Contains(context.Facts, value => value.Fact.Tag == "plains" && !value.Effective);
        Assert.Contains(context.Facts, value => value.Fact.Tag == "forest" && !value.Effective);
        Assert.Contains(context.Facts, value => value.Fact.Tag == "hills" && !value.Effective);
    }

    [Fact]
    public void EqualAuthorityScalarConflictIsExplicitAndOrderIndependent()
    {
        var cold = Measurement("temperature", -5, "C", id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var hot = Measurement("temperature", 30, "C", id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var procedure = Procedure("simple-fixed-distance");
        var first = AbstractExpedition(procedure) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [cold, hot] }
        };
        var second = first with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [hot, cold] }
        };

        var a = EnvironmentContextResolver.Resolve(first);
        var b = EnvironmentContextResolver.Resolve(second);

        Assert.Equal(EnvironmentContextStatus.RequiresAdjudication, a.Status);
        Assert.Equal(EnvironmentContextStatus.RequiresAdjudication, b.Status);
        Assert.Equal(a.Conflicts.Single().Candidates.Select(value => value.Fact.Id),
            b.Conflicts.Single().Candidates.Select(value => value.Fact.Id));
        Assert.Equal(2, a.Conflicts.Single().Candidates.Count);
    }

    [Fact]
    public void Dnd35EnvironmentTerrainFeedsExistingMovementComposer()
    {
        var walker = Member("Walker");
        var expedition = AbstractExpedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), new CrawlPartySheet
        {
            Members = [walker],
            MovementContributors = [Participant(walker, 3)]
        }) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [Tag("terrain", "difficult")] }
        };

        var evaluation = Evaluate(expedition);
        var movement = MovementCapabilityComposer.Compose(expedition, evaluation.MovementInput);

        Assert.Equal("difficult", evaluation.TerrainKey);
        Assert.Equal(MovementCompositionStatus.Resolved, movement.Status);
        Assert.Equal(1.5, movement.EffectiveValue);
        Assert.Equal(1.5, movement.SuggestedExpectedDistance?.Value);
    }

    [Fact]
    public void Dnd2024ArcticComesFromEnvironmentButDoesNotFabricateEquipmentCapability()
    {
        var walker = Member("Walker");
        var expedition = AbstractExpedition(Procedure(CrawlProcedureCatalog.Dnd2024PresetKey), new CrawlPartySheet
        {
            Members = [walker],
            MovementContributors = [Participant(walker, 3)]
        }) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [Tag("terrain", "arctic")] }
        };

        var evaluation = Evaluate(expedition);
        var movement = MovementCapabilityComposer.Compose(
            expedition,
            evaluation.MovementInput with { PaceKey = "fast" });

        Assert.Equal("arctic", evaluation.TerrainKey);
        Assert.Equal(MovementCompositionStatus.RequiresAdjudication, movement.Status);
        Assert.Contains(movement.Contributors, value => value.SymbolicValue == "fast-if-appropriately-equipped");
        Assert.DoesNotContain(evaluation.Provenance, value =>
            value.Contains("equipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnvironmentPreservesNonDistanceMovementSemanticsAcrossProofProcedures()
    {
        var pathfinder = WithTerrain(CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey, "difficult");
        var forbidden = WithTerrain(CrawlProcedureCatalog.ForbiddenLandsPresetKey, "difficult");
        var oneRing = WithTerrain(CrawlProcedureCatalog.OneRing2ePresetKey, "forest");

        var pfEval = Evaluate(pathfinder);
        var pfMove = MovementCapabilityComposer.Compose(pathfinder, pfEval.MovementInput);
        var flEval = Evaluate(forbidden);
        var flMove = MovementCapabilityComposer.Compose(forbidden, flEval.MovementInput);
        var orEval = Evaluate(oneRing);
        var orMove = MovementCapabilityComposer.Compose(oneRing, orEval.MovementInput);

        Assert.Equal("hexploration-activity", pfMove.Policy.BudgetUnit);
        Assert.Null(pfMove.SuggestedExpectedDistance);
        Assert.Equal("quarter-day", flMove.EffectiveUnit);
        Assert.Null(flMove.SuggestedExpectedDistance);
        Assert.Equal("journey-progress", orMove.Policy.BudgetModel);
        Assert.Null(orMove.SuggestedExpectedDistance);
        Assert.DoesNotContain(oneRing.CampaignProcedure.Modules,
            value => value.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
    }

    [Fact]
    public void WorldsWithoutNumberEnvironmentCanDriveNumericRateWithoutPresetDispatch()
    {
        var baseRate = new MovementCapabilityContributor
        {
            Id = Guid.NewGuid(),
            Kind = MovementCapabilityContributorKind.Environment,
            Key = "manual-party-rate",
            Operation = MovementCapabilityOperation.Base,
            Scope = MovementCapabilityScope.Party,
            Value = 4,
            Unit = "mi",
            PerUnit = "hour",
            DistanceUnit = DistanceUnit.Miles,
            Provenance = "manual party capability"
        };
        var expedition = AbstractExpedition(Procedure(CrawlProcedureCatalog.WorldsWithoutNumberPresetKey),
            new CrawlPartySheet { MovementContributors = [baseRate] }) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [Tag("terrain", "rough")] }
        };

        var evaluation = Evaluate(expedition);
        var movement = MovementCapabilityComposer.Compose(expedition, evaluation.MovementInput);

        Assert.Equal("rough", evaluation.TerrainKey);
        Assert.Equal(2, movement.EffectiveValue);
        Assert.Equal(20, movement.SuggestedExpectedDistance?.Value);
    }

    [Fact]
    public void MultipleMateriallyDifferentTerrainTagsRequireAdjudicationRatherThanFirstOrWorst()
    {
        var expedition = AbstractExpedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey)) with
        {
            Environment = new ExpeditionEnvironmentState
            {
                CurrentFacts = [Tag("terrain", "difficult"), Tag("terrain", "trackless")]
            }
        };

        var evaluation = Evaluate(expedition);

        Assert.Null(evaluation.TerrainKey);
        Assert.Equal(EnvironmentProcedureEvaluationStatus.RequiresAdjudication, evaluation.Status);
        Assert.Contains(evaluation.MovementInput.ResolvedContributors!, value =>
            value.Kind == MovementCapabilityContributorKind.Environment
            && value.Operation == MovementCapabilityOperation.SymbolicLimit
            && value.SymbolicValue == "terrain-ambiguity");
    }

    [Fact]
    public void UnknownEnvironmentValueRemainsValidWhenPinnedProcedureDoesNotInterpretIt()
    {
        var expedition = AbstractExpedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey)) with
        {
            Environment = new ExpeditionEnvironmentState
            {
                CurrentFacts = [Tag("terrain", "crystal-desert"), Tag("hazard", "singing-stones")]
            }
        };

        var context = EnvironmentContextResolver.Resolve(expedition);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);

        Assert.Equal(EnvironmentContextStatus.Resolved, context.Status);
        Assert.Contains(("terrain", "crystal-desert"), EffectiveTags(context));
        Assert.Contains(("hazard", "singing-stones"), EffectiveTags(context));
        Assert.Contains(evaluation.UnsupportedSemantics, value => value.Contains("crystal-desert", StringComparison.Ordinal));
    }

    [Fact]
    public void WeatherRemainsManualWhenPinnedGenericContractDoesNotDefineFormula()
    {
        var expedition = AbstractExpedition(Procedure(CrawlProcedureCatalog.Dnd2024PresetKey)) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [Tag("weather", "blizzard")] }
        };

        var evaluation = Evaluate(expedition);

        Assert.Equal(EnvironmentProcedureEvaluationStatus.RequiresAdjudication, evaluation.Status);
        Assert.Contains(evaluation.Diagnostics, value => value.Contains("no weather formula was invented", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evaluation.MovementInput.ResolvedContributors!, value =>
            value.Kind == MovementCapabilityContributorKind.Environment
            && value.Operation == MovementCapabilityOperation.SymbolicLimit);
    }

    private static EnvironmentProcedureEvaluation Evaluate(StoredExpedition expedition) =>
        EnvironmentProcedureEvaluator.Evaluate(expedition, EnvironmentContextResolver.Resolve(expedition));

    private static StoredExpedition WithTerrain(string presetKey, string terrain) =>
        AbstractExpedition(Procedure(presetKey)) with
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = [Tag("terrain", terrain)] }
        };

    private static IReadOnlyList<(string, string)> EffectiveTags(EffectiveEnvironmentContext context) =>
        context.Facts.Where(value => value.Effective && value.Fact.ValueKind == EnvironmentValueKind.Tag)
            .Select(value => (value.Fact.Dimension, value.Fact.Tag!))
            .OrderBy(value => value.Dimension, StringComparer.Ordinal)
            .ThenBy(value => value.Item2, StringComparer.Ordinal)
            .ToArray();

    private static EnvironmentFact Tag(string dimension, string value, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Dimension = dimension, ValueKind = EnvironmentValueKind.Tag, Tag = value
    };

    private static EnvironmentFact Measurement(string dimension, double value, string unit, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Dimension = dimension, ValueKind = EnvironmentValueKind.Measurement,
        Measurement = new EnvironmentMeasurement(value, unit)
    };

    private static EnvironmentAnnotation Annotation(EnvironmentAnnotationScope scope, params EnvironmentFact[] facts) => new()
    {
        Id = Guid.NewGuid(), Scope = scope, Facts = facts
    };

    private static EnvironmentAnnotationScope WorldScope() => new() { Kind = EnvironmentAnnotationScopeKind.World };
    private static EnvironmentAnnotationScope HexScope(int q, int r) => new()
    {
        Kind = EnvironmentAnnotationScopeKind.Hex, Hex = new HexCoordinate(q, r)
    };
    private static EnvironmentAnnotationScope FeatureScope(Guid id) => new()
    {
        Kind = EnvironmentAnnotationScopeKind.SpatialFeature, FeatureId = id
    };

    private static OverworldDefinition World(IReadOnlyList<SpatialFeature> features, IReadOnlyList<EnvironmentAnnotation> annotations) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Environment test world",
        Grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(),
            Orientation = HexOrientation.PointyTop,
            Origin = new WorldPoint(0, 0),
            RotationDegrees = 0,
            HexRadiusWorldUnits = 1,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        },
        Features = features,
        EnvironmentAnnotations = annotations
    };

    private static StoredExpedition WorldExpedition(OverworldDefinition world, ExpeditionState? state = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredExpedition("World environment test", state ?? State(0, 0),
            new WorldBoundCrawlSessionContext(world.Id), null, Procedure(CrawlProcedureCatalog.Dnd35PresetKey),
            null, TimeSpan.FromHours(1), "owner", 1, now, now);
    }

    private static StoredExpedition AbstractExpedition(CampaignProcedure procedure, CrawlPartySheet? party = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredExpedition("Abstract environment test", State(0, 0),
            new AbstractHexCrawlSessionContext("Abstract", HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
            null, procedure, null, TimeSpan.FromHours(1), "owner", 1, now, now)
        { Party = party ?? CrawlPartySheet.Empty };
    }

    private static StoredExpedition NonSpatialExpedition(CampaignProcedure procedure)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredExpedition("Nonspatial environment test", new NonSpatialSessionState { Id = Guid.NewGuid() },
            new NonSpatialCrawlSessionContext("Journey"), null, procedure, null, TimeSpan.Zero,
            "owner", 1, now, now);
    }

    private static ExpeditionState State(int q, int r) => new()
    {
        Id = Guid.NewGuid(),
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(q, r), DistanceUnit.Miles),
        DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
    };

    private static CampaignProcedure Procedure(string presetKey) =>
        CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;

    private static CrawlPartyMember Member(string name) => new(Guid.NewGuid(), name);

    private static MovementCapabilityContributor Participant(CrawlPartyMember member, double value) => new()
    {
        Id = Guid.NewGuid(), Kind = MovementCapabilityContributorKind.Participant,
        Key = member.Name.ToLowerInvariant(), Operation = MovementCapabilityOperation.Base,
        Scope = MovementCapabilityScope.Participant, Value = value, Unit = "mi", PerUnit = "hour",
        DistanceUnit = DistanceUnit.Miles, ParticipantId = member.Id, Provenance = "manual participant capability"
    };
}
