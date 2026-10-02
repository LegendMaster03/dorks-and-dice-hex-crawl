using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Web.Api;

namespace HexCrawl.Web.Modules.Expeditions;

public sealed record PersistentEffectPolicyContract(
    PersistentEffectPolicySupport Support,
    IReadOnlyList<string> EffectKinds,
    string? AccumulationModel,
    string? RecoveryModel,
    ExpeditionEffectScope? Scope,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static PersistentEffectPolicyContract From(PersistentEffectPolicy policy) => new(
        policy.Support,
        policy.EffectKinds,
        policy.AccumulationModel,
        policy.RecoveryModel,
        policy.Scope,
        policy.MechanicKey,
        policy.MechanicVersion,
        policy.ExecutionHandler,
        policy.UnsupportedReason);
}

public sealed record ExpeditionEffectStateContract(
    PersistentEffectPolicyContract Policy,
    IReadOnlyList<ExpeditionEffect> ActiveEffects,
    IReadOnlyList<AppliedConsequenceRecord> AppliedConsequences,
    IReadOnlyList<PendingExpeditionConsequence> PendingConsequences,
    IReadOnlyList<ExpeditionEffectAuditRecord> History)
{
    public static ExpeditionEffectStateContract From(StoredExpedition expedition) => new(
        PersistentEffectPolicyContract.From(
            PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure)),
        expedition.Effects.ActiveEffects,
        expedition.Effects.AppliedConsequences,
        expedition.Effects.PendingConsequences,
        expedition.Effects.History);
}

public sealed record ExpeditionEffectOperationContract(
    ExpeditionWorkbenchContract Expedition,
    ExpeditionEffectStateContract Effects,
    ExpeditionConsequenceStatus Status,
    string Detail);

public sealed record ApplyExpeditionConsequenceRequest(
    long ExpectedVersion,
    ExpeditionConsequence Consequence)
{
    public ApplyExpeditionConsequenceCommand ToCommand() =>
        new(ExpectedVersion, Consequence);
}

public sealed record UpsertExpeditionEffectRequest(
    long ExpectedVersion,
    ExpeditionEffect Effect,
    ExpeditionConsequenceProvenance Provenance)
{
    public UpsertExpeditionEffectCommand ToCommand(Guid effectId)
    {
        if (Effect.Id != effectId)
        {
            throw new InvalidOperationException("Effect id in the request body must match the route.");
        }
        return new(ExpectedVersion, Effect, Provenance);
    }
}

public sealed record RecoverExpeditionEffectRequest(
    long ExpectedVersion,
    string TriggerKey,
    int? LevelReduction,
    bool Clear,
    ExpeditionConsequenceProvenance Provenance)
{
    public RecoverExpeditionEffectCommand ToCommand() =>
        new(ExpectedVersion, TriggerKey, LevelReduction, Clear, Provenance);
}

public sealed record ClearExpeditionEffectRequest(
    long ExpectedVersion,
    ExpeditionConsequenceProvenance Provenance)
{
    public ClearExpeditionEffectCommand ToCommand() => new(ExpectedVersion, Provenance);
}

public sealed record ResolvePendingConsequenceRequest(
    long ExpectedVersion,
    string ResolutionNote,
    ExpeditionConsequenceProvenance Provenance)
{
    public ResolvePendingConsequenceCommand ToCommand() =>
        new(ExpectedVersion, ResolutionNote, Provenance);
}
