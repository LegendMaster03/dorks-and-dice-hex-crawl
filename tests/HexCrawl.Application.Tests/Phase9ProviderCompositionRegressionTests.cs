using HexCrawl.Application.Persistence;
using HexCrawl.Application.Rules;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class Phase9ProviderCompositionRegressionTests
{
    [Fact]
    public async Task ProviderBaseCapabilityStillUsesPinnedEnvironmentMovementInput()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey)
            .MaterializeGeneric().Procedure;
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "Provider environment regression",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
            },
            new AbstractHexCrawlSessionContext(
                "Provider environment regression",
                HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
            null,
            procedure,
            null,
            GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration,
            "owner",
            3,
            now,
            now)
        {
            Environment = new ExpeditionEnvironmentState
            {
                CurrentFacts =
                [
                    new EnvironmentFact
                    {
                        Id = Guid.NewGuid(),
                        Dimension = EnvironmentDimensions.Terrain,
                        ValueKind = EnvironmentValueKind.Tag,
                        Tag = "difficult",
                        Provenance = "test terrain"
                    }
                ]
            }
        };
        var context = EnvironmentContextResolver.Resolve(expedition);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);
        Assert.Equal(EnvironmentProcedureEvaluationStatus.Resolved, evaluation.Status);
        Assert.Equal("difficult", evaluation.TerrainKey);

        var provider = new BaseRateProvider();
        var enricher = new ProcedureResolutionProviderEnricher(
            new TravelEnvironmentProviderRegistry([provider]));
        var prepared = await enricher.PrepareAsync(
            expedition,
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = expedition.Version,
                TravelDistanceRule = "walk",
                BaseSpeedFeet = 30
            },
            evaluation.MovementInput);

        Assert.Equal(1.5d, prepared.ExpectedDistance);
        Assert.Equal([TravelEnvironmentMechanicKeys.WalkDistance], provider.ResolveCalls);
    }

    private sealed class BaseRateProvider : ITravelEnvironmentProvider
    {
        public TravelEnvironmentProviderMetadata Metadata { get; } =
            new("phase9-test", "Phase 9 Test Provider", true);

        public List<string> ResolveCalls { get; } = [];

        public Task<TravelEnvironmentProviderCatalogResult> GetCatalogAsync(
            Guid? campaignId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TravelEnvironmentProviderCatalogResult(
                Metadata,
                TravelEnvironmentProviderAvailabilityStates.Available,
                new TravelEnvironmentCatalogView("global", null, null, null, [])));

        public Task<TravelEnvironmentProviderResolutionResult> ResolveAsync(
            Guid? campaignId,
            string mechanicKey,
            TravelEnvironmentResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls.Add(mechanicKey);
            if (string.Equals(mechanicKey, TravelEnvironmentMechanicKeys.WalkDistance, StringComparison.Ordinal))
            {
                var evaluation = new TravelEnvironmentEvaluationView(
                    mechanicKey,
                    TravelEnvironmentMechanicStates.Resolved,
                    TravelEnvironmentEvaluationStates.Resolved,
                    new TravelEnvironmentQuantity(3, "miles", "hour"),
                    null,
                    null,
                    [],
                    []);
                return Task.FromResult(new TravelEnvironmentProviderResolutionResult(
                    Metadata,
                    mechanicKey,
                    TravelEnvironmentProviderResolutionStates.Resolved,
                    evaluation,
                    []));
            }

            return Task.FromResult(new TravelEnvironmentProviderResolutionResult(
                Metadata,
                mechanicKey,
                TravelEnvironmentProviderResolutionStates.Unsupported,
                null,
                []));
        }
    }
}
