using HexCrawl.Application;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Api;

public sealed record MovementCompositionPolicyContract(
    MovementCompositionPolicySupport Support,
    string? BudgetModel,
    double? BaseBudget,
    string? BudgetUnit,
    string? LimitingScope,
    MovementTerrainPolicySupport TerrainSupport,
    string? TerrainAdjustmentModel,
    IReadOnlyDictionary<string, string> TerrainAdjustments,
    string? RouteAdjustmentModel,
    string? WeatherAdjustmentModel,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static MovementCompositionPolicyContract From(MovementCompositionPolicy policy) => new(
        policy.Support,
        policy.BudgetModel,
        policy.BaseBudget,
        policy.BudgetUnit,
        policy.LimitingScope,
        policy.Terrain.Support,
        policy.Terrain.AdjustmentModel,
        policy.Terrain.TerrainAdjustments,
        policy.Terrain.RouteAdjustmentModel,
        policy.Terrain.WeatherAdjustmentModel,
        policy.MechanicKey,
        policy.MechanicVersion,
        policy.ExecutionHandler,
        policy.UnsupportedReason ?? policy.Terrain.UnsupportedReason);
}

public sealed record MovementAppliedContributorContract(
    Guid? Id,
    MovementCapabilityContributorKind? Kind,
    string Key,
    MovementCapabilityOperation Operation,
    bool Applied,
    double? Value,
    string? SymbolicValue,
    string? Unit,
    string? PerUnit,
    Guid? ParticipantId,
    string? MovementUnitKey,
    string? Provenance,
    string? Detail)
{
    public static MovementAppliedContributorContract From(MovementAppliedContributor contributor) => new(
        contributor.Id,
        contributor.Kind,
        contributor.Key,
        contributor.Operation,
        contributor.Applied,
        contributor.Value,
        contributor.SymbolicValue,
        contributor.Unit,
        contributor.PerUnit,
        contributor.ParticipantId,
        contributor.MovementUnitKey,
        contributor.Provenance,
        contributor.Detail);
}

public sealed record MovementCapabilityCompositionContract(
    MovementCompositionPolicyContract Policy,
    MovementCompositionStatus Status,
    double? EffectiveValue,
    string? EffectiveUnit,
    string? EffectivePerUnit,
    DistanceUnitContract? EffectiveDistanceUnit,
    string? LimitingContributorKey,
    Guid? LimitingParticipantId,
    double? PreOverrideValue,
    IReadOnlyList<MovementAppliedContributorContract> Contributors,
    IReadOnlyList<string> Provenance,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> Diagnostics,
    MovementReferenceUse ReferenceUse,
    DistanceContract? SuggestedExpectedDistance)
{
    public static MovementCapabilityCompositionContract From(MovementCapabilityComposition composition) => new(
        MovementCompositionPolicyContract.From(composition.Policy),
        composition.Status,
        composition.EffectiveValue,
        composition.EffectiveUnit,
        composition.EffectivePerUnit,
        composition.EffectiveDistanceUnit is { } unit ? DistanceUnitContract.From(unit) : null,
        composition.LimitingContributorKey,
        composition.LimitingParticipantId,
        composition.PreOverrideValue,
        composition.Contributors.Select(MovementAppliedContributorContract.From).ToArray(),
        composition.Provenance,
        composition.MissingInputs,
        composition.Diagnostics,
        composition.ReferenceUse,
        composition.SuggestedExpectedDistance is { } suggestion ? DistanceContract.From(suggestion) : null);
}
