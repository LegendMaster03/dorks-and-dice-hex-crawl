using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Api;

public sealed record Phase11PolicyContract(
    Phase11PolicySupport Support,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason);

public sealed record ResourceConsumptionPolicyContract(
    Phase11PolicySupport Support,
    IReadOnlyList<string> ResourceKinds,
    ExpeditionResourceInventoryModel? InventoryModel,
    string? ConsumptionModel,
    string? ConsumptionInterval,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ResourceConsumptionPolicyContract From(ResourceConsumptionPolicy value) => new(
        value.Support, value.ResourceKinds, value.InventoryModel, value.ConsumptionModel,
        value.ConsumptionInterval, value.MechanicKey, value.MechanicVersion, value.ExecutionHandler, value.UnsupportedReason);
}

public sealed record ForagingPolicyContract(
    Phase11PolicySupport Support,
    string? ResolutionModel,
    double? TimeCost,
    string? TimeUnit,
    string? MovementTradeoff,
    bool ActivityBacked,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ForagingPolicyContract From(ForagingPolicy value) => new(
        value.Support, value.ResolutionModel, value.TimeCost, value.TimeUnit, value.MovementTradeoff,
        value.ActivityBacked, value.MechanicKey, value.MechanicVersion, value.ExecutionHandler, value.UnsupportedReason);
}

public sealed record CampingPolicyContract(
    Phase11PolicySupport Support,
    string? ResolutionModel,
    double? TimeCost,
    string? TimeUnit,
    string? WatchModel,
    bool ActivityBacked,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static CampingPolicyContract From(CampingPolicy value) => new(
        value.Support, value.ResolutionModel, value.TimeCost, value.TimeUnit, value.WatchModel,
        value.ActivityBacked, value.MechanicKey, value.MechanicVersion, value.ExecutionHandler, value.UnsupportedReason);
}

public sealed record ForcedTravelPolicyContract(
    Phase11PolicySupport Support,
    double? NormalTravelLimit,
    string? LimitUnit,
    string? CheckModel,
    string? FailureConsequence,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ForcedTravelPolicyContract From(ForcedTravelPolicy value) => new(
        value.Support, value.NormalTravelLimit, value.LimitUnit, value.CheckModel, value.FailureConsequence,
        value.MechanicKey, value.MechanicVersion, value.ExecutionHandler, value.UnsupportedReason);
}

public sealed record ExposurePolicyContract(
    Phase11PolicySupport Support,
    IReadOnlyList<string> Dimensions,
    string? EvaluationModel,
    string? EvaluationInterval,
    ExpeditionEffectScope? TargetScope,
    string? ConsequenceModel,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ExposurePolicyContract From(ExposurePolicy value) => new(
        value.Support, value.Dimensions, value.EvaluationModel, value.EvaluationInterval, value.TargetScope,
        value.ConsequenceModel, value.MechanicKey, value.MechanicVersion, value.ExecutionHandler, value.UnsupportedReason);
}

public sealed record ExpeditionTargetContract(ExpeditionEffectScope Scope, Guid? TargetId)
{
    public static ExpeditionTargetContract From(ExpeditionEffectTarget value) => new(value.Scope, value.TargetId);
    public ExpeditionEffectTarget ToDomain() => new(Scope, TargetId);
}

public sealed record ExpeditionResourceContract(
    Guid Id,
    string ResourceKey,
    ExpeditionTargetContract Target,
    ExpeditionResourceInventoryModel InventoryModel,
    double? Quantity,
    string? Unit,
    string? SymbolicState,
    int? SupplyDieSides,
    bool IsDepleted,
    string? Note)
{
    public static ExpeditionResourceContract From(ExpeditionResource value) => new(
        value.Id, value.ResourceKey, ExpeditionTargetContract.From(value.Target), value.InventoryModel,
        value.Quantity, value.Unit, value.SymbolicState, value.SupplyDieSides, value.IsDepleted, value.Note);
}

public sealed record ForcedTravelStateContract(
    double AmountSinceReset,
    string? Unit,
    double? NormalLimit,
    bool ThresholdReached,
    bool ForcedTravelBegun,
    bool CheckDue,
    Guid? PendingCheckId,
    Guid? PendingConsequenceId,
    ForcedTravelResolutionRecord? LastResolution)
{
    public static ForcedTravelStateContract From(ForcedTravelState value, ForcedTravelPolicy policy)
    {
        var limit = policy.Support == Phase11PolicySupport.Supported ? policy.NormalTravelLimit : null;
        return new(
            value.AmountSinceReset,
            value.Unit,
            limit,
            limit.HasValue && value.AmountSinceReset >= limit.Value,
            limit.HasValue && value.AmountSinceReset > limit.Value,
            value.PendingCheck is not null,
            value.PendingCheck?.CheckId,
            value.PendingCheck?.ConsequenceId,
            value.LastResolution);
    }
}

public sealed record ExposureProgressContract(
    Guid Id,
    string ExposureKey,
    ExpeditionTargetContract Target,
    double Amount,
    string Unit,
    IReadOnlyList<Guid> SourceOccurrenceIds)
{
    public static ExposureProgressContract From(ExpeditionExposureProgress value) => new(
        value.Id, value.ExposureKey, ExpeditionTargetContract.From(value.Target), value.Amount, value.Unit, value.SourceOccurrenceIds);
}

public sealed record CampStateContract(
    Guid ResolutionId,
    bool Established,
    IReadOnlyList<Guid> ActivityAssignmentIds,
    string? ResolutionModel,
    string? RestTriggerKey,
    bool? RestSafe,
    bool? RestProlonged)
{
    public static CampStateContract From(ExpeditionCampState value) => new(
        value.ResolutionId, value.Established, value.ActivityAssignmentIds, value.ResolutionModel,
        value.RestTriggerKey, value.RestSafe, value.RestProlonged);
}

public sealed record SurvivalEnvironmentFactContract(
    Guid Id,
    string Dimension,
    string ValueKind,
    string? Value,
    bool Effective,
    EnvironmentFactSourceKind SourceKind);

public sealed record PendingResourceConsequenceContract(
    Guid ConsequenceId,
    string ConsequenceKey,
    ExpeditionConsequenceStatus Status,
    IReadOnlyList<string> ResourceKeys,
    string Reason);

public sealed record SurvivalResourcesContract(
    long ExpeditionVersion,
    ResourceConsumptionPolicyContract ResourcePolicy,
    ForagingPolicyContract ForagingPolicy,
    CampingPolicyContract CampingPolicy,
    ForcedTravelPolicyContract ForcedTravelPolicy,
    ExposurePolicyContract ExposurePolicy,
    IReadOnlyList<ExpeditionResourceContract> Resources,
    ForcedTravelStateContract ForcedTravel,
    IReadOnlyList<ExposureProgressContract> Exposure,
    CampStateContract? Camp,
    IReadOnlyList<SurvivalEnvironmentFactContract> EnvironmentFacts,
    IReadOnlyList<PendingResourceConsequenceContract> PendingResourceConsequences)
{
    public static SurvivalResourcesContract From(StoredExpedition expedition, EffectiveEnvironmentContext? environment = null)
    {
        var resourcePolicy = Phase11ProcedurePolicyResolver.ResolveResources(expedition.CampaignProcedure);
        var foraging = Phase11ProcedurePolicyResolver.ResolveForaging(expedition.CampaignProcedure);
        var camping = Phase11ProcedurePolicyResolver.ResolveCamping(expedition.CampaignProcedure);
        var forcedTravel = Phase11ProcedurePolicyResolver.ResolveForcedTravel(expedition.CampaignProcedure);
        var exposure = Phase11ProcedurePolicyResolver.ResolveExposure(expedition.CampaignProcedure);
        var dimensions = exposure.Support == Phase11PolicySupport.Supported
            ? exposure.Dimensions.ToHashSet(StringComparer.Ordinal)
            : [];
        var facts = environment?.Facts
            .Where(value => value.Effective && dimensions.Contains(value.Fact.Dimension))
            .Select(value => new SurvivalEnvironmentFactContract(
                value.Fact.Id,
                value.Fact.Dimension,
                value.Fact.ValueKind.ToString(),
                value.Fact.Measurement is { } measurement
                    ? $"{measurement.Value} {measurement.Unit}"
                    : value.Fact.Tag,
                value.Effective,
                value.Source.Kind))
            .ToArray() ?? [];
        var pending = expedition.Effects.PendingConsequences
            .Where(value => value.UnresolvedComponents.Any(component => component is ResourceChangeConsequenceComponent))
            .Select(value => new PendingResourceConsequenceContract(
                value.Consequence.Id,
                value.Consequence.ConsequenceKey,
                value.Status,
                value.UnresolvedComponents.OfType<ResourceChangeConsequenceComponent>()
                    .Select(component => component.ResourceKey).Distinct(StringComparer.Ordinal).ToArray(),
                value.Reason))
            .ToArray();
        return new(
            expedition.Version,
            ResourceConsumptionPolicyContract.From(resourcePolicy),
            ForagingPolicyContract.From(foraging),
            CampingPolicyContract.From(camping),
            ForcedTravelPolicyContract.From(forcedTravel),
            ExposurePolicyContract.From(exposure),
            expedition.Resources.Resources.Select(ExpeditionResourceContract.From).ToArray(),
            ForcedTravelStateContract.From(expedition.Survival.ForcedTravel, forcedTravel),
            expedition.Survival.Exposure.Select(ExposureProgressContract.From).ToArray(),
            expedition.Survival.Camp is null ? null : CampStateContract.From(expedition.Survival.Camp),
            facts,
            pending);
    }
}

public sealed record SurvivalOperationContract(
    long ExpeditionVersion,
    SurvivalOperationStatus Status,
    string Detail,
    Guid? ConsequenceId,
    SurvivalResourcesContract State);

public sealed record ConsequenceProvenanceRequest(
    ExpeditionConsequenceSourceKind SourceKind,
    string SourceKey,
    string? SourceReference = null,
    string? ProviderName = null,
    string? Note = null)
{
    public ExpeditionConsequenceProvenance ToDomain() => new(SourceKind, SourceKey, SourceReference, ProviderName, Note);
}

public sealed record ResourceChangeRequest(
    string ResourceKey,
    Guid? ResourceId,
    ResourceChangeOperation Operation,
    double? Quantity = null,
    string? Unit = null,
    string? State = null,
    int? SupplyDieSides = null)
{
    public ResourceChangeConsequenceComponent ToDomain() => new()
    {
        ResourceKey = ResourceKey,
        ResourceId = ResourceId,
        Operation = Operation,
        Quantity = Quantity,
        Unit = Unit,
        State = State,
        SupplyDieSides = SupplyDieSides
    };
}

public enum ResolvedSurvivalComponentKind { PersistentEffect, ExternalState }

public sealed record ResolvedSurvivalComponentRequest
{
    public required ResolvedSurvivalComponentKind Kind { get; init; }
    public required string Key { get; init; }
    public PersistentEffectChangeOperation? EffectOperation { get; init; }
    public int? LevelDelta { get; init; }
    public int? Level { get; init; }
    public double? Magnitude { get; init; }
    public double? Delta { get; init; }
    public string? Unit { get; init; }
    public string? State { get; init; }

    public ExpeditionConsequenceComponent ToDomain() => Kind switch
    {
        ResolvedSurvivalComponentKind.PersistentEffect => new PersistentEffectChangeConsequenceComponent
        {
            EffectKey = Key,
            Operation = EffectOperation ?? throw new ArgumentException("Persistent-effect component requires an operation."),
            LevelDelta = LevelDelta,
            Level = Level,
            Magnitude = Magnitude,
            Unit = Unit,
            State = State,
            ExplicitlyResolved = true
        },
        ResolvedSurvivalComponentKind.ExternalState => new ExternalStateConsequenceComponent(
            Key,
            Delta ?? throw new ArgumentException("External-state component requires a resolved delta."),
            Unit),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
}

public sealed record UpsertResourceRequest(
    long ExpectedVersion,
    string ResourceKey,
    ExpeditionTargetContract Target,
    ExpeditionResourceInventoryModel InventoryModel,
    double? Quantity,
    string? Unit,
    string? SymbolicState,
    int? SupplyDieSides,
    string? Note,
    ConsequenceProvenanceRequest Provenance)
{
    public UpsertExpeditionResourceCommand ToCommand(Guid id) => new(
        ExpectedVersion,
        new ExpeditionResource
        {
            Id = id,
            ResourceKey = ResourceKey,
            Target = Target.ToDomain(),
            InventoryModel = InventoryModel,
            Quantity = Quantity,
            Unit = Unit,
            SymbolicState = SymbolicState,
            SupplyDieSides = SupplyDieSides,
            Note = Note
        },
        Provenance.ToDomain());
}

public sealed record RemoveResourceRequest(long ExpectedVersion, ConsequenceProvenanceRequest Provenance)
{
    public RemoveExpeditionResourceCommand ToCommand() => new(ExpectedVersion, Provenance.ToDomain());
}

public sealed record ApplyPendingResourceRequest(long ExpectedVersion, ConsequenceProvenanceRequest Provenance)
{
    public ApplyPendingResourceConsequenceCommand ToCommand() => new(ExpectedVersion, Provenance.ToDomain());
}

public sealed record ResolveConsumptionRequest(
    long ExpectedVersion,
    Guid OccurrenceId,
    bool Due,
    ExpeditionTargetContract Target,
    IReadOnlyList<ResourceChangeRequest> Changes,
    ConsequenceProvenanceRequest Provenance)
{
    public ResolveResourceConsumptionCommand ToCommand() => new(
        ExpectedVersion, OccurrenceId, Due, Target.ToDomain(), Changes.Select(value => value.ToDomain()).ToArray(), Provenance.ToDomain());
}

public sealed record ResolveForagingRequest(
    long ExpectedVersion,
    Guid OccurrenceId,
    ExpeditionTargetContract Target,
    IReadOnlyList<Guid> ActivityAssignmentIds,
    IReadOnlyList<ResourceChangeRequest> ResourceGains,
    ConsequenceProvenanceRequest Provenance)
{
    public ResolveForagingCommand ToCommand() => new(
        ExpectedVersion, OccurrenceId, Target.ToDomain(), ActivityAssignmentIds,
        ResourceGains.Select(value => value.ToDomain()).ToArray(), Provenance.ToDomain());
}

public sealed record RecordForcedTravelUsageRequest(
    long ExpectedVersion,
    Guid OccurrenceId,
    double Amount,
    string Unit,
    ConsequenceProvenanceRequest Provenance)
{
    public RecordForcedTravelUsageCommand ToCommand() => new(
        ExpectedVersion, OccurrenceId, Amount, Unit, Provenance.ToDomain());
}

public sealed record ResolveForcedTravelCheckRequest(
    long ExpectedVersion,
    Guid CheckId,
    bool Success,
    ExpeditionTargetContract Target,
    IReadOnlyList<ResolvedSurvivalComponentRequest> FailureComponents,
    ConsequenceProvenanceRequest Provenance)
{
    public ResolveForcedTravelCheckCommand ToCommand() => new(
        ExpectedVersion, CheckId, Success, Target.ToDomain(), FailureComponents.Select(value => value.ToDomain()).ToArray(), Provenance.ToDomain());
}

public sealed record ResetForcedTravelRequest(long ExpectedVersion, ConsequenceProvenanceRequest Provenance)
{
    public ResetForcedTravelCommand ToCommand() => new(ExpectedVersion, Provenance.ToDomain());
}

public sealed record ResolveExposureRequest(
    long ExpectedVersion,
    Guid OccurrenceId,
    string ExposureKey,
    ExpeditionTargetContract Target,
    double? ProgressDelta,
    string? ProgressUnit,
    IReadOnlyList<ResolvedSurvivalComponentRequest> ConsequenceComponents,
    ConsequenceProvenanceRequest Provenance)
{
    public ResolveExposureCommand ToCommand() => new(
        ExpectedVersion, OccurrenceId, ExposureKey, Target.ToDomain(), ProgressDelta, ProgressUnit,
        ConsequenceComponents.Select(value => value.ToDomain()).ToArray(), Provenance.ToDomain());
}

public sealed record ResolveCampRequest(
    long ExpectedVersion,
    Guid ResolutionId,
    bool Established,
    IReadOnlyList<Guid> ActivityAssignmentIds,
    string? RestTriggerKey,
    bool? RestSafe,
    bool? RestProlonged,
    ConsequenceProvenanceRequest Provenance)
{
    public ResolveCampCommand ToCommand() => new(
        ExpectedVersion, ResolutionId, Established, ActivityAssignmentIds,
        RestTriggerKey, RestSafe, RestProlonged, Provenance.ToDomain());
}

public sealed record RecoverFromRestRequest(
    long ExpectedVersion,
    string TriggerKey,
    Guid EffectId,
    int? LevelReduction,
    bool Clear,
    ConsequenceProvenanceRequest Provenance)
{
    public RecoverFromResolvedRestCommand ToCommand() => new(
        ExpectedVersion, TriggerKey, EffectId, LevelReduction, Clear, Provenance.ToDomain());
}
