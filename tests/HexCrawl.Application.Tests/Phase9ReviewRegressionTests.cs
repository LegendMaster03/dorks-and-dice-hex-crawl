using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class Phase9ReviewRegressionTests
{
    [Fact]
    public void TrimmedDimensionControlsBothPrecedenceAndMechanicalInterpretation()
    {
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Trimmed dimension world",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(),
                Orientation = HexOrientation.PointyTop,
                Origin = new WorldPoint(0, 0),
                RotationDegrees = 0,
                HexRadiusWorldUnits = 1,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            },
            EnvironmentAnnotations =
            [
                new EnvironmentAnnotation
                {
                    Id = Guid.NewGuid(),
                    Scope = new EnvironmentAnnotationScope { Kind = EnvironmentAnnotationScopeKind.World },
                    Facts = [Tag("terrain", "normal")]
                }
            ]
        };
        var walker = new CrawlPartyMember(Guid.NewGuid(), "Walker");
        var now = DateTimeOffset.UtcNow;
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey)
            .MaterializeGeneric().Procedure;
        var expedition = new StoredExpedition(
            "Trimmed dimension expedition",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
            },
            new WorldBoundCrawlSessionContext(world.Id),
            null,
            procedure,
            null,
            TimeSpan.FromHours(1),
            "owner",
            1,
            now,
            now)
        {
            Party = new CrawlPartySheet
            {
                Members = [walker],
                MovementContributors =
                [
                    new MovementCapabilityContributor
                    {
                        Id = Guid.NewGuid(),
                        Kind = MovementCapabilityContributorKind.Participant,
                        Key = "walker",
                        Operation = MovementCapabilityOperation.Base,
                        Scope = MovementCapabilityScope.Participant,
                        Value = 3,
                        Unit = "mi",
                        PerUnit = "hour",
                        DistanceUnit = DistanceUnit.Miles,
                        ParticipantId = walker.Id,
                        Provenance = "manual participant capability"
                    }
                ]
            },
            Environment = new ExpeditionEnvironmentState
            {
                CurrentFacts = [Tag(" terrain ", "difficult")]
            }
        };

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);
        var movement = MovementCapabilityComposer.Compose(expedition, evaluation.MovementInput);

        Assert.Contains(context.Facts, value => value.Fact.Tag == "normal" && !value.Effective);
        Assert.Contains(context.Facts, value => value.Fact.Tag == "difficult" && value.Effective);
        Assert.Equal("difficult", evaluation.TerrainKey);
        Assert.Equal(MovementCompositionStatus.Resolved, movement.Status);
        Assert.Equal(1.5, movement.EffectiveValue);
    }

    [Theory]
    [InlineData("terrain", "forest", "terrain-no-policy")]
    [InlineData("route", "road", "route-no-policy")]
    public void TerrainAndRouteRequireAdjudicationWhenPinnedProcedureHasNoTerrainMechanic(
        string dimension,
        string value,
        string symbolicValue)
    {
        var (world, expedition) = ExpeditionWithEnvironment(
            "alexandrian-advanced",
            Tag(dimension, value));

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);

        Assert.Equal(EnvironmentProcedureEvaluationStatus.RequiresAdjudication, evaluation.Status);
        Assert.Contains(evaluation.MovementInput.ResolvedContributors!, contributor =>
            contributor.Kind == MovementCapabilityContributorKind.Environment
            && contributor.Operation == MovementCapabilityOperation.SymbolicLimit
            && contributor.SymbolicValue == symbolicValue);
    }

    [Theory]
    [InlineData("terrain", "terrain-measurement")]
    [InlineData("route", "route-measurement")]
    public void MeasuredTerrainAndRouteRequireAdjudicationInsteadOfBeingSilentlyIgnored(
        string dimension,
        string symbolicValue)
    {
        var (world, expedition) = ExpeditionWithEnvironment(
            CrawlProcedureCatalog.Dnd35PresetKey,
            Measurement(dimension, 2, "custom-unit"));

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);

        Assert.Equal(EnvironmentProcedureEvaluationStatus.RequiresAdjudication, evaluation.Status);
        Assert.Null(evaluation.TerrainKey);
        Assert.Null(evaluation.RouteKey);
        Assert.Contains(evaluation.MovementInput.ResolvedContributors!, contributor =>
            contributor.Kind == MovementCapabilityContributorKind.Environment
            && contributor.Operation == MovementCapabilityOperation.SymbolicLimit
            && contributor.SymbolicValue == symbolicValue);
    }

    [Fact]
    public void ConflictedRouteDoesNotLeakTagIntoMovementInput()
    {
        var (world, expedition) = ExpeditionWithEnvironment(
            CrawlProcedureCatalog.Dnd35PresetKey,
            Tag("route", "road"),
            Measurement("route", 2, "custom-unit"));

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);

        Assert.Equal(EnvironmentContextStatus.RequiresAdjudication, context.Status);
        Assert.Equal(EnvironmentProcedureEvaluationStatus.RequiresAdjudication, evaluation.Status);
        Assert.Null(evaluation.RouteKey);
        Assert.Contains(evaluation.MovementInput.ResolvedContributors!, contributor =>
            contributor.Kind == MovementCapabilityContributorKind.Environment
            && contributor.Operation == MovementCapabilityOperation.SymbolicLimit
            && contributor.SymbolicValue == "route-conflict");
    }

    private static (OverworldDefinition World, StoredExpedition Expedition) ExpeditionWithEnvironment(
        string procedureKey,
        params EnvironmentFact[] facts)
    {
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Phase 9 review world",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(),
                Orientation = HexOrientation.PointyTop,
                Origin = new WorldPoint(0, 0),
                RotationDegrees = 0,
                HexRadiusWorldUnits = 1,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            }
        };
        var now = DateTimeOffset.UtcNow;
        var procedure = CrawlProcedureCatalog.Resolve(procedureKey).MaterializeGeneric().Procedure;
        var expedition = new StoredExpedition(
            "Phase 9 review expedition",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
            },
            new WorldBoundCrawlSessionContext(world.Id),
            null,
            procedure,
            null,
            TimeSpan.FromHours(1),
            "owner",
            1,
            now,
            now)
        {
            Environment = new ExpeditionEnvironmentState { CurrentFacts = facts }
        };
        return (world, expedition);
    }

    private static EnvironmentFact Tag(string dimension, string tag) => new()
    {
        Id = Guid.NewGuid(),
        Dimension = dimension,
        ValueKind = EnvironmentValueKind.Tag,
        Tag = tag
    };

    private static EnvironmentFact Measurement(string dimension, double value, string unit) => new()
    {
        Id = Guid.NewGuid(),
        Dimension = dimension,
        ValueKind = EnvironmentValueKind.Measurement,
        Measurement = new EnvironmentMeasurement(value, unit)
    };
}
