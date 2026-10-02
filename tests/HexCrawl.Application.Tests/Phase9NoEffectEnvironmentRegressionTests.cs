using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class Phase9NoEffectEnvironmentRegressionTests
{
    [Fact]
    public void RouteAndWeatherConflictsDoNotBlockMovementWhenPinnedModelsExplicitlyHaveNoEffect()
    {
        var baseline = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey)
            .MaterializeGeneric().Procedure;
        var procedure = baseline with
        {
            Modules = baseline.Modules.Select(module =>
            {
                if (module.Module.Key != GenericProcedureCatalog.TerrainMovementModule)
                {
                    return module;
                }

                return module with
                {
                    Parameters = new Dictionary<string, string>(module.Parameters, StringComparer.Ordinal)
                    {
                        ["routeAdjustmentModel"] = "none",
                        ["weatherAdjustmentModel"] = "none"
                    }
                };
            }).ToArray()
        };
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "No-effect environment regression",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
            },
            new AbstractHexCrawlSessionContext(
                "No-effect environment regression",
                HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
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
                CurrentFacts =
                [
                    Tag(EnvironmentDimensions.Terrain, "normal"),
                    Tag(EnvironmentDimensions.Route, "road"),
                    Measurement(EnvironmentDimensions.Route, 2, "custom-route-unit"),
                    Tag(EnvironmentDimensions.Weather, "rain"),
                    Measurement(EnvironmentDimensions.Weather, 12, "mm-per-hour")
                ]
            }
        };

        var context = EnvironmentContextResolver.Resolve(expedition);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);

        Assert.Equal(EnvironmentContextStatus.RequiresAdjudication, context.Status);
        Assert.Equal(EnvironmentProcedureEvaluationStatus.Resolved, evaluation.Status);
        Assert.Equal("normal", evaluation.TerrainKey);
        Assert.Null(evaluation.RouteKey);
        Assert.Empty(evaluation.UnsupportedSemantics);
        Assert.DoesNotContain(
            evaluation.MovementInput.ResolvedContributors ?? [],
            contributor => contributor.Operation == MovementCapabilityOperation.SymbolicLimit);
        Assert.Contains(evaluation.Diagnostics, value =>
            value.Contains("route adjustment model is 'none'", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(evaluation.Diagnostics, value =>
            value.Contains("weather adjustment model is 'none'", StringComparison.OrdinalIgnoreCase));
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
