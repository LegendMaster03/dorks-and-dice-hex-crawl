using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class Phase11SurvivalResourcesTests
{
    private static readonly ExpeditionConsequenceProvenance Dm = new(
        ExpeditionConsequenceSourceKind.Dm,
        "phase-11-test");

    [Fact]
    public void ResourcePoliciesComeFromExactPinnedProcedureAndDoNotInventRates()
    {
        var bx = Materialize(CrawlProcedureCatalog.BxPresetKey);
        var policy = Phase11ProcedurePolicyResolver.ResolveResources(bx);

        Assert.Equal(Phase11PolicySupport.Supported, policy.Support);
        Assert.Equal(["food", "water"], policy.ResourceKinds);
        Assert.Equal(ExpeditionResourceInventoryModel.Counted, policy.InventoryModel);
        Assert.Equal("fixed-per-person", policy.ConsumptionModel);
        Assert.Equal("travel-day", policy.ConsumptionInterval);
        Assert.DoesNotContain("rate", bx.Modules
            .Single(value => value.Module.Key == GenericProcedureCatalog.ResourceConsumptionModule)
            .Parameters.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleAndSupplyDieResourceProofsRemainGeneric()
    {
        var wwn = Phase11ProcedurePolicyResolver.ResolveResources(Materialize(CrawlProcedureCatalog.WorldsWithoutNumberPresetKey));
        Assert.Equal(Phase11PolicySupport.Supported, wwn.Support);
        Assert.Equal(["food", "water", "shelter", "fire"], wwn.ResourceKinds);
        Assert.Equal(ExpeditionResourceInventoryModel.Counted, wwn.InventoryModel);

        var forbidden = Phase11ProcedurePolicyResolver.ResolveResources(Materialize(CrawlProcedureCatalog.ForbiddenLandsPresetKey));
        Assert.Equal(Phase11PolicySupport.Supported, forbidden.Support);
        Assert.Equal(ExpeditionResourceInventoryModel.SupplyDie, forbidden.InventoryModel);
        Assert.Equal("usage-roll", forbidden.ConsumptionModel);
    }

    [Fact]
    public void OneRingDoesNotAcquirePhase11ResourceOrIntervalDependencies()
    {
        var procedure = Materialize(CrawlProcedureCatalog.OneRing2ePresetKey);

        Assert.Equal(Phase11PolicySupport.None, Phase11ProcedurePolicyResolver.ResolveResources(procedure).Support);
        Assert.Equal(Phase11PolicySupport.None, Phase11ProcedurePolicyResolver.ResolveForcedTravel(procedure).Support);
        Assert.DoesNotContain(procedure.Modules, value => value.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.DoesNotContain(procedure.Modules, value => value.Module.Key == GenericProcedureCatalog.ResourceConsumptionModule);
    }

    [Theory]
    [InlineData(CrawlProcedureCatalog.Dnd35PresetKey, 8d, "hours", "escalating-check", "fatigue-and-nonlethal-effect")]
    [InlineData(CrawlProcedureCatalog.Dnd2024PresetKey, 8d, "hours", "escalating-constitution-save", "exhaustion")]
    [InlineData(CrawlProcedureCatalog.ForbiddenLandsPresetKey, 2d, "quarter-days", "endurance-check", "fatigue-or-mishap")]
    public void ForcedTravelProofPoliciesRemainDeclarative(
        string presetKey,
        double limit,
        string unit,
        string checkModel,
        string consequence)
    {
        var policy = Phase11ProcedurePolicyResolver.ResolveForcedTravel(Materialize(presetKey));

        Assert.Equal(Phase11PolicySupport.Supported, policy.Support);
        Assert.Equal(limit, policy.NormalTravelLimit);
        Assert.Equal(unit, policy.LimitUnit);
        Assert.Equal(checkModel, policy.CheckModel);
        Assert.Equal(consequence, policy.FailureConsequence);
        Assert.Equal(GenericProcedureExecutionHandlers.DeclarativeContract, policy.ExecutionHandler);
    }

    [Fact]
    public void PathfinderAndForbiddenLandsUseTypedActivityBackedForageAndCampPolicies()
    {
        var pathfinder = Materialize(CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey);
        var pfForage = Phase11ProcedurePolicyResolver.ResolveForaging(pathfinder);
        var pfCamp = Phase11ProcedurePolicyResolver.ResolveCamping(pathfinder);
        Assert.True(pfForage.ActivityBacked);
        Assert.True(pfCamp.ActivityBacked);
        Assert.Equal("hexploration-activity", pfForage.TimeUnit);
        Assert.Equal("hexploration-activity", pfCamp.TimeUnit);

        var forbidden = Materialize(CrawlProcedureCatalog.ForbiddenLandsPresetKey);
        Assert.True(Phase11ProcedurePolicyResolver.ResolveForaging(forbidden).ActivityBacked);
        Assert.True(Phase11ProcedurePolicyResolver.ResolveCamping(forbidden).ActivityBacked);
    }

    [Fact]
    public void UnknownVersionAndHandlerRemainUnsupportedWithoutChangingOriginIdentity()
    {
        var procedure = Materialize(CrawlProcedureCatalog.BxPresetKey);
        var resource = procedure.Modules.Single(value => value.Module.Key == GenericProcedureCatalog.ResourceConsumptionModule);
        var badVersion = procedure with
        {
            Key = "custom-key",
            Name = "Custom name",
            Modules = procedure.Modules.Select(value => ReferenceEquals(value, resource)
                ? value with { Mechanic = value.Mechanic with { Version = 999 } }
                : value).ToArray()
        };
        Assert.Equal(Phase11PolicySupport.Unsupported, Phase11ProcedurePolicyResolver.ResolveResources(badVersion).Support);

        var badHandler = procedure with
        {
            Modules = procedure.Modules.Select(value => ReferenceEquals(value, resource)
                ? value with { Mechanic = value.Mechanic with { ExecutionHandler = "future-handler" } }
                : value).ToArray()
        };
        Assert.Equal(Phase11PolicySupport.Unsupported, Phase11ProcedurePolicyResolver.ResolveResources(badHandler).Support);
    }

    [Fact]
    public void GenericExposureModuleConsumesEnvironmentOnlyThroughExplicitPolicy()
    {
        var procedure = Materialize(CrawlProcedureCatalog.BxPresetKey) with
        {
            Modules = Materialize(CrawlProcedureCatalog.BxPresetKey).Modules.Append(
                new MaterializedProcedureModule(
                    Phase11GenericProcedureCatalog.ExposureModuleDefinition,
                    Phase11GenericProcedureCatalog.ExposureMechanicDefinition,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["dimensions"] = "temperature;elevation;weather",
                        ["evaluationModel"] = "resolved-check",
                        ["evaluationInterval"] = "travel-day",
                        ["targetScope"] = "party",
                        ["consequenceModel"] = "resolved-structured-consequence"
                    })).ToArray()
        };

        var policy = Phase11ProcedurePolicyResolver.ResolveExposure(procedure);
        Assert.Equal(Phase11PolicySupport.Supported, policy.Support);
        Assert.Equal(["temperature", "elevation", "weather"], policy.Dimensions);
        Assert.Equal(ExpeditionEffectScope.Party, policy.TargetScope);
    }

    [Fact]
    public void CountedAbstractSupplyDieAndExternalResourcesValidateWithoutBecomingCharacterInventory()
    {
        var party = CrawlPartySheet.Empty;
        var target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party);
        var resources = new ExpeditionResourceState
        {
            Resources =
            [
                new ExpeditionResource
                {
                    Id = Guid.NewGuid(), ResourceKey = "custom-food", Target = target,
                    InventoryModel = ExpeditionResourceInventoryModel.Counted, Quantity = 12.5, Unit = "custom-unit"
                },
                new ExpeditionResource
                {
                    Id = Guid.NewGuid(), ResourceKey = "morale-supply", Target = target,
                    InventoryModel = ExpeditionResourceInventoryModel.Abstract, SymbolicState = "campaign-defined-state"
                },
                new ExpeditionResource
                {
                    Id = Guid.NewGuid(), ResourceKey = "torch-supply", Target = target,
                    InventoryModel = ExpeditionResourceInventoryModel.SupplyDie, SupplyDieSides = 8
                },
                new ExpeditionResource
                {
                    Id = Guid.NewGuid(), ResourceKey = "character-owned-ammunition", Target = target,
                    InventoryModel = ExpeditionResourceInventoryModel.ExternalManual
                }
            ]
        };

        resources.Validate(party);
        Assert.False(resources.Resources[0].IsDepleted);
        Assert.False(resources.Resources[2].IsDepleted);
    }

    [Fact]
    public void InvalidResourceAggregatesAreRejectedBeforePersistence()
    {
        var target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party);
        var id = Guid.NewGuid();
        var resource = new ExpeditionResource
        {
            Id = id,
            ResourceKey = "food",
            Target = target,
            InventoryModel = ExpeditionResourceInventoryModel.Counted,
            Quantity = 1,
            Unit = "ration"
        };
        Assert.Throws<InvalidOperationException>(() => new ExpeditionResourceState
        {
            Resources = [resource, resource]
        }.Validate(CrawlPartySheet.Empty));

        Assert.Throws<InvalidOperationException>(() => (resource with { Quantity = -1 }).Validate(CrawlPartySheet.Empty));
        Assert.Throws<InvalidOperationException>(() => (resource with { SymbolicState = "low" }).Validate(CrawlPartySheet.Empty));
    }

    [Fact]
    public void Phase10PendingResourceConsequenceMutatesExactlyOnce()
    {
        var party = CrawlPartySheet.Empty;
        var target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party);
        var resource = new ExpeditionResource
        {
            Id = Guid.NewGuid(), ResourceKey = "food", Target = target,
            InventoryModel = ExpeditionResourceInventoryModel.Counted, Quantity = 5, Unit = "ration"
        };
        var consequence = ResourceConsequence(
            Guid.NewGuid(), target,
            new ResourceChangeConsequenceComponent
            {
                ResourceKey = "food", ResourceId = resource.Id,
                Operation = ResourceChangeOperation.AdjustQuantity,
                Quantity = -2, Unit = "ration"
            });
        var effects = PendingResourceState(consequence);

        var first = ResourceConsequenceConsumer.Consume(
            new ExpeditionResourceState { Resources = [resource] }, effects, party, consequence.Id, Dm);
        Assert.True(first.StateChanged);
        Assert.Equal(3, first.Resources.Resources.Single().Quantity);
        Assert.Empty(first.Effects.PendingConsequences);
        Assert.Equal(ExpeditionConsequenceStatus.Applied, first.Effects.AppliedConsequences.Single().Status);

        var retry = ResourceConsequenceConsumer.Consume(first.Resources, first.Effects, party, consequence.Id, Dm);
        Assert.False(retry.StateChanged);
        Assert.Equal(ExpeditionConsequenceStatus.AlreadyApplied, retry.Status);
        Assert.Equal(3, retry.Resources.Resources.Single().Quantity);
    }

    [Fact]
    public void ResourceShortageDoesNotClampOrResolvePendingWork()
    {
        var party = CrawlPartySheet.Empty;
        var target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party);
        var resource = new ExpeditionResource
        {
            Id = Guid.NewGuid(), ResourceKey = "water", Target = target,
            InventoryModel = ExpeditionResourceInventoryModel.Counted, Quantity = 2, Unit = "water-unit"
        };
        var consequence = ResourceConsequence(
            Guid.NewGuid(), target,
            new ResourceChangeConsequenceComponent
            {
                ResourceKey = "water", ResourceId = resource.Id,
                Operation = ResourceChangeOperation.AdjustQuantity,
                Quantity = -4, Unit = "water-unit"
            });
        var effects = PendingResourceState(consequence);

        var result = ResourceConsequenceConsumer.Consume(
            new ExpeditionResourceState { Resources = [resource] }, effects, party, consequence.Id, Dm);

        Assert.False(result.StateChanged);
        Assert.Equal(ExpeditionConsequenceStatus.RequiresAdjudication, result.Status);
        Assert.Equal(2, result.Resources.Resources.Single().Quantity);
        Assert.Single(result.Effects.PendingConsequences);
    }

    [Fact]
    public void ResolvedSupplyDieTransitionUsesGenericResourceOperation()
    {
        var party = CrawlPartySheet.Empty;
        var target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party);
        var resource = new ExpeditionResource
        {
            Id = Guid.NewGuid(), ResourceKey = "supplies", Target = target,
            InventoryModel = ExpeditionResourceInventoryModel.SupplyDie, SupplyDieSides = 8
        };
        var consequence = ResourceConsequence(
            Guid.NewGuid(), target,
            new ResourceChangeConsequenceComponent
            {
                ResourceKey = "supplies", ResourceId = resource.Id,
                Operation = ResourceChangeOperation.SetSupplyDie, SupplyDieSides = 6
            });

        var result = ResourceConsequenceConsumer.Consume(
            new ExpeditionResourceState { Resources = [resource] }, PendingResourceState(consequence), party, consequence.Id, Dm);

        Assert.Equal(6, result.Resources.Resources.Single().SupplyDieSides);
    }

    [Fact]
    public void ForcedTravelThresholdCheckAndRetryAreDeterministic()
    {
        var policy = ForcedPolicy(8, "hours");
        var party = CrawlPartySheet.Empty;
        var first = Phase11SurvivalEngine.AccountForcedTravel(
            ExpeditionSurvivalState.Empty, policy, party, 8, "hours", "travel-1", Dm);
        Assert.True(first.StateChanged);
        Assert.Equal(8, first.State.ForcedTravel.AmountSinceReset);
        Assert.Null(first.State.ForcedTravel.PendingCheck);

        var beyond = Phase11SurvivalEngine.AccountForcedTravel(
            first.State, policy, party, 1, "hours", "travel-2", Dm);
        Assert.NotNull(beyond.State.ForcedTravel.PendingCheck);
        Assert.Equal(9, beyond.State.ForcedTravel.AmountSinceReset);

        var retry = Phase11SurvivalEngine.AccountForcedTravel(
            beyond.State, policy, party, 1, "hours", "travel-2", Dm);
        Assert.Equal(SurvivalOperationStatus.AlreadyApplied, retry.Status);
        Assert.Equal(9, retry.State.ForcedTravel.AmountSinceReset);
    }

    [Fact]
    public void ForcedTravelDoesNotImplicitlyConvertUnitsAndResetsOnlyExplicitly()
    {
        var policy = ForcedPolicy(2, "quarter-days");
        var wrongUnit = Phase11SurvivalEngine.AccountForcedTravel(
            ExpeditionSurvivalState.Empty, policy, CrawlPartySheet.Empty, 6, "hours", "travel", Dm);
        Assert.Equal(SurvivalOperationStatus.RequiresAdjudication, wrongUnit.Status);
        Assert.Equal(0, wrongUnit.State.ForcedTravel.AmountSinceReset);

        var correct = Phase11SurvivalEngine.AccountForcedTravel(
            ExpeditionSurvivalState.Empty, policy, CrawlPartySheet.Empty, 2, "quarter-days", "travel", Dm);
        var reset = Phase11SurvivalEngine.ResetForcedTravel(correct.State, CrawlPartySheet.Empty, Dm);
        Assert.Equal(0, reset.State.ForcedTravel.AmountSinceReset);
        Assert.Null(reset.State.ForcedTravel.Unit);
    }

    [Fact]
    public void ExposureProgressIsSeparateFromEnvironmentAndPersistentEffectState()
    {
        var progress = new ExpeditionExposureProgress
        {
            Id = Guid.NewGuid(),
            ExposureKey = "custom-cold",
            Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party),
            Amount = 3,
            Unit = "hours",
            SourceOccurrenceIds = [Guid.NewGuid()],
            Provenance = [Dm]
        };
        var survival = new ExpeditionSurvivalState { Exposure = [progress] };
        survival.Validate(CrawlPartySheet.Empty);

        Assert.Empty(ExpeditionEffectState.Empty.ActiveEffects);
        Assert.Single(survival.Exposure);
    }

    private static CampaignProcedure Materialize(string presetKey) =>
        CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;

    private static ForcedTravelPolicy ForcedPolicy(double limit, string unit) => new(
        Phase11PolicySupport.Supported,
        limit,
        unit,
        "resolved-check",
        "resolved-consequence",
        GenericProcedureCatalog.ForcedTravelPolicyMechanic,
        1,
        GenericProcedureExecutionHandlers.DeclarativeContract,
        null);

    private static ExpeditionConsequence ResourceConsequence(
        Guid id,
        ExpeditionEffectTarget target,
        ResourceChangeConsequenceComponent component) => new()
    {
        Id = id,
        ConsequenceKey = "resource-test",
        Category = ExpeditionConsequenceCategory.ResourceChange,
        Target = target,
        Components = [component],
        Provenance = Dm
    };

    private static ExpeditionEffectState PendingResourceState(ExpeditionConsequence consequence) => new()
    {
        AppliedConsequences =
        [
            new AppliedConsequenceRecord(
                consequence.Id,
                consequence.ConsequenceKey,
                ExpeditionConsequenceStatus.Deferred,
                [],
                consequence.Provenance,
                "Deferred to Phase 11")
        ],
        PendingConsequences =
        [
            new PendingExpeditionConsequence(
                consequence,
                ExpeditionConsequenceStatus.Deferred,
                "Resource mutation belongs to Phase 11.",
                "Apply through authoritative expedition resource state.")
        ]
    };
}
