using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class MovementCapabilityCompositionTests
{
    [Fact]
    public void PolicyComesFromExactPinnedProcedureAndIgnoresPresetIdentity()
    {
        var original = Procedure(CrawlProcedureCatalog.Dnd35PresetKey);
        var detached = original with
        {
            Key = "detached-custom-key",
            Name = "Detached movement procedure",
            Modules = original.Modules.Select(module =>
                module.Module.Key == GenericProcedureCatalog.MovementBudgetModule
                    ? module with
                    {
                        Parameters = new Dictionary<string, string>(module.Parameters, StringComparer.Ordinal)
                        {
                            ["baseBudget"] = "7",
                            ["budgetUnit"] = "custom-unit"
                        }
                    }
                    : module).ToArray()
        };

        var policy = MovementCompositionPolicyResolver.Resolve(detached);

        Assert.Equal(MovementCompositionPolicySupport.Supported, policy.Support);
        Assert.Equal("speed-derived-distance", policy.BudgetModel);
        Assert.Equal(7, policy.BaseBudget);
        Assert.Equal("custom-unit", policy.BudgetUnit);
        Assert.Equal("party-limiting", policy.LimitingScope);
    }

    [Fact]
    public void OriginMetadataDoesNotChangeComposition()
    {
        var materialized = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey).MaterializeGeneric();
        var member = Member("Walker");
        var party = new CrawlPartySheet
        {
            Members = [member],
            MovementContributors = [Participant(member, 3)]
        };
        var withOrigin = Expedition(materialized.Procedure, party) with
        {
            ProcedureOrigin = materialized.Origin
        };
        var withoutOrigin = withOrigin with { ProcedureOrigin = null };

        var first = MovementCapabilityComposer.Compose(withOrigin);
        var second = MovementCapabilityComposer.Compose(withoutOrigin);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.EffectiveValue, second.EffectiveValue);
        Assert.Equal(first.EffectiveUnit, second.EffectiveUnit);
        Assert.Equal(first.LimitingParticipantId, second.LimitingParticipantId);
    }

    [Fact]
    public void UnknownStoredMovementMechanicVersionIsUnsupported()
    {
        var procedure = Procedure(CrawlProcedureCatalog.Dnd35PresetKey);
        var future = procedure with
        {
            Modules = procedure.Modules.Select(module =>
                module.Module.Key == GenericProcedureCatalog.MovementBudgetModule
                    ? module with { Mechanic = module.Mechanic with { Version = 99 } }
                    : module).ToArray()
        };

        var policy = MovementCompositionPolicyResolver.Resolve(future);

        Assert.Equal(MovementCompositionPolicySupport.Unsupported, policy.Support);
        Assert.Contains("version 99", policy.UnsupportedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoMovementBudgetStillAllowsExplicitPartyMovementReference()
    {
        var party = new CrawlPartySheet
        {
            BaseMovement = new PartyMovementReference
            {
                PerHour = new DistanceMeasure(3, DistanceUnit.Miles)
            }
        };
        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure("simple-fixed-distance"), party));

        Assert.Equal(MovementCompositionPolicySupport.None, result.Policy.Support);
        Assert.Equal(MovementCompositionStatus.ReferenceFallback, result.Status);
        Assert.Equal(MovementReferenceUse.AuthoritativeBase, result.ReferenceUse);
        Assert.Equal(12, result.SuggestedExpectedDistance?.Value);
        Assert.Equal(DistanceUnit.Miles, result.SuggestedExpectedDistance?.Unit);
    }

    [Fact]
    public void IncludedParticipantsLimitDeterministicallyAndExcludedParticipantDoesNot()
    {
        var alice = Member("Alice");
        var bob = Member("Bob");
        var cara = Member("Cara", counts: false);
        var party = new CrawlPartySheet
        {
            Members = [alice, bob, cara],
            MovementContributors =
            [
                Participant(alice, 3, id: Guid.Parse("00000000-0000-0000-0000-000000000003")),
                Participant(bob, 2, id: Guid.Parse("00000000-0000-0000-0000-000000000002")),
                Participant(cara, 1, id: Guid.Parse("00000000-0000-0000-0000-000000000001"))
            ]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party));

        Assert.Equal(MovementCompositionStatus.Resolved, result.Status);
        Assert.Equal(2, result.EffectiveValue);
        Assert.Equal(bob.Id, result.LimitingParticipantId);
        Assert.Contains(result.Contributors, value =>
            value.ParticipantId == cara.Id && !value.Applied);
    }

    [Fact]
    public void AssignedMountReplacesRiderWalkingCapability()
    {
        var alice = Member("Alice");
        var bob = Member("Bob");
        var mount = BaseContributor(
            MovementCapabilityContributorKind.Mount,
            "horse",
            4,
            replaces: [bob.Id]);
        var party = new CrawlPartySheet
        {
            Members = [alice, bob],
            MovementContributors =
            [
                Participant(alice, 3),
                Participant(bob, 1),
                mount
            ]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party));

        Assert.Equal(3, result.EffectiveValue);
        Assert.Equal(alice.Id, result.LimitingParticipantId);
        Assert.Contains(result.Contributors, value =>
            value.ParticipantId == bob.Id
            && !value.Applied
            && value.Detail!.Contains("replaced", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VehicleCanReplaceMultiplePassengersAndUnassignedVehicleIsIgnored()
    {
        var alice = Member("Alice");
        var bob = Member("Bob");
        var cara = Member("Cara");
        var wagon = BaseContributor(
            MovementCapabilityContributorKind.Vehicle,
            "wagon",
            2.5,
            replaces: [bob.Id, cara.Id]);
        var emptyCart = BaseContributor(
            MovementCapabilityContributorKind.Vehicle,
            "empty-cart",
            0.5);
        var party = new CrawlPartySheet
        {
            Members = [alice, bob, cara],
            MovementContributors =
            [
                Participant(alice, 3),
                Participant(bob, 1),
                Participant(cara, 1.5),
                wagon,
                emptyCart
            ]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party));

        Assert.Equal(2.5, result.EffectiveValue);
        Assert.Equal("wagon", result.LimitingContributorKey);
        Assert.Contains(result.Contributors, value =>
            value.Key == "empty-cart" && !value.Applied);
    }

    [Fact]
    public void TypedLoadModeAndEffectOperationsUseDeterministicStages()
    {
        var alice = Member("Alice");
        var party = new CrawlPartySheet
        {
            Members = [alice],
            MovementContributors =
            [
                Participant(alice, 4),
                Adjustment(MovementCapabilityContributorKind.PersistentEffect, "cap", MovementCapabilityOperation.Cap, 3, physical: true),
                Adjustment(MovementCapabilityContributorKind.TravelMode, "fast", MovementCapabilityOperation.Multiply, 2),
                Adjustment(MovementCapabilityContributorKind.Load, "encumbered", MovementCapabilityOperation.Multiply, 0.5)
            ]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party),
            new MovementCompositionInput(PaceKey: "fast"));

        Assert.Equal(MovementCompositionStatus.Resolved, result.Status);
        Assert.Equal(3, result.EffectiveValue);
        Assert.Equal(
            ["encumbered", "fast", "cap"],
            result.Contributors
                .Where(value => value.Applied && value.Id.HasValue && value.Kind != MovementCapabilityContributorKind.Participant)
                .Select(value => value.Key)
                .ToArray());
    }

    [Fact]
    public void Dnd35NumericTerrainMultiplierComposesWithParticipantCapability()
    {
        var walker = Member("Walker");
        var party = new CrawlPartySheet
        {
            Members = [walker],
            MovementContributors = [Participant(walker, 3)]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party),
            new MovementCompositionInput(TerrainKey: "difficult"));

        Assert.Equal(MovementCompositionStatus.Resolved, result.Status);
        Assert.Equal(1.5, result.EffectiveValue);
        Assert.Equal(1.5, result.SuggestedExpectedDistance?.Value);
    }

    [Fact]
    public void Dnd2024ArcticPaceRemainsConditionalSymbolicData()
    {
        var walker = Member("Walker");
        var party = new CrawlPartySheet
        {
            Members = [walker],
            MovementContributors = [Participant(walker, 3)]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd2024PresetKey), party),
            new MovementCompositionInput(TerrainKey: "arctic", PaceKey: "fast"));

        Assert.Equal(MovementCompositionStatus.RequiresAdjudication, result.Status);
        Assert.Null(result.SuggestedExpectedDistance);
        Assert.Contains(result.Contributors, value =>
            value.SymbolicValue == "fast-if-appropriately-equipped");
    }

    [Fact]
    public void SpeedDerivedActivityBudgetRequiresCapabilityInsteadOfInventingFormula()
    {
        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey)));

        Assert.Equal("speed-derived-activities", result.Policy.BudgetModel);
        Assert.Equal("hexploration-activity", result.Policy.BudgetUnit);
        Assert.Equal(MovementCompositionStatus.InputRequired, result.Status);
        Assert.Contains("movement base capability", result.MissingInputs);
        Assert.Null(result.SuggestedExpectedDistance);
    }

    [Fact]
    public void ForbiddenLandsPreservesQuarterDayBudgetAndTerrainCostSemantics()
    {
        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.ForbiddenLandsPresetKey)),
            new MovementCompositionInput(TerrainKey: "difficult"));

        Assert.Equal("quarter-day-activities", result.Policy.BudgetModel);
        Assert.Equal("quarter-day", result.EffectiveUnit);
        Assert.Equal(1, result.EffectiveValue);
        Assert.Equal(MovementCompositionStatus.RequiresAdjudication, result.Status);
        Assert.Null(result.SuggestedExpectedDistance);
        Assert.Contains(result.Contributors, value =>
            value.Key == "difficult"
            && value.Operation == MovementCapabilityOperation.Cost
            && !value.Applied);
    }

    [Fact]
    public void WorldsWithoutNumberSupportsNumericRateAdjustmentWithoutCatalogDispatch()
    {
        var baseRate = BaseContributor(
            MovementCapabilityContributorKind.Environment,
            "manual-party-rate",
            4,
            scope: MovementCapabilityScope.Party);
        var party = new CrawlPartySheet { MovementContributors = [baseRate] };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.WorldsWithoutNumberPresetKey), party),
            new MovementCompositionInput(TerrainKey: "rough"));

        Assert.Equal("distance-per-hour", result.Policy.BudgetModel);
        Assert.Equal(2, result.EffectiveValue);
        Assert.Equal(20, result.SuggestedExpectedDistance?.Value);
    }

    [Fact]
    public void OneRingRemainsJourneyProgressWithNoFabricatedIntervalOrDistance()
    {
        var procedure = Procedure(CrawlProcedureCatalog.OneRing2ePresetKey);
        var result = MovementCapabilityComposer.Compose(Expedition(procedure));

        Assert.DoesNotContain(procedure.Modules, value =>
            value.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.Equal("journey-progress", result.Policy.BudgetModel);
        Assert.Equal("journey-leg", result.EffectiveUnit);
        Assert.Equal(1, result.EffectiveValue);
        Assert.Equal(MovementCompositionStatus.Resolved, result.Status);
        Assert.Null(result.EffectiveDistanceUnit);
        Assert.Null(result.SuggestedExpectedDistance);
    }

    [Fact]
    public void ReferenceIsInformationalWhenCapabilityResolvesAndDmOverrideWinsLast()
    {
        var walker = Member("Walker");
        var party = new CrawlPartySheet
        {
            Members = [walker],
            BaseMovement = new PartyMovementReference
            {
                PerHour = new DistanceMeasure(2, DistanceUnit.Miles)
            },
            MovementContributors =
            [
                Participant(walker, 3),
                new MovementCapabilityContributor
                {
                    Id = Guid.NewGuid(),
                    Kind = MovementCapabilityContributorKind.DmOverride,
                    Key = "dm-final",
                    Operation = MovementCapabilityOperation.Replace,
                    Scope = MovementCapabilityScope.Party,
                    Value = 4,
                    Unit = "mi",
                    PerUnit = "hour",
                    DistanceUnit = DistanceUnit.Miles,
                    Provenance = "DM adjudication"
                }
            ]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party));

        Assert.Equal(4, result.EffectiveValue);
        Assert.Equal(3, result.PreOverrideValue);
        Assert.Equal(MovementReferenceUse.InformationalOnly, result.ReferenceUse);
        Assert.Equal(4, result.SuggestedExpectedDistance?.Value);
        Assert.Contains("DM adjudication", result.Provenance);
        Assert.Contains(result.Contributors, value =>
            value.Key == "party-movement-reference" && !value.Applied);
    }

    [Fact]
    public void ProviderStylePartyCapabilityComposesWithoutStructuralMovementBudget()
    {
        var providerBase = BaseContributor(
            MovementCapabilityContributorKind.Environment,
            "provider:walk-distance",
            3,
            scope: MovementCapabilityScope.Party,
            provenance: "Provider walk capability");
        var terrain = Adjustment(
            MovementCapabilityContributorKind.Environment,
            "provider:terrain-factor",
            MovementCapabilityOperation.Multiply,
            0.5,
            provenance: "Provider terrain factor");
        var expedition = Expedition(Procedure("alexandrian-advanced"));

        var result = MovementCapabilityComposer.Compose(
            expedition,
            new MovementCompositionInput(ResolvedContributors: [providerBase, terrain]));

        Assert.Equal(MovementCompositionPolicySupport.None, result.Policy.Support);
        Assert.Equal(MovementCompositionStatus.Resolved, result.Status);
        Assert.Equal(1.5, result.EffectiveValue);
        Assert.Equal(6, result.SuggestedExpectedDistance?.Value);
        Assert.Contains("Provider walk capability", result.Provenance);
        Assert.Contains("Provider terrain factor", result.Provenance);
    }

    [Fact]
    public void ActiveWatchSuggestionUsesRemainingIntervalDuration()
    {
        var providerBase = BaseContributor(
            MovementCapabilityContributorKind.Environment,
            "provider:walk-distance",
            3,
            scope: MovementCapabilityScope.Party);
        var state = State() with
        {
            ActiveWatch = new ActiveWatchState(
                1,
                TimeSpan.FromHours(4),
                TimeSpan.FromHours(2),
                new WatchTravelPlan(
                    new HexDirection(0),
                    TravelModeSelection.Normal,
                    NavigationAidSelection.None),
                ResolvedEncounter.None,
                false,
                null)
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure("alexandrian-advanced"), state: state),
            new MovementCompositionInput(ResolvedContributors: [providerBase]));

        Assert.Equal(6, result.SuggestedExpectedDistance?.Value);
    }

    [Fact]
    public void IncompatibleCustomUnitsAreNotGuessed()
    {
        var alice = Member("Alice");
        var bob = Member("Bob");
        var party = new CrawlPartySheet
        {
            Members = [alice, bob],
            MovementContributors =
            [
                NonDistanceParticipant(alice, 2, "custom-a"),
                NonDistanceParticipant(bob, 1, "custom-b")
            ]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party));

        Assert.Equal(MovementCompositionStatus.RequiresAdjudication, result.Status);
        Assert.Contains(result.Diagnostics, value =>
            value.Contains("can not be converted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidContributorNumbersAreRejected()
    {
        var negative = BaseContributor(
            MovementCapabilityContributorKind.Environment,
            "negative",
            -1,
            scope: MovementCapabilityScope.Party);
        var infinity = BaseContributor(
            MovementCapabilityContributorKind.Environment,
            "infinite",
            double.PositiveInfinity,
            scope: MovementCapabilityScope.Party);

        Assert.Throws<InvalidOperationException>(() => negative.Validate(new HashSet<Guid>()));
        Assert.Throws<InvalidOperationException>(() => infinity.Validate(new HashSet<Guid>()));
    }

    [Fact]
    public async Task PersistedManualMovementContributorRoundTripsPostgres()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var contributor = BaseContributor(
            MovementCapabilityContributorKind.Environment,
            "manual-party-rate",
            2.75,
            scope: MovementCapabilityScope.Party,
            provenance: "manual test");
        var expedition = Expedition(
            Procedure("simple-fixed-distance"),
            new CrawlPartySheet { MovementContributors = [contributor] });

        await store.CreateExpeditionAsync(expedition);
        var loaded = await store.GetExpeditionAsync(expedition.Id, expedition.OwnerUserId);

        Assert.NotNull(loaded);
        var roundTripped = Assert.Single(loaded!.Party.MovementContributors);
        Assert.Equal(contributor.Id, roundTripped.Id);
        Assert.Equal(contributor.Kind, roundTripped.Kind);
        Assert.Equal(contributor.Key, roundTripped.Key);
        Assert.Equal(contributor.Operation, roundTripped.Operation);
        Assert.Equal(contributor.Scope, roundTripped.Scope);
        Assert.Equal(contributor.Value, roundTripped.Value);
        Assert.Equal(contributor.Unit, roundTripped.Unit);
        Assert.Equal(contributor.PerUnit, roundTripped.PerUnit);
        Assert.Equal(contributor.DistanceUnit, roundTripped.DistanceUnit);
        Assert.Equal(contributor.SymbolicValue, roundTripped.SymbolicValue);
        Assert.Equal(contributor.ParticipantId, roundTripped.ParticipantId);
        Assert.Equal(contributor.MovementUnitKey, roundTripped.MovementUnitKey);
        Assert.Equal(contributor.ReplacesParticipantIds, roundTripped.ReplacesParticipantIds);
        Assert.Equal(contributor.Provenance, roundTripped.Provenance);
        Assert.Equal(contributor.Note, roundTripped.Note);
        Assert.Equal(contributor.Enabled, roundTripped.Enabled);
    }

    [Theory]
    [InlineData(CrawlProcedureCatalog.BxPresetKey, "fixed-per-day", "travel-day")]
    [InlineData(CrawlProcedureCatalog.Adnd2ePresetKey, "movement-points", "daily-movement-budget")]
    [InlineData(CrawlProcedureCatalog.Dnd35PresetKey, "speed-derived-distance", "hour")]
    [InlineData(CrawlProcedureCatalog.Dnd2024PresetKey, "speed-and-pace", "hour")]
    [InlineData(CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey, "speed-derived-activities", "hexploration-activity")]
    [InlineData(CrawlProcedureCatalog.ForbiddenLandsPresetKey, "quarter-day-activities", "quarter-day")]
    [InlineData(CrawlProcedureCatalog.WorldsWithoutNumberPresetKey, "distance-per-hour", "travel-hours")]
    [InlineData(CrawlProcedureCatalog.OneRing2ePresetKey, "journey-progress", "journey-leg")]
    public void ProofMatrixMovementPolicyIsGeneric(string presetKey, string budgetModel, string budgetUnit)
    {
        var policy = MovementCompositionPolicyResolver.Resolve(Procedure(presetKey));

        Assert.Equal(MovementCompositionPolicySupport.Supported, policy.Support);
        Assert.Equal(budgetModel, policy.BudgetModel);
        Assert.Equal(budgetUnit, policy.BudgetUnit);
        Assert.DoesNotContain(presetKey, policy.MechanicKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SyntheticMixedMovementPolicyIsGeneric()
    {
        var procedure = SyntheticProcedureFixtures.MixedProcedure();
        var policy = MovementCompositionPolicyResolver.Resolve(procedure);

        Assert.Equal(MovementCompositionPolicySupport.Supported, policy.Support);
        Assert.Equal("activity-and-distance", policy.BudgetModel);
        Assert.Equal("watch", policy.BudgetUnit);
        Assert.DoesNotContain("synthetic-mixed-procedure", policy.MechanicKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeAssemblyHasNoOptionalProviderDependency()
    {
        var domainAssembly = typeof(CrawlRuntimeEngine).Assembly;
        Assert.DoesNotContain(domainAssembly.GetReferencedAssemblies(), reference =>
            string.Equals(reference.Name, "HexCrawl.Application", StringComparison.Ordinal)
            || reference.Name?.Contains("RulesCore", StringComparison.OrdinalIgnoreCase) == true
            || reference.Name?.Contains("CharacterSheet", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static CampaignProcedure Procedure(string presetKey) =>
        CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;

    private static StoredExpedition Expedition(
        CampaignProcedure procedure,
        CrawlPartySheet? party = null,
        ExpeditionState? state = null)
    {
        var now = DateTimeOffset.UtcNow;
        var interval = FocusedIntervalPolicyResolver.Resolve(procedure).IntervalDuration ?? TimeSpan.Zero;
        return new StoredExpedition(
            "Movement test",
            state ?? State(),
            Context(),
            null,
            procedure,
            null,
            interval,
            "owner",
            1,
            now,
            now)
        {
            Party = party ?? CrawlPartySheet.Empty
        };
    }

    private static AbstractHexCrawlSessionContext Context() => new(
        "Movement test",
        HexOrientation.PointyTop,
        new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));

    private static ExpeditionState State() => new()
    {
        Id = Guid.NewGuid(),
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
    };

    private static CrawlPartyMember Member(string name, bool counts = true) =>
        new(Guid.NewGuid(), name, CountsTowardPartyMovement: counts);

    private static MovementCapabilityContributor Participant(
        CrawlPartyMember member,
        double value,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Kind = MovementCapabilityContributorKind.Participant,
        Key = member.Name.ToLowerInvariant(),
        Operation = MovementCapabilityOperation.Base,
        Scope = MovementCapabilityScope.Participant,
        Value = value,
        Unit = "mi",
        PerUnit = "hour",
        DistanceUnit = DistanceUnit.Miles,
        ParticipantId = member.Id,
        Provenance = "manual participant capability"
    };

    private static MovementCapabilityContributor NonDistanceParticipant(
        CrawlPartyMember member,
        double value,
        string unit) => new()
    {
        Id = Guid.NewGuid(),
        Kind = MovementCapabilityContributorKind.Participant,
        Key = member.Name.ToLowerInvariant(),
        Operation = MovementCapabilityOperation.Base,
        Scope = MovementCapabilityScope.Participant,
        Value = value,
        Unit = unit,
        ParticipantId = member.Id
    };

    private static MovementCapabilityContributor BaseContributor(
        MovementCapabilityContributorKind kind,
        string key,
        double value,
        MovementCapabilityScope scope = MovementCapabilityScope.MovementUnit,
        IReadOnlyList<Guid>? replaces = null,
        string? provenance = null) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Key = key,
        Operation = MovementCapabilityOperation.Base,
        Scope = scope,
        Value = value,
        Unit = "mi",
        PerUnit = "hour",
        DistanceUnit = DistanceUnit.Miles,
        ReplacesParticipantIds = replaces ?? [],
        Provenance = provenance
    };

    private static MovementCapabilityContributor Adjustment(
        MovementCapabilityContributorKind kind,
        string key,
        MovementCapabilityOperation operation,
        double value,
        bool physical = false,
        string? provenance = null) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Key = key,
        Operation = operation,
        Scope = MovementCapabilityScope.Party,
        Value = value,
        Unit = physical ? "mi" : "factor",
        PerUnit = physical ? "hour" : null,
        DistanceUnit = physical ? DistanceUnit.Miles : null,
        Provenance = provenance
    };
}
