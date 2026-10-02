using System.Text.Json;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class Phase10EffectConsequenceEngineTests
{
    [Fact]
    public void MovementChangeConsequenceRoundTripsThroughPolymorphicJson()
    {
        var consequence = new ExpeditionConsequence
        {
            Id = Guid.NewGuid(),
            ConsequenceKey = "movement-change",
            Category = ExpeditionConsequenceCategory.MovementChange,
            Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party),
            Components =
            [
                new MovementChangeConsequenceComponent(new MovementConsequenceComponent
                {
                    Id = Guid.NewGuid(),
                    Key = "slow",
                    Operation = MovementCapabilityOperation.Multiply,
                    Value = 0.5
                })
            ],
            Provenance = Provenance("movement-source")
        };

        var json = JsonSerializer.Serialize(consequence);
        var roundTrip = JsonSerializer.Deserialize<ExpeditionConsequence>(json);

        Assert.NotNull(roundTrip);
        var movement = Assert.IsType<MovementChangeConsequenceComponent>(Assert.Single(roundTrip.Components));
        Assert.Equal("slow", movement.Movement.Key);
    }

    [Fact]
    public void MixedPersistentAndExternalConsequenceAppliesOwnedEffectAndPreservesExternalWork()
    {
        var consequence = MixedFatigueAndDamage();
        var policy = FatiguePolicy();

        var result = ExpeditionConsequenceEngine.Process(
            ExpeditionEffectState.Empty,
            policy,
            consequence,
            CrawlPartySheet.Empty);

        Assert.Equal(ExpeditionConsequenceStatus.ExternalActionRequired, result.Status);
        var effect = Assert.Single(result.State.ActiveEffects);
        Assert.Equal("fatigue", effect.EffectKey);
        Assert.Equal(1, effect.Level);

        var pending = Assert.Single(result.State.PendingConsequences);
        Assert.Equal(consequence.Id, pending.Consequence.Id);
        var unresolved = Assert.IsType<ExternalStateConsequenceComponent>(Assert.Single(pending.UnresolvedComponents));
        Assert.Equal("hit-points", unresolved.StateKey);

        var applied = Assert.Single(result.State.AppliedConsequences);
        Assert.Equal(ExpeditionConsequenceStatus.ExternalActionRequired, applied.Status);
        Assert.Equal(effect.Id, Assert.Single(applied.EffectIds));
        Assert.Equal(consequence.Provenance, applied.Provenance);

        var replay = ExpeditionConsequenceEngine.Process(
            result.State,
            policy,
            consequence,
            CrawlPartySheet.Empty);

        Assert.Equal(ExpeditionConsequenceStatus.AlreadyApplied, replay.Status);
        Assert.False(replay.StateChanged);
        Assert.Single(replay.State.ActiveEffects);
        Assert.Single(replay.State.PendingConsequences);
    }

    [Fact]
    public void ResolvingPendingConsequencePreservesProducerProvenanceAndRecordsResolverSeparately()
    {
        var consequence = MixedFatigueAndDamage();
        var processed = ExpeditionConsequenceEngine.Process(
            ExpeditionEffectState.Empty,
            FatiguePolicy(),
            consequence,
            CrawlPartySheet.Empty);
        var resolver = new ExpeditionConsequenceProvenance(
            ExpeditionConsequenceSourceKind.Dm,
            "dm-resolution",
            Note: "Damage applied in the character tool.");

        var resolved = ExpeditionConsequenceEngine.ResolvePending(
            processed.State,
            CrawlPartySheet.Empty,
            consequence.Id,
            resolver,
            "Applied the damage in the owning character tool.");

        Assert.Empty(resolved.PendingConsequences);
        var record = Assert.Single(resolved.AppliedConsequences);
        Assert.Equal(ExpeditionConsequenceStatus.Recorded, record.Status);
        Assert.Equal(consequence.Provenance, record.Provenance);
        Assert.Equal(resolver, record.ResolutionProvenance);
    }

    [Fact]
    public void MountEffectProjectionPreservesEstablishedMovementUnitIdentity()
    {
        var mountId = Guid.NewGuid();
        const string movementUnitKey = "horse-team";
        var party = new CrawlPartySheet
        {
            MovementContributors =
            [
                new MovementCapabilityContributor
                {
                    Id = mountId,
                    Kind = MovementCapabilityContributorKind.Mount,
                    Key = "horse-team-base",
                    Operation = MovementCapabilityOperation.Base,
                    Scope = MovementCapabilityScope.MovementUnit,
                    Value = 30,
                    Unit = "mi",
                    PerUnit = "day",
                    DistanceUnit = DistanceUnit.Miles,
                    MovementUnitKey = movementUnitKey,
                    Provenance = "phase-8-test"
                }
            ]
        };
        var movementId = Guid.NewGuid();
        var effect = new ExpeditionEffect
        {
            Id = Guid.NewGuid(),
            EffectKey = "mount-fatigue",
            Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Mount, mountId),
            MovementComponents =
            [
                new MovementConsequenceComponent
                {
                    Id = movementId,
                    Key = "fatigue-slowdown",
                    Operation = MovementCapabilityOperation.Multiply,
                    Value = 0.5
                }
            ]
        };
        var expedition = Expedition(party, new ExpeditionEffectState { ActiveEffects = [effect] });

        var projected = Assert.Single(ExpeditionEffectMovementProjection.Project(expedition));

        Assert.Equal(movementId, projected.Id);
        Assert.Equal(MovementCapabilityScope.MovementUnit, projected.Scope);
        Assert.Equal(movementUnitKey, projected.MovementUnitKey);
        Assert.NotEqual(mountId.ToString("D"), projected.MovementUnitKey);
    }

    private static ExpeditionConsequence MixedFatigueAndDamage() => new()
    {
        Id = Guid.NewGuid(),
        ConsequenceKey = "fatigue-with-damage",
        Category = ExpeditionConsequenceCategory.ExposureFatigue,
        Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party),
        Components =
        [
            new PersistentEffectChangeConsequenceComponent
            {
                EffectKey = "fatigue",
                Operation = PersistentEffectChangeOperation.AdjustLevel,
                LevelDelta = 1
            },
            new ExternalStateConsequenceComponent("hit-points", -3, "hp")
        ],
        Provenance = Provenance("forced-travel-result")
    };

    private static PersistentEffectPolicy FatiguePolicy() => new(
        PersistentEffectPolicySupport.Supported,
        ["fatigue"],
        "levels",
        "long-rest",
        ExpeditionEffectScope.Party,
        GenericProcedureCatalog.ProgressiveExpeditionEffectMechanic,
        1,
        GenericProcedureExecutionHandlers.DeclarativeContract,
        null);

    private static ExpeditionConsequenceProvenance Provenance(string key) => new(
        ExpeditionConsequenceSourceKind.ForcedTravelResult,
        key);

    private static StoredExpedition Expedition(
        CrawlPartySheet party,
        ExpeditionEffectState effects)
    {
        var now = DateTimeOffset.UtcNow;
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey)
            .MaterializeGeneric().Procedure;
        return new StoredExpedition(
            "Phase 10 effect projection",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
            },
            new WorldBoundCrawlSessionContext(Guid.NewGuid()),
            null,
            procedure,
            null,
            TimeSpan.FromHours(1),
            "owner",
            1,
            now,
            now)
        {
            Party = party,
            Effects = effects
        };
    }
}