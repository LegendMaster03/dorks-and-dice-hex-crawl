using HexCrawl.Application.Persistence;
using HexCrawl.Application.Rules;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureResolutionRulesCoreAdapterTests
{
    [Fact]
    public async Task ExplicitDmValuesTakePrecedenceWithoutRulesCoreCalls()
    {
        var gateway = new FakeTravelGateway();
        var adapter = new ProcedureResolutionRulesCoreAdapter(gateway);
        var command = new ProcedureResolutionHelperCommand
        {
            ExpectedVersion = 3,
            ExpectedDistance = 7,
            NavigationDifficultyClass = 13,
            TravelDistanceRule = "walk",
            BaseSpeedFeet = 30,
            NavigationRiskFactors = ["forest"]
        };

        var prepared = await adapter.PrepareAsync(Expedition(), command);

        Assert.Same(command, prepared);
        Assert.Empty(gateway.ResolveCalls);
        Assert.Empty(gateway.CatalogCalls);
    }

    [Fact]
    public async Task WalkDistanceAndTerrainFactorUseCampaignScopeWithoutDuplicatingEditionFormula()
    {
        var campaignId = Guid.NewGuid();
        var gateway = new FakeTravelGateway
        {
            Catalog = Catalog(
                campaignId,
                Mechanic(
                    TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                    factorSemantic: "distance-multiplier")),
            Resolutions =
            {
                [TravelEnvironmentMechanicKeys.WalkDistance] = Evaluation(
                    TravelEnvironmentMechanicKeys.WalkDistance,
                    quantity: new TravelEnvironmentQuantity(3, "miles", "hour")),
                [TravelEnvironmentMechanicKeys.TerrainDistanceFactor] = Evaluation(
                    TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                    factor: 0.5m)
            }
        };
        var adapter = new ProcedureResolutionRulesCoreAdapter(gateway);

        var prepared = await adapter.PrepareAsync(
            Expedition(campaignId),
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                TravelDistanceRule = "walk",
                BaseSpeedFeet = 30,
                Terrain = "forest",
                Route = "trackless"
            });

        // Alexandrian baseline watches are four hours: 3 mi/h * 4h * 0.5.
        Assert.Equal(6d, prepared.ExpectedDistance);
        Assert.All(gateway.ResolveCalls, call => Assert.Equal(campaignId, call.CampaignId));
        Assert.Single(gateway.CatalogCalls);
        Assert.Equal(campaignId, gateway.CatalogCalls[0]);
        Assert.Contains(
            gateway.ResolveCalls,
            call => call.MechanicKey == TravelEnvironmentMechanicKeys.WalkDistance);
        Assert.Contains(
            gateway.ResolveCalls,
            call => call.MechanicKey == TravelEnvironmentMechanicKeys.TerrainDistanceFactor);
    }

    [Fact]
    public async Task NavigationDcAndRulesCoreProvenanceFlowIntoAutomaticResolution()
    {
        var gateway = new FakeTravelGateway
        {
            Resolutions =
            {
                [TravelEnvironmentMechanicKeys.AvoidGettingLost] = Evaluation(
                    TravelEnvironmentMechanicKeys.AvoidGettingLost,
                    check: new TravelEnvironmentCheckResolution(
                        15,
                        null,
                        "skill.survival",
                        "once-per-hour-or-portion",
                        "become-lost"))
            }
        };
        var adapter = new ProcedureResolutionRulesCoreAdapter(gateway);
        var prepared = await adapter.PrepareAsync(
            Expedition(),
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                NavigationRiskFactors = ["forest"],
                NavigationModifier = 0,
                FailureVeerSteps = 1
            });

        Assert.Equal(15, prepared.NavigationDifficultyClass);

        var baseline = CrawlProcedureProfile.AlexandrianAdvancedBaseline();
        var navigationOnly = baseline with
        {
            EncounterCadence = EncounterCheckCadence.None,
            ResolutionHelpers = new ProcedureResolutionHelperProfile(
                Navigation: baseline.ResolutionHelpers!.Navigation)
        };
        var resolver = new ProcedureResolutionResolver(new SequenceRandomSource(20));
        var result = resolver.Resolve(
            navigationOnly,
            Context(),
            State(),
            3,
            prepared);

        var navigation = Assert.IsType<ProcedureResolvedNavigation>(result.Navigation);
        Assert.Equal(NavigationCheckOutcome.Succeeded, navigation.Outcome);
        Assert.Contains(TravelEnvironmentMechanicKeys.AvoidGettingLost, navigation.Provenance.Note!);
        Assert.Contains("once-per-hour-or-portion", navigation.Provenance.Note!);
    }

    [Fact]
    public async Task ConflictedMechanicRequiresDmAdjudicationOrOverride()
    {
        var gateway = new FakeTravelGateway
        {
            Resolutions =
            {
                [TravelEnvironmentMechanicKeys.WalkDistance] = Evaluation(
                    TravelEnvironmentMechanicKeys.WalkDistance,
                    mechanicState: TravelEnvironmentMechanicStates.Conflicted,
                    evaluationState: TravelEnvironmentEvaluationStates.Resolved)
            }
        };
        var adapter = new ProcedureResolutionRulesCoreAdapter(gateway);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.PrepareAsync(
            Expedition(),
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                TravelDistanceRule = "walk",
                BaseSpeedFeet = 30
            }));

        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DM override", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TravelEnvironmentEvaluationStates.InputRequired, "requires")]
    [InlineData(TravelEnvironmentEvaluationStates.NotApplicable, "not applicable")]
    public async Task UnresolvedEvaluationDoesNotSilentlyFallback(string evaluationState, string expectedMessage)
    {
        var gateway = new FakeTravelGateway
        {
            Resolutions =
            {
                [TravelEnvironmentMechanicKeys.AvoidGettingLost] = Evaluation(
                    TravelEnvironmentMechanicKeys.AvoidGettingLost,
                    evaluationState: evaluationState,
                    missingInputs: evaluationState == TravelEnvironmentEvaluationStates.InputRequired
                        ? ["risk-factors"]
                        : [])
            }
        };
        var adapter = new ProcedureResolutionRulesCoreAdapter(gateway);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.PrepareAsync(
            Expedition(),
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = 3,
                NavigationRiskFactors = ["forest"]
            }));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TerrainFactorMustRetainDistanceMultiplierSemantic()
    {
        var gateway = new FakeTravelGateway
        {
            Catalog = Catalog(
                null,
                Mechanic(
                    TravelEnvironmentMechanicKeys.TerrainDistanceFactor,
                    factorSemantic: "movement-cost-multiplier")),
            Resolutions =
            {
                [TravelEnvironmentMechanicKeys.WalkDistance] = Evaluation(
                    TravelEnvironmentMechanicKeys.WalkDistance,
                    quantity: new TravelEnvironmentQuantity(3, "miles", "hour"))
            }
        };
        var adapter = new ProcedureResolutionRulesCoreAdapter(gateway);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.PrepareAsync(
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
        Assert.DoesNotContain(
            gateway.ResolveCalls,
            call => call.MechanicKey == TravelEnvironmentMechanicKeys.TerrainDistanceFactor);
    }

    [Fact]
    public async Task CampaignScopeRoundTripsWithoutDatabaseSchemaMigration()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"hex-crawl-rules-scope-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteHexCrawlStore($"Data Source={databasePath}");
            await store.InitializeAsync();
            var campaignId = Guid.NewGuid();
            var expedition = Expedition(campaignId);

            await store.CreateExpeditionAsync(expedition);
            var loaded = await store.GetExpeditionAsync(expedition.Id, expedition.OwnerUserId);

            Assert.NotNull(loaded);
            Assert.Equal(campaignId, loaded.CampaignId);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private static StoredExpedition Expedition(Guid? campaignId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var profile = CrawlProcedureProfile.AlexandrianAdvancedBaseline();
        return new StoredExpedition(
            "Rules Core test",
            State(),
            Context(),
            null,
            profile,
            null,
            profile.WatchLength,
            "owner",
            3,
            now,
            now)
        {
            CampaignId = campaignId
        };
    }

    private static AbstractHexCrawlSessionContext Context() => new(
        "Rules Core test",
        HexOrientation.PointyTop,
        new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));

    private static ExpeditionState State() => new()
    {
        Id = Guid.NewGuid(),
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
    };

    private static TravelEnvironmentCatalogView Catalog(
        Guid? campaignId,
        params TravelEnvironmentMechanicView[] mechanics) => new(
            campaignId.HasValue ? "campaign" : "global",
            campaignId,
            campaignId.HasValue ? 1 : null,
            campaignId.HasValue ? DateTimeOffset.UtcNow : null,
            mechanics);

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

    private sealed class FakeTravelGateway : IRulesCoreTravelGateway
    {
        public TravelEnvironmentCatalogView Catalog { get; init; } = Catalog(null);
        public Dictionary<string, TravelEnvironmentEvaluationView> Resolutions { get; } = new(StringComparer.Ordinal);
        public List<Guid?> CatalogCalls { get; } = [];
        public List<(Guid? CampaignId, string MechanicKey, TravelEnvironmentResolutionRequest Request)> ResolveCalls { get; } = [];

        public Task<TravelEnvironmentCatalogView> GetCatalogAsync(
            Guid? campaignId,
            CancellationToken cancellationToken = default)
        {
            CatalogCalls.Add(campaignId);
            return Task.FromResult(Catalog);
        }

        public Task<TravelEnvironmentEvaluationView?> ResolveAsync(
            Guid? campaignId,
            string mechanicKey,
            TravelEnvironmentResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls.Add((campaignId, mechanicKey, request));
            Resolutions.TryGetValue(mechanicKey, out var value);
            return Task.FromResult(value);
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
