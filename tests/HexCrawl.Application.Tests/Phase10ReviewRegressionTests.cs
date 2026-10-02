using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class Phase10ReviewRegressionTests
{
    [Fact]
    public void RecoveryUsesTheEffectsMaterializedRecoveryModel()
    {
        var effectId = Guid.NewGuid();
        var state = new ExpeditionEffectState
        {
            ActiveEffects =
            [
                new ExpeditionEffect
                {
                    Id = effectId,
                    EffectKey = "manual-fatigue",
                    Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party),
                    Level = 2,
                    RecoveryModel = "short-rest"
                }
            ]
        };
        var provenance = new ExpeditionConsequenceProvenance(
            ExpeditionConsequenceSourceKind.Dm,
            "recovery-test");

        var result = ExpeditionConsequenceEngine.Recover(
            state,
            PersistentEffectPolicy.None,
            CrawlPartySheet.Empty,
            effectId,
            "short-rest",
            levelReduction: null,
            clear: true,
            provenance);

        Assert.True(result.StateChanged);
        Assert.Equal(ExpeditionConsequenceStatus.Applied, result.Status);
        Assert.Empty(result.State.ActiveEffects);
        Assert.Equal("recover:short-rest:clear", Assert.Single(result.State.History).Operation);
    }

    [Fact]
    public void AdjustLevelRejectsOverflowInsteadOfWrappingAndClearingTheEffect()
    {
        var effectId = Guid.NewGuid();
        var state = new ExpeditionEffectState
        {
            ActiveEffects =
            [
                new ExpeditionEffect
                {
                    Id = effectId,
                    EffectKey = "fatigue",
                    Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party),
                    MergeKey = "policy:fatigue:Party",
                    Level = int.MaxValue,
                    RecoveryModel = "rest"
                }
            ]
        };
        var policy = new PersistentEffectPolicy(
            PersistentEffectPolicySupport.Supported,
            ["fatigue"],
            "levels",
            "rest",
            ExpeditionEffectScope.Party,
            "effects.progressive",
            1,
            "declarative-contract",
            null);
        var consequence = new ExpeditionConsequence
        {
            Id = Guid.NewGuid(),
            ConsequenceKey = "fatigue-overflow",
            Category = ExpeditionConsequenceCategory.ExposureFatigue,
            Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party),
            Components =
            [
                new PersistentEffectChangeConsequenceComponent
                {
                    EffectKey = "fatigue",
                    Operation = PersistentEffectChangeOperation.AdjustLevel,
                    LevelDelta = 1
                }
            ],
            Provenance = new ExpeditionConsequenceProvenance(
                ExpeditionConsequenceSourceKind.Procedure,
                "overflow-test")
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ExpeditionConsequenceEngine.Process(
                state,
                policy,
                consequence,
                CrawlPartySheet.Empty));

        Assert.Equal("Adjusted persistent effect level exceeds the supported range.", exception.Message);
        Assert.Equal(int.MaxValue, Assert.Single(state.ActiveEffects).Level);
    }
}
