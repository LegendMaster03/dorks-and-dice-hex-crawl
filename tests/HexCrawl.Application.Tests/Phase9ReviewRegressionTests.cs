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
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(),
            Name = "No terrain policy world",
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
        var procedure = CrawlProcedureCatalog.Resolve("alexandrian-advanced")
            .MaterializeGeneric().Procedure;
        var expedition = new StoredExpedition(
            "No terrain policy expedition",
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
            Environment = new ExpeditionEnvironmentState
            {
                CurrentFacts = [Tag(dimension, value)]
            }
        };

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);

        Assert.Equal(EnvironmentProcedureEvaluationStatus.RequiresAdjudication, evaluation.Status);
        Assert.Contains(evaluation.MovementInput.ResolvedContributors!, contributor =>
            contributor.Kind == MovementCapabilityContributorKind.Environment
            && contributor.Operation == MovementCapabilityOperation.SymbolicLimit
            && contributor.SymbolicValue == symbolicValue);
    }

    private static EnvironmentFact Tag(string dimension, string tag) => new()
    {
        Id = Guid.NewGuid(),
        Dimension = dimension,
        ValueKind = EnvironmentValueKind.Tag,
        Tag = tag
    };
}
