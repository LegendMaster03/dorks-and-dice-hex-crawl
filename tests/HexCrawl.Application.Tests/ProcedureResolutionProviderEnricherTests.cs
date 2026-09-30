using HexCrawl.Application.Persistence;
using HexCrawl.Application.Rules;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureResolutionProviderEnricherTests
{
    [Fact]
    public async Task ExplicitDmValuesBypassProviderResolution()
    {
        var enricher = new ProcedureResolutionProviderEnricher(new TravelEnvironmentProviderRegistry([]));
        var command = new ProcedureResolutionHelperCommand
        {
            ExpectedVersion = 3,
            ExpectedDistance = 7,
            NavigationDifficultyClass = 13,
            TravelDistanceRule = "walk",
            BaseSpeedFeet = 30,
            NavigationRiskFactors = ["forest"]
        };

        var prepared = await enricher.PrepareAsync(Expedition(), command);

        Assert.Same(command, prepared);
    }

    [Fact]
    public async Task NativeProcedureResolutionExecutesWithoutAnyProvider()
    {
        var enricher = new ProcedureResolutionProviderEnricher(new TravelEnvironmentProviderRegistry([]));
        var command = new ProcedureResolutionHelperCommand
        {
            ExpectedVersion = 3,
            NavigationDifficultyClass = 10,
            NavigationModifier = 0,
            FailureVeerSteps = 1,
            NavigationRiskFactors = ["forest"]
        };

        var prepared = await enricher.PrepareAsync(Expedition(), command);
        var resolver = new ProcedureResolutionResolver(new SequenceRandomSource(20));
        var result = resolver.Resolve(
            GenericProcedureRuntime.Bind(NavigationOnlyProcedure()),
            Context(),
            State(),
            3,
            prepared);

        var navigation = Assert.IsType<ProcedureResolvedNavigation>(result.Navigation);
        Assert.Equal(NavigationCheckOutcome.Succeeded, navigation.Outcome);
    }

    [Fact]
    public async Task MissingProviderIsUnresolvedAndExplicitDmFallbackRemainsUsable()
    {
        var enricher = new ProcedureResolutionProviderEnricher(new TravelEnvironmentProviderRegistry([]));
        var providerRequest = new ProcedureResolutionHelperCommand
        {
            ExpectedVersion = 3,
            TravelDistanceRule = "walk",
            BaseSpeedFeet = 30
        };

        var exception = await Assert.ThrowsAsync<OptionalProviderResolutionException>(() =>
            enricher.PrepareAsync(Expedition(), providerRequest));

        Assert.Equal(TravelEnvironmentProviderResolutionStates.Unavailable, exception.Status);
        Assert.Equal(TravelEnvironmentMechanicKeys.WalkDistance, exception.MechanicKey);

        var manual = providerRequest with { ExpectedDistance = 6 };
        var prepared = await enricher.PrepareAsync(Expedition(), manual);
        Assert.Same(manual, prepared);
    }

    [Fact]
    public async Task WalkDistanceAndTerrainFactorUseCampaignScope()
    {
        var campaignId = Guid.NewGuid();
        var provider = FakeProvider.WithResolved(
            TravelEnvironmentMechanicKeys.WalkDistance,
            Evaluation(
                TravelEnvironmentMechanicKeys.WalkDistance,
                quantity: new TravelEnvironmentQuantity(3, "miles", "hour")));
        provider.Catalog = CatalogResult(
            campaignId,
            Mechanic(
                TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                factorSemantic: "distance-multiplier"));
        provider.Resolutions[TravelEnvironmentMechanicKeys.TerrainDistanceFactor] = Resolved(
            TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
            Evaluation(TravelEnvironmentMechanicKeys.TerrainDistanceFactor, factor: 0.5m),
            provider.Metadata);
        var enricher = Enricher(provider);

        var prepared = await enricher.PrepareAsync(
            Expedition(campaignId),
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                TravelDistanceRule = "walk",
                BaseSpeedFeet = 30,
                Terrain = "forest",
                Route = "trackless"
            });

        Assert.Equal(6d, prepared.ExpectedDistance);
        Assert.All(provider.ResolveCalls, call => Assert.Equal(campaignId, call.CampaignId));
        Assert.Single(provider.CatalogCalls);
        Assert.Equal(campaignId, provider.CatalogCalls[0]);
    }

    [Fact]
    public async Task GlobalProviderResolutionPassesNullCampaignScope()
    {
        var provider = FakeProvider.WithResolved(
            TravelEnvironmentMechanicKeys.WalkDistance,
            Evaluation(
                TravelEnvironmentMechanicKeys.WalkDistance,
                quantity: new TravelEnvironmentQuantity(3, "miles", "hour")));

        var prepared = await Enricher(provider).PrepareAsync(
            Expedition(),
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                TravelDistanceRule = "walk",
                BaseSpeedFeet = 30
            });

        Assert.Equal(12d, prepared.ExpectedDistance);
        Assert.Single(provider.ResolveCalls);
        Assert.Null(provider.ResolveCalls[0].CampaignId);
    }

    [Fact]
    public async Task NavigationDcPreservesProviderProvenance()
    {
        var provider = FakeProvider.WithResolved(
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            Evaluation(
                TravelEnvironmentMechanicKeys.AvoidGettingLost,
                check: new TravelEnvironmentCheckResolution(
                    15,
                    null,
                    "skill.survival",
                    "once-per-hour-or-portion",
                    "become-lost")),
            new TravelEnvironmentProviderMetadata("rules-core", "Rules Core", true));
        var prepared = await Enricher(provider).PrepareAsync(
            Expedition(),
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                NavigationRiskFactors = ["forest"],
                NavigationModifier = 0,
                FailureVeerSteps = 1
            });

        Assert.Equal(15, prepared.NavigationDifficultyClass);
        var resolver = new ProcedureResolutionResolver(new SequenceRandomSource(20));
        var result = resolver.Resolve(
            GenericProcedureRuntime.Bind(NavigationOnlyProcedure()),
            Context(),
            State(),
            3,
            prepared);
        var navigation = Assert.IsType<ProcedureResolvedNavigation>(result.Navigation);
        Assert.Contains("Provider: Rules Core", navigation.Provenance.Note!);
    }

    [Fact]
    public async Task InputRequiredPreservesMissingInputKeys()
    {
        var provider = FakeProvider.WithStatus(
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            TravelEnvironmentProviderResolutionStates.InputRequired,
            ["risk-factors"]);

        var exception = await Assert.ThrowsAsync<OptionalProviderResolutionException>(() =>
            Enricher(provider).PrepareAsync(
                Expedition(),
                new ProcedureResolutionHelperCommand
                {
                    ExpectedVersion = 3,
                    NavigationRiskFactors = ["forest"]
                }));

        Assert.Equal(TravelEnvironmentProviderResolutionStates.InputRequired, exception.Status);
        Assert.Collection(exception.MissingInputKeys, key => Assert.Equal("risk-factors", key));
    }

    [Theory]
    [InlineData(TravelEnvironmentProviderResolutionStates.NotApplicable)]
    [InlineData(TravelEnvironmentProviderResolutionStates.RequiresAdjudication)]
    [InlineData(TravelEnvironmentProviderResolutionStates.Unsupported)]
    [InlineData(TravelEnvironmentProviderResolutionStates.Failed)]
    public async Task ProviderResolutionStatesRemainDistinct(string status)
    {
        var provider = FakeProvider.WithStatus(
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            status);

        var exception = await Assert.ThrowsAsync<OptionalProviderResolutionException>(() =>
            Enricher(provider).PrepareAsync(
                Expedition(),
                new ProcedureResolutionHelperCommand
                {
                    ExpectedVersion = 3,
                    NavigationRiskFactors = ["forest"]
                }));

        Assert.Equal(status, exception.Status);
    }

    [Fact]
    public async Task UnsupportedDistanceUnitIsRejectedRatherThanGuessed()
    {
        var provider = FakeProvider.WithResolved(
            TravelEnvironmentMechanicKeys.WalkDistance,
            Evaluation(
                TravelEnvironmentMechanicKeys.WalkDistance,
                quantity: new TravelEnvironmentQuantity(3, "leagues", "hour")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Enricher(provider).PrepareAsync(
                Expedition(),
                new ProcedureResolutionHelperCommand
                {
                    ExpectedVersion = 3,
                    TravelDistanceRule = "walk",
                    BaseSpeedFeet = 30
                }));

        Assert.Contains("unsupported travel distance unit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DistanceMustRetainHourlySemantic()
    {
        var provider = FakeProvider.WithResolved(
            TravelEnvironmentMechanicKeys.WalkDistance,
            Evaluation(
                TravelEnvironmentMechanicKeys.WalkDistance,
                quantity: new TravelEnvironmentQuantity(24, "miles", "day")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Enricher(provider).PrepareAsync(
                Expedition(),
                new ProcedureResolutionHelperCommand
                {
                    ExpectedVersion = 3,
                    TravelDistanceRule = "walk",
                    BaseSpeedFeet = 30
                }));

        Assert.Contains("hourly", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerrainFactorMustRetainDistanceMultiplierSemantic()
    {
        var provider = FakeProvider.WithResolved(
            TravelEnvironmentMechanicKeys.WalkDistance,
            Evaluation(
                TravelEnvironmentMechanicKeys.WalkDistance,
                quantity: new TravelEnvironmentQuantity(3, "miles", "hour")));
        provider.Catalog = CatalogResult(
            null,
            Mechanic(
                TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                factorSemantic: "movement-cost-multiplier"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Enricher(provider).PrepareAsync(
                Expedition(),
                new ProcedureResolutionHelperCommand
                {
                    ExpectedVersion = 3,
                    TravelDistanceRule = "walk",
                    BaseSpeedFeet = 30,
                    Terrain = "forest",
                    Route = "trackless"
                }));

        Assert.Contains("distance-multiplier", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(provider.ResolveCalls, call =>
            call.MechanicKey == TravelEnvironmentMechanicKeys.TerrainDistanceFactor);
    }

    [Fact]
    public async Task NegativeTerrainFactorIsRejected()
    {
        var provider = FakeProvider.WithResolved(
            TravelEnvironmentMechanicKeys.WalkDistance,
            Evaluation(
                TravelEnvironmentMechanicKeys.WalkDistance,
                quantity: new TravelEnvironmentQuantity(3, "miles", "hour")));
        provider.Catalog = CatalogResult(
            null,
            Mechanic(
                TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                factorSemantic: "distance-multiplier"));
        provider.Resolutions[TravelEnvironmentMechanicKeys.TerrainDistanceFactor] = Resolved(
            TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
            Evaluation(TravelEnvironmentMechanicKeys.TerrainDistanceFactor, factor: -0.5m),
            provider.Metadata);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Enricher(provider).PrepareAsync(
                Expedition(),
                new ProcedureResolutionHelperCommand
                {
                    ExpectedVersion = 3,
                    TravelDistanceRule = "walk",
                    BaseSpeedFeet = 30,
                    Terrain = "forest",
                    Route = "trackless"
                }));

        Assert.Contains("negative terrain distance factor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistryUsesExplicitOrUniqueDefaultProviderWithoutVendorBranching()
    {
        var first = new FakeProvider(new("alpha", "Alpha"));
        var second = new FakeProvider(new("beta", "Beta", true));
        var registry = new TravelEnvironmentProviderRegistry([first, second]);

        Assert.Same(second, registry.Select().Provider);
        Assert.Same(first, registry.Select("alpha").Provider);
        Assert.Null(registry.Select("missing").Provider);
    }

    [Fact]
    public async Task CampaignScopeRoundTripsWithCampaignProcedureSnapshot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var campaignId = Guid.NewGuid();
        var expedition = Expedition(campaignId);

        await store.CreateExpeditionAsync(expedition);
        var loaded = await store.GetExpeditionAsync(expedition.Id, expedition.OwnerUserId);

        Assert.NotNull(loaded);
        Assert.Equal(campaignId, loaded!.CampaignId);
        Assert.Equal(expedition.CampaignProcedure, loaded.CampaignProcedure);
    }

    private static ProcedureResolutionProviderEnricher Enricher(FakeProvider provider) =>
        new(new TravelEnvironmentProviderRegistry([provider]));

    private static StoredExpedition Expedition(Guid? campaignId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var procedure = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure;
        return new StoredExpedition(
            "Provider test",
            State(),
            Context(),
            null,
            procedure,
            null,
            GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration,
            "owner",
            3,
            now,
            now)
        {
            CampaignId = campaignId
        };
    }

    private static CampaignProcedure NavigationOnlyProcedure()
    {
        var baseline = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure;
        return baseline with
        {
            Modules = baseline.Modules.Select(module =>
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
                    return module with
                    {
                        Parameters = new Dictionary<string, string>(module.Parameters, StringComparer.Ordinal)
                        {
                            ["travel.enabled"] = "false",
                            ["navigation.enabled"] = "true",
                            ["encounter.enabled"] = "false"
                        }
                    };
                }
                return module;
            }).ToArray()
        };
    }

    private static AbstractHexCrawlSessionContext Context() => new(
        "Provider test",
        HexOrientation.PointyTop,
        new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));

    private static ExpeditionState State() => new()
    {
        Id = Guid.NewGuid(),
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
    };

    private static TravelEnvironmentProviderCatalogResult CatalogResult(
        Guid? campaignId,
        params TravelEnvironmentMechanicView[] mechanics) => new(
            new TravelEnvironmentProviderMetadata("fake", "Fake Provider", true),
            TravelEnvironmentProviderAvailabilityStates.Available,
            new TravelEnvironmentCatalogView(
                campaignId.HasValue ? "campaign" : "global",
                campaignId,
                campaignId.HasValue ? 1 : null,
                campaignId.HasValue ? DateTimeOffset.UtcNow : null,
                mechanics));

    private static TravelEnvironmentMechanicView Mechanic(
        string key,
        string state = TravelEnvironmentMechanicStates.Resolved,
        bool canResolve = true,
        string? factorSemantic = null) => new(
            key,
            state,
            canResolve,
            new TravelEnvironmentMechanicDefinition(
                key,
                "test",
                key,
                "test",
                [],
                FactorSemantic: factorSemantic),
            []);

    private static TravelEnvironmentEvaluationView Evaluation(
        string key,
        string mechanicState = TravelEnvironmentMechanicStates.Resolved,
        string evaluationState = TravelEnvironmentEvaluationStates.Resolved,
        TravelEnvironmentQuantity? quantity = null,
        decimal? factor = null,
        TravelEnvironmentCheckResolution? check = null,
        IReadOnlyList<string>? missingInputs = null) => new(
            key,
            mechanicState,
            evaluationState,
            quantity,
            factor,
            check,
            missingInputs ?? [],
            []);

    private static TravelEnvironmentProviderResolutionResult Resolved(
        string key,
        TravelEnvironmentEvaluationView evaluation,
        TravelEnvironmentProviderMetadata metadata) => new(
            metadata,
            key,
            TravelEnvironmentProviderResolutionStates.Resolved,
            evaluation,
            evaluation.MissingInputKeys);

    private sealed class FakeProvider : ITravelEnvironmentProvider
    {
        public FakeProvider(TravelEnvironmentProviderMetadata? metadata = null)
        {
            Metadata = metadata ?? new TravelEnvironmentProviderMetadata("fake", "Fake Provider", true);
            Catalog = new TravelEnvironmentProviderCatalogResult(
                Metadata,
                TravelEnvironmentProviderAvailabilityStates.Available,
                new TravelEnvironmentCatalogView("global", null, null, null, []));
        }

        public TravelEnvironmentProviderMetadata Metadata { get; }
        public TravelEnvironmentProviderCatalogResult Catalog { get; set; }
        public Dictionary<string, TravelEnvironmentProviderResolutionResult> Resolutions { get; } = new(StringComparer.Ordinal);
        public List<Guid?> CatalogCalls { get; } = [];
        public List<(Guid? CampaignId, string MechanicKey, TravelEnvironmentResolutionRequest Request)> ResolveCalls { get; } = [];

        public static FakeProvider WithResolved(
            string key,
            TravelEnvironmentEvaluationView evaluation,
            TravelEnvironmentProviderMetadata? metadata = null)
        {
            var provider = new FakeProvider(metadata);
            provider.Resolutions[key] = Resolved(key, evaluation, provider.Metadata);
            return provider;
        }

        public static FakeProvider WithStatus(
            string key,
            string status,
            IReadOnlyList<string>? missingInputs = null)
        {
            var provider = new FakeProvider();
            provider.Resolutions[key] = new TravelEnvironmentProviderResolutionResult(
                provider.Metadata,
                key,
                status,
                null,
                missingInputs ?? []);
            return provider;
        }

        public Task<TravelEnvironmentProviderCatalogResult> GetCatalogAsync(
            Guid? campaignId,
            CancellationToken cancellationToken = default)
        {
            CatalogCalls.Add(campaignId);
            return Task.FromResult(Catalog with
            {
                Provider = Metadata,
                Catalog = Catalog.Catalog is null
                    ? null
                    : Catalog.Catalog with { CampaignId = campaignId }
            });
        }

        public Task<TravelEnvironmentProviderResolutionResult> ResolveAsync(
            Guid? campaignId,
            string mechanicKey,
            TravelEnvironmentResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls.Add((campaignId, mechanicKey, request));
            if (Resolutions.TryGetValue(mechanicKey, out var result))
            {
                return Task.FromResult(result);
            }
            return Task.FromResult(new TravelEnvironmentProviderResolutionResult(
                Metadata,
                mechanicKey,
                TravelEnvironmentProviderResolutionStates.Unsupported,
                null,
                [],
                "Capability is absent."));
        }
    }

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
