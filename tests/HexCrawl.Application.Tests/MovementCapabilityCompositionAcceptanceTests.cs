using HexCrawl.Application.Persistence;
using HexCrawl.Application.Rules;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class MovementCapabilityCompositionAcceptanceTests
{
    [Fact]
    public async Task ManualParticipantCapabilityPreemptsOptionalProvider()
    {
        var member = new CrawlPartyMember(Guid.Parse("00000000-0000-0000-0000-000000000011"), "Walker");
        var expedition = Expedition(
            CrawlProcedureCatalog.Dnd35PresetKey,
            new CrawlPartySheet
            {
                Members = [member],
                MovementContributors =
                [
                    Participant(member, 3, DistanceUnit.Miles, "manual participant capability")
                ]
            });
        var provider = new CountingProvider(ResolvedHourly(9));
        var enricher = new ProcedureResolutionProviderEnricher(new TravelEnvironmentProviderRegistry([provider]));

        var prepared = await enricher.PrepareAsync(
            expedition,
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = expedition.Version,
                TravelDistanceRule = "walk",
                BaseSpeedFeet = 30
            });

        Assert.Equal(3, prepared.ExpectedDistance);
        Assert.Equal(0, provider.ResolveCalls);
    }

    [Fact]
    public async Task UnavailableProviderFallsBackToExplicitPartyMovementReference()
    {
        var expedition = Expedition(
            "alexandrian-advanced",
            new CrawlPartySheet
            {
                BaseMovement = new PartyMovementReference
                {
                    PerHour = new DistanceMeasure(3, DistanceUnit.Miles),
                    Note = "explicit campaign movement reference"
                }
            });
        var provider = new CountingProvider(new TravelEnvironmentProviderResolutionResult(
            new TravelEnvironmentProviderMetadata("fake", "Fake provider", true),
            TravelEnvironmentMechanicKeys.WalkDistance,
            TravelEnvironmentProviderResolutionStates.Unavailable,
            null,
            [],
            "offline"));
        var enricher = new ProcedureResolutionProviderEnricher(new TravelEnvironmentProviderRegistry([provider]));

        var prepared = await enricher.PrepareAsync(
            expedition,
            new ProcedureResolutionHelperCommand
            {
                ExpectedVersion = expedition.Version,
                TravelDistanceRule = "walk",
                BaseSpeedFeet = 30
            });

        Assert.Equal(12, prepared.ExpectedDistance);
        Assert.Equal(1, provider.ResolveCalls);
    }

    [Fact]
    public void CompatiblePhysicalUnitsConvertBeforeSelectingPartyLimiter()
    {
        var miles = new CrawlPartyMember(Guid.Parse("00000000-0000-0000-0000-000000000021"), "Miles");
        var kilometers = new CrawlPartyMember(Guid.Parse("00000000-0000-0000-0000-000000000022"), "Kilometers");
        var expedition = Expedition(
            CrawlProcedureCatalog.Dnd35PresetKey,
            new CrawlPartySheet
            {
                Members = [miles, kilometers],
                MovementContributors =
                [
                    Participant(miles, 3, DistanceUnit.Miles, "miles capability"),
                    Participant(kilometers, 4, DistanceUnit.Kilometers, "kilometers capability")
                ]
            });

        var result = MovementCapabilityComposer.Compose(expedition);

        Assert.Equal(MovementCompositionStatus.Resolved, result.Status);
        Assert.Equal(kilometers.Id, result.LimitingParticipantId);
        Assert.Equal(DistanceUnit.Miles, result.EffectiveDistanceUnit);
        Assert.Equal(4d / 1.609344d, result.EffectiveValue!.Value, 8);
    }

    [Fact]
    public void UnknownContributorOperationRequiresAdjudicationInsteadOfGuessing()
    {
        var member = new CrawlPartyMember(Guid.Parse("00000000-0000-0000-0000-000000000031"), "Walker");
        var unknown = new MovementCapabilityContributor
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000032"),
            Kind = MovementCapabilityContributorKind.Load,
            Key = "future-load-operation",
            Operation = (MovementCapabilityOperation)999,
            Scope = MovementCapabilityScope.Party,
            Value = 1,
            Unit = "mi",
            PerUnit = "hour",
            DistanceUnit = DistanceUnit.Miles
        };
        var expedition = Expedition(
            CrawlProcedureCatalog.Dnd35PresetKey,
            new CrawlPartySheet
            {
                Members = [member],
                MovementContributors =
                [
                    Participant(member, 3, DistanceUnit.Miles, "manual participant capability"),
                    unknown
                ]
            });

        var result = MovementCapabilityComposer.Compose(expedition);

        Assert.Equal(MovementCompositionStatus.RequiresAdjudication, result.Status);
        Assert.Contains(result.Diagnostics, value =>
            value.Contains("not supported", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Contributors, value => value.Id == unknown.Id && value.Applied);
    }

    [Fact]
    public void UnknownPinnedTerrainSemanticRemainsUnsupported()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey)
            .MaterializeGeneric()
            .Procedure;
        procedure = procedure with
        {
            Modules = procedure.Modules.Select(module =>
                module.Module.Key == GenericProcedureCatalog.TerrainMovementModule
                    ? module with
                    {
                        Parameters = new Dictionary<string, string>(module.Parameters, StringComparer.Ordinal)
                        {
                            ["adjustmentModel"] = "future-terrain-semantic"
                        }
                    }
                    : module).ToArray()
        };
        var member = new CrawlPartyMember(Guid.Parse("00000000-0000-0000-0000-000000000041"), "Walker");
        var expedition = Expedition(
            procedure,
            new CrawlPartySheet
            {
                Members = [member],
                MovementContributors =
                [
                    Participant(member, 3, DistanceUnit.Miles, "manual participant capability")
                ]
            });

        var result = MovementCapabilityComposer.Compose(
            expedition,
            new MovementCompositionInput(TerrainKey: "difficult"));

        Assert.Equal(MovementTerrainPolicySupport.Unsupported, result.Policy.Terrain.Support);
        Assert.Equal(MovementCompositionStatus.Unsupported, result.Status);
        Assert.Contains(result.Diagnostics, value =>
            value.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(MovementCompositionStatus.Unavailable)]
    [InlineData(MovementCompositionStatus.Failed)]
    [InlineData(MovementCompositionStatus.RequiresAdjudication)]
    public void ExternalResolutionStatusRemainsDistinctWhenNoCapabilityExists(MovementCompositionStatus externalStatus)
    {
        var expedition = Expedition("simple-fixed-distance", CrawlPartySheet.Empty);

        var result = MovementCapabilityComposer.Compose(
            expedition,
            new MovementCompositionInput(
                ExternalStatus: externalStatus,
                ExternalDiagnostic: "external capability state"));

        Assert.Equal(externalStatus, result.Status);
        Assert.Contains("external capability state", result.Diagnostics);
        Assert.Null(result.SuggestedExpectedDistance);
    }

    private static TravelEnvironmentProviderResolutionResult ResolvedHourly(decimal value)
    {
        var metadata = new TravelEnvironmentProviderMetadata("fake", "Fake provider", true);
        return new TravelEnvironmentProviderResolutionResult(
            metadata,
            TravelEnvironmentMechanicKeys.WalkDistance,
            TravelEnvironmentProviderResolutionStates.Resolved,
            new TravelEnvironmentEvaluationView(
                TravelEnvironmentMechanicKeys.WalkDistance,
                TravelEnvironmentMechanicStates.Resolved,
                TravelEnvironmentEvaluationStates.Resolved,
                new TravelEnvironmentQuantity(value, "miles", "hour"),
                null,
                null,
                [],
                []),
            []);
    }

    private static MovementCapabilityContributor Participant(
        CrawlPartyMember member,
        double value,
        DistanceUnit unit,
        string provenance) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MovementCapabilityContributorKind.Participant,
        Key = $"participant:{member.Name.ToLowerInvariant()}",
        Operation = MovementCapabilityOperation.Base,
        Scope = MovementCapabilityScope.Participant,
        Value = value,
        Unit = unit.Symbol,
        PerUnit = "hour",
        DistanceUnit = unit,
        ParticipantId = member.Id,
        Provenance = provenance
    };

    private static StoredExpedition Expedition(string presetKey, CrawlPartySheet party) =>
        Expedition(CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure, party);

    private static StoredExpedition Expedition(CampaignProcedure procedure, CrawlPartySheet party)
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
            DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
        };
        return new StoredExpedition(
            "Phase 8 acceptance",
            state,
            new AbstractHexCrawlSessionContext(
                "Phase 8 acceptance",
                HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
            null,
            procedure,
            null,
            FocusedIntervalPolicyResolver.Resolve(procedure).IntervalDuration ?? TimeSpan.Zero,
            "owner",
            1,
            now,
            now)
        {
            Party = party
        };
    }

    private sealed class CountingProvider(TravelEnvironmentProviderResolutionResult result)
        : ITravelEnvironmentProvider
    {
        public TravelEnvironmentProviderMetadata Metadata { get; } =
            result.Provider ?? new TravelEnvironmentProviderMetadata("fake", "Fake provider", true);

        public int ResolveCalls { get; private set; }

        public Task<TravelEnvironmentProviderCatalogResult> GetCatalogAsync(
            Guid? campaignId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TravelEnvironmentProviderCatalogResult(
                Metadata,
                TravelEnvironmentProviderAvailabilityStates.Available,
                new TravelEnvironmentCatalogView("global", campaignId, null, null, [])));

        public Task<TravelEnvironmentProviderResolutionResult> ResolveAsync(
            Guid? campaignId,
            string mechanicKey,
            TravelEnvironmentResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            return Task.FromResult(result with { MechanicKey = mechanicKey, Provider = Metadata });
        }
    }
}
