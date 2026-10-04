using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record SurvivalOperationResult(
    StoredExpedition Expedition,
    SurvivalOperationStatus Status,
    string Detail,
    Guid? ConsequenceId = null);

public sealed record UpsertExpeditionResourceCommand(
    long ExpectedVersion,
    ExpeditionResource Resource,
    ExpeditionConsequenceProvenance Provenance);

public sealed record RemoveExpeditionResourceCommand(
    long ExpectedVersion,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ApplyPendingResourceConsequenceCommand(
    long ExpectedVersion,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ResolveResourceConsumptionCommand(
    long ExpectedVersion,
    Guid OccurrenceId,
    bool Due,
    ExpeditionEffectTarget Target,
    IReadOnlyList<ResourceChangeConsequenceComponent> Changes,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ResolveForagingCommand(
    long ExpectedVersion,
    Guid OccurrenceId,
    ExpeditionEffectTarget Target,
    IReadOnlyList<Guid> ActivityAssignmentIds,
    IReadOnlyList<ResourceChangeConsequenceComponent> ResourceGains,
    ExpeditionConsequenceProvenance Provenance);

public sealed record RecordForcedTravelUsageCommand(
    long ExpectedVersion,
    Guid OccurrenceId,
    double Amount,
    string Unit,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ResolveForcedTravelCheckCommand(
    long ExpectedVersion,
    Guid CheckId,
    bool Success,
    ExpeditionEffectTarget Target,
    IReadOnlyList<ExpeditionConsequenceComponent> FailureComponents,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ResetForcedTravelCommand(
    long ExpectedVersion,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ResolveExposureCommand(
    long ExpectedVersion,
    Guid OccurrenceId,
    string ExposureKey,
    ExpeditionEffectTarget Target,
    double? ProgressDelta,
    string? ProgressUnit,
    IReadOnlyList<ExpeditionConsequenceComponent> ConsequenceComponents,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ResolveCampCommand(
    long ExpectedVersion,
    Guid ResolutionId,
    bool Established,
    IReadOnlyList<Guid> ActivityAssignmentIds,
    string? RestTriggerKey,
    bool? RestSafe,
    bool? RestProlonged,
    ExpeditionConsequenceProvenance Provenance);

public sealed record RecoverFromResolvedRestCommand(
    long ExpectedVersion,
    string TriggerKey,
    Guid EffectId,
    int? LevelReduction,
    bool Clear,
    ExpeditionConsequenceProvenance Provenance);

/// <summary>
/// Phase 11 orchestration. Character statistics and equipment remain external. Providers and DMs
/// supply typed resolved inputs; this service owns only expedition resource/survival state and uses
/// Phase 10 for consequence identity and persistent effect mutation.
/// </summary>
public sealed class ExpeditionSurvivalService(IHexCrawlStore store, HexCrawlService coreService)
{
    public async Task<SurvivalOperationResult> UpsertResourceAsync(
        Guid expeditionId,
        string ownerUserId,
        UpsertExpeditionResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        command.Provenance.Validate();
        command.Resource.Validate(expedition.Party);
        var resources = expedition.Resources.Resources.ToList();
        var existing = resources.SingleOrDefault(value => value.Id == command.Resource.Id);
        var resource = command.Resource with
        {
            Provenance = (existing?.Provenance ?? []).Concat(command.Resource.Provenance).Append(command.Provenance).ToArray()
        };
        if (existing is null) resources.Add(resource);
        else resources[resources.IndexOf(existing)] = resource;
        var audit = new ExpeditionResourceAuditRecord(
            Guid.NewGuid(), resource.Id, resource.ResourceKey, existing is null ? "manual:add" : "manual:correct",
            existing?.Quantity, resource.Quantity, existing?.SymbolicState, resource.SymbolicState,
            existing?.SupplyDieSides, resource.SupplyDieSides, null, command.Provenance);
        var state = expedition.Resources with
        {
            Resources = resources,
            History = expedition.Resources.History.Append(audit).ToArray()
        };
        state.Validate(expedition.Party);
        var saved = await SaveAsync(expedition with { Resources = state }, command.ExpectedVersion, cancellationToken);
        return new(saved, SurvivalOperationStatus.Applied,
            existing is null ? "Expedition resource added." : "Expedition resource corrected explicitly.");
    }

    public async Task<SurvivalOperationResult> RemoveResourceAsync(
        Guid expeditionId,
        Guid resourceId,
        string ownerUserId,
        RemoveExpeditionResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        command.Provenance.Validate();
        var resource = expedition.Resources.Resources.SingleOrDefault(value => value.Id == resourceId)
            ?? throw new InvalidOperationException("Expedition resource was not found.");
        if (expedition.Effects.PendingConsequences.Any(pending =>
                pending.UnresolvedComponents.OfType<ResourceChangeConsequenceComponent>()
                    .Any(component =>
                        component.ResourceId == resourceId
                        || (!component.ResourceId.HasValue
                            && string.Equals(component.ResourceKey, resource.ResourceKey, StringComparison.Ordinal)
                            && pending.Consequence.Target == resource.Target))))
        {
            throw new InvalidOperationException("Resource can not be removed while a pending consequence targets it explicitly or by resource key and target.");
        }
        var audit = new ExpeditionResourceAuditRecord(
            Guid.NewGuid(), resource.Id, resource.ResourceKey, "manual:remove",
            resource.Quantity, null, resource.SymbolicState, null, resource.SupplyDieSides, null, null, command.Provenance);
        var state = expedition.Resources with
        {
            Resources = expedition.Resources.Resources.Where(value => value.Id != resourceId).ToArray(),
            History = expedition.Resources.History.Append(audit).ToArray()
        };
        var saved = await SaveAsync(expedition with { Resources = state }, command.ExpectedVersion, cancellationToken);
        return new(saved, SurvivalOperationStatus.Applied, "Expedition resource removed explicitly.");
    }

    public async Task<SurvivalOperationResult> ApplyPendingResourceConsequenceAsync(
        Guid expeditionId,
        Guid consequenceId,
        string ownerUserId,
        ApplyPendingResourceConsequenceCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var result = ResourceConsequenceConsumer.Consume(
            expedition.Resources, expedition.Effects, expedition.Party, consequenceId, command.Provenance);
        if (!result.StateChanged)
        {
            return new(expedition, ToSurvivalStatus(result.Status), result.Detail, consequenceId);
        }
        var saved = await SaveAsync(expedition with { Resources = result.Resources, Effects = result.Effects },
            command.ExpectedVersion, cancellationToken);
        return new(saved, ToSurvivalStatus(result.Status), result.Detail, consequenceId);
    }

    public async Task<SurvivalOperationResult> ResolveConsumptionAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveResourceConsumptionCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = Phase11ProcedurePolicyResolver.ResolveResources(expedition.CampaignProcedure);
        if (!command.Due)
        {
            return new(expedition, SurvivalOperationStatus.NotDue, "Resource consumption was explicitly resolved as not due.");
        }
        if (policy.Support != Phase11PolicySupport.Supported)
        {
            return PolicyFailure(expedition, policy.Support, policy.UnsupportedReason, "survival.resources");
        }
        if (command.Changes.Count == 0)
        {
            return new(expedition, SurvivalOperationStatus.InputRequired,
                "Consumption is due, but the pinned policy does not contain a complete quantity formula. Supply resolved resource changes.");
        }
        if (command.Changes.Any(value => !policy.ResourceKinds.Contains(value.ResourceKey, StringComparer.Ordinal)))
        {
            return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                "A resolved consumption change references a resource key not declared by the exact pinned resource policy.");
        }
        return await ProduceResourceConsequenceAsync(expedition, command.ExpectedVersion, command.OccurrenceId,
            "resource-consumption", command.Target, command.Changes, command.Provenance, cancellationToken);
    }

    public async Task<SurvivalOperationResult> ResolveForagingAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveForagingCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = Phase11ProcedurePolicyResolver.ResolveForaging(expedition.CampaignProcedure);
        if (policy.Support != Phase11PolicySupport.Supported)
        {
            return PolicyFailure(expedition, policy.Support, policy.UnsupportedReason, "exploration.foraging");
        }
        if (policy.ActivityBacked)
        {
            if (command.ActivityAssignmentIds.Count == 0)
            {
                return new(expedition, SurvivalOperationStatus.InputRequired,
                    "The pinned foraging policy is activity-backed and requires explicit typed participant activity assignments.");
            }
            ValidateAssignments(expedition.Party, command.ActivityAssignmentIds);
        }
        if (command.ResourceGains.Count == 0)
        {
            return new(expedition, SurvivalOperationStatus.InputRequired,
                "The pinned foraging policy does not define a generic yield table. Supply an explicitly resolved resource gain.");
        }
        if (command.ResourceGains.Any(value => value.Operation == ResourceChangeOperation.AdjustQuantity && value.Quantity <= 0))
        {
            return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                "Foraging resource gains must be explicitly positive when using adjust-quantity operations.");
        }
        return await ProduceResourceConsequenceAsync(expedition, command.ExpectedVersion, command.OccurrenceId,
            "foraging-yield", command.Target, command.ResourceGains, command.Provenance, cancellationToken);
    }

    public async Task<SurvivalOperationResult> RecordForcedTravelUsageAsync(
        Guid expeditionId,
        string ownerUserId,
        RecordForcedTravelUsageCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        if (command.OccurrenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Forced-travel usage occurrence id is required.");
        }
        var transition = Phase11SurvivalEngine.AccountForcedTravel(
            expedition.Survival,
            Phase11ProcedurePolicyResolver.ResolveForcedTravel(expedition.CampaignProcedure),
            expedition.Party,
            command.Amount,
            command.Unit,
            command.OccurrenceId.ToString("D"),
            command.Provenance);
        if (!transition.StateChanged)
        {
            return new(expedition, transition.Status, transition.Detail);
        }
        var saved = await SaveAsync(expedition with { Survival = transition.State }, command.ExpectedVersion, cancellationToken);
        return new(saved, transition.Status, transition.Detail);
    }

    public async Task<SurvivalOperationResult> ResolveForcedTravelCheckAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveForcedTravelCheckCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = Phase11ProcedurePolicyResolver.ResolveForcedTravel(expedition.CampaignProcedure);
        if (policy.Support != Phase11PolicySupport.Supported)
        {
            return PolicyFailure(expedition, policy.Support, policy.UnsupportedReason, "time.forced-travel");
        }
        var pending = expedition.Survival.ForcedTravel.PendingCheck;
        if (pending is null)
        {
            if (expedition.Survival.ForcedTravel.LastResolution?.CheckId == command.CheckId)
            {
                return new(expedition, SurvivalOperationStatus.AlreadyApplied,
                    "This forced-travel check was already resolved.", expedition.Survival.ForcedTravel.LastResolution.ConsequenceId);
            }
            return new(expedition, SurvivalOperationStatus.InputRequired, "No forced-travel check is currently due.");
        }
        if (pending.CheckId != command.CheckId)
        {
            return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                "The supplied check id does not match the current pending forced-travel check.");
        }
        if (command.Success && command.FailureComponents.Count > 0)
        {
            throw new InvalidOperationException("A successful forced-travel check can not include failure consequences.");
        }
        if (!command.Success && command.FailureComponents.Count == 0)
        {
            return new(expedition, SurvivalOperationStatus.InputRequired,
                $"Failure consequence '{policy.FailureConsequence}' does not encode a complete generic mutation. Supply explicitly resolved consequence components.");
        }

        var effects = expedition.Effects;
        Guid? consequenceId = null;
        SurvivalOperationStatus status = SurvivalOperationStatus.Applied;
        string detail = "Forced-travel check success recorded.";
        if (!command.Success)
        {
            consequenceId = pending.ConsequenceId;
            var consequence = BuildResolvedConsequence(
                pending.ConsequenceId, "forced-travel-failure", command.Target,
                command.FailureComponents, command.Provenance);
            var result = ExpeditionConsequenceEngine.Process(
                effects, PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure), consequence, expedition.Party);
            effects = result.State;
            status = ToSurvivalStatus(result.Status);
            detail = result.Detail;
        }

        var forced = expedition.Survival.ForcedTravel with
        {
            LastResolvedAmount = pending.AmountAtDue,
            PendingCheck = null,
            LastResolution = new ForcedTravelResolutionRecord(
                pending.CheckId, command.Success, pending.AmountAtDue, pending.Unit, consequenceId, command.Provenance)
        };
        var survival = expedition.Survival with { ForcedTravel = forced };
        survival.Validate(expedition.Party);
        var saved = await SaveAsync(expedition with { Survival = survival, Effects = effects }, command.ExpectedVersion, cancellationToken);
        return new(saved, status, detail, consequenceId);
    }

    public async Task<SurvivalOperationResult> ResetForcedTravelAsync(
        Guid expeditionId,
        string ownerUserId,
        ResetForcedTravelCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var transition = Phase11SurvivalEngine.ResetForcedTravel(expedition.Survival, expedition.Party, command.Provenance);
        var saved = await SaveAsync(expedition with { Survival = transition.State }, command.ExpectedVersion, cancellationToken);
        return new(saved, transition.Status, transition.Detail);
    }

    public async Task<SurvivalOperationResult> ResolveExposureAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveExposureCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = Phase11ProcedurePolicyResolver.ResolveExposure(expedition.CampaignProcedure);
        if (policy.Support != Phase11PolicySupport.Supported)
        {
            return PolicyFailure(expedition, policy.Support, policy.UnsupportedReason, Phase11GenericProcedureCatalog.ExposureModule);
        }
        if (command.OccurrenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Exposure occurrence id is required.");
        }
        if (policy.TargetScope != command.Target.Scope)
        {
            return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                "Resolved exposure target does not match the exact pinned exposure scope.");
        }
        command.Target.ValidateAgainst(expedition.Party);
        command.Provenance.Validate();
        var exposureKey = RequiredTrim(command.ExposureKey, "Exposure key");
        var progressUnit = string.IsNullOrWhiteSpace(command.ProgressUnit) ? null : command.ProgressUnit.Trim();
        if (!command.ProgressDelta.HasValue && command.ConsequenceComponents.Count == 0)
        {
            return new(expedition, SurvivalOperationStatus.InputRequired,
                "Exposure semantics remain unresolved. Supply explicit progress and/or structured consequence components.");
        }
        if (command.ProgressDelta.HasValue
            && (!double.IsFinite(command.ProgressDelta.Value) || command.ProgressDelta.Value == 0 || progressUnit is null))
        {
            throw new InvalidOperationException("Resolved exposure progress requires a non-zero finite delta and explicit unit.");
        }

        var exposure = expedition.Survival.Exposure.ToList();
        var priorOccurrence = exposure.SingleOrDefault(value => value.SourceOccurrenceIds.Contains(command.OccurrenceId));
        if (priorOccurrence is not null)
        {
            var sameIdentity = string.Equals(priorOccurrence.ExposureKey, exposureKey, StringComparison.Ordinal)
                && priorOccurrence.Target == command.Target
                && (!command.ProgressDelta.HasValue || string.Equals(priorOccurrence.Unit, progressUnit, StringComparison.Ordinal));
            return sameIdentity
                ? new(expedition, SurvivalOperationStatus.AlreadyApplied, "This exposure occurrence was already resolved.", command.OccurrenceId)
                : new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                    "This exposure occurrence id was already used by a different exposure target, key, or unit.", command.OccurrenceId);
        }

        var existingConsequence = expedition.Effects.AppliedConsequences.SingleOrDefault(value => value.ConsequenceId == command.OccurrenceId);
        if (existingConsequence is not null)
        {
            if (!string.Equals(existingConsequence.ConsequenceKey, "survival-exposure", StringComparison.Ordinal))
            {
                return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                    "This exposure occurrence id is already owned by a different consequence.", command.OccurrenceId);
            }
            if (command.ProgressDelta.HasValue)
            {
                return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                    "The exposure consequence was already recorded without matching exposure progress; automatic replay would risk a partial duplicate.", command.OccurrenceId);
            }
            return new(expedition, SurvivalOperationStatus.AlreadyApplied,
                "This consequence-only exposure occurrence was already resolved.", command.OccurrenceId);
        }

        var progress = command.ProgressDelta.HasValue
            ? exposure.SingleOrDefault(value =>
                string.Equals(value.ExposureKey, exposureKey, StringComparison.Ordinal)
                && value.Target == command.Target
                && string.Equals(value.Unit, progressUnit, StringComparison.Ordinal))
            : null;
        var progressChanged = false;
        if (command.ProgressDelta.HasValue)
        {
            var nextAmount = (progress?.Amount ?? 0) + command.ProgressDelta.Value;
            if (!double.IsFinite(nextAmount) || nextAmount < 0)
            {
                return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                    "Resolved exposure progress would become negative or non-finite; no state was changed.");
            }
            var next = new ExpeditionExposureProgress
            {
                Id = progress?.Id ?? Guid.NewGuid(),
                ExposureKey = exposureKey,
                Target = command.Target,
                Amount = nextAmount,
                Unit = progressUnit!,
                SourceOccurrenceIds = (progress?.SourceOccurrenceIds ?? []).Append(command.OccurrenceId).ToArray(),
                Provenance = (progress?.Provenance ?? []).Append(command.Provenance).ToArray()
            };
            if (progress is null) exposure.Add(next); else exposure[exposure.IndexOf(progress)] = next;
            progressChanged = true;
        }

        var effects = expedition.Effects;
        SurvivalOperationStatus resultStatus = SurvivalOperationStatus.Applied;
        string detail = "Explicit exposure progress recorded without changing environment truth.";
        var effectChanged = false;
        if (command.ConsequenceComponents.Count > 0)
        {
            var consequence = BuildResolvedConsequence(
                command.OccurrenceId, "survival-exposure", command.Target, command.ConsequenceComponents, command.Provenance);
            var result = ExpeditionConsequenceEngine.Process(
                effects, PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure), consequence, expedition.Party);
            effects = result.State;
            resultStatus = ToSurvivalStatus(result.Status);
            detail = result.Detail;
            effectChanged = result.StateChanged;
        }
        if (!progressChanged && !effectChanged)
        {
            return new(expedition, resultStatus, detail, command.ConsequenceComponents.Count > 0 ? command.OccurrenceId : null);
        }

        var survival = expedition.Survival with { Exposure = exposure };
        survival.Validate(expedition.Party);
        var saved = await SaveAsync(expedition with { Survival = survival, Effects = effects }, command.ExpectedVersion, cancellationToken);
        return new(saved, resultStatus, detail, command.ConsequenceComponents.Count > 0 ? command.OccurrenceId : null);
    }

    public async Task<SurvivalOperationResult> ResolveCampAsync(
        Guid expeditionId,
        string ownerUserId,
        ResolveCampCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = Phase11ProcedurePolicyResolver.ResolveCamping(expedition.CampaignProcedure);
        if (policy.Support != Phase11PolicySupport.Supported)
        {
            return PolicyFailure(expedition, policy.Support, policy.UnsupportedReason, "survival.camping");
        }
        if (expedition.Survival.Camp?.ResolutionId == command.ResolutionId)
        {
            return new(expedition, SurvivalOperationStatus.AlreadyApplied, "This camp resolution was already recorded.");
        }
        if (policy.ActivityBacked)
        {
            if (command.ActivityAssignmentIds.Count == 0)
            {
                return new(expedition, SurvivalOperationStatus.InputRequired,
                    "The pinned camping policy is activity-backed and requires explicit typed activity assignments.");
            }
            ValidateAssignments(expedition.Party, command.ActivityAssignmentIds);
        }
        command.Provenance.Validate();
        var camp = new ExpeditionCampState
        {
            ResolutionId = command.ResolutionId,
            Established = command.Established,
            ActivityAssignmentIds = command.ActivityAssignmentIds,
            ResolutionModel = policy.ResolutionModel,
            RestTriggerKey = string.IsNullOrWhiteSpace(command.RestTriggerKey) ? null : command.RestTriggerKey.Trim(),
            RestSafe = command.RestSafe,
            RestProlonged = command.RestProlonged,
            Provenance = command.Provenance
        };
        camp.Validate(expedition.Party);
        var survival = expedition.Survival with { Camp = camp };
        var saved = await SaveAsync(expedition with { Survival = survival }, command.ExpectedVersion, cancellationToken);
        var detail = command.RestTriggerKey is null
            ? "Camp resolution recorded. No rest/recovery semantics were inferred."
            : "Camp resolution recorded with an explicit rest trigger; effect recovery still requires an explicit Phase 10 recovery operation.";
        return new(saved, SurvivalOperationStatus.Applied, detail);
    }

    public async Task<SurvivalOperationResult> RecoverFromResolvedRestAsync(
        Guid expeditionId,
        string ownerUserId,
        RecoverFromResolvedRestCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var camp = expedition.Survival.Camp;
        if (camp is null || string.IsNullOrWhiteSpace(camp.RestTriggerKey))
        {
            return new(expedition, SurvivalOperationStatus.InputRequired,
                "No explicit camp/rest trigger has been resolved for Phase 10 recovery.");
        }
        if (!string.Equals(camp.RestTriggerKey, command.TriggerKey, StringComparison.Ordinal))
        {
            return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                "Requested recovery trigger does not match the explicit resolved camp/rest trigger.");
        }
        var effect = expedition.Effects.ActiveEffects.SingleOrDefault(value => value.Id == command.EffectId);
        var policy = PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure) with { RecoveryModel = effect?.RecoveryModel };
        var recovery = ExpeditionConsequenceEngine.Recover(
            expedition.Effects, policy, expedition.Party, command.EffectId, command.TriggerKey,
            command.LevelReduction, command.Clear, command.Provenance);
        if (!recovery.StateChanged)
        {
            return new(expedition, ToSurvivalStatus(recovery.Status), recovery.Detail);
        }
        var saved = await SaveAsync(expedition with { Effects = recovery.State }, command.ExpectedVersion, cancellationToken);
        return new(saved, ToSurvivalStatus(recovery.Status), recovery.Detail);
    }

    private async Task<SurvivalOperationResult> ProduceResourceConsequenceAsync(
        StoredExpedition expedition,
        long expectedVersion,
        Guid occurrenceId,
        string consequenceKey,
        ExpeditionEffectTarget target,
        IReadOnlyList<ResourceChangeConsequenceComponent> changes,
        ExpeditionConsequenceProvenance provenance,
        CancellationToken cancellationToken)
    {
        if (occurrenceId == Guid.Empty) throw new InvalidOperationException("Resolved operation occurrence id is required.");
        foreach (var change in changes) change.Validate();
        target.ValidateAgainst(expedition.Party);
        provenance.Validate();

        var existing = expedition.Effects.AppliedConsequences.SingleOrDefault(value => value.ConsequenceId == occurrenceId);
        if (existing is not null && !string.Equals(existing.ConsequenceKey, consequenceKey, StringComparison.Ordinal))
        {
            return new(expedition, SurvivalOperationStatus.RequiresAdjudication,
                $"Occurrence '{occurrenceId:D}' is already owned by consequence '{existing.ConsequenceKey}'.", occurrenceId);
        }

        var effects = expedition.Effects;
        if (existing is null)
        {
            var consequence = new ExpeditionConsequence
            {
                Id = occurrenceId,
                ConsequenceKey = consequenceKey,
                Category = ExpeditionConsequenceCategory.ResourceChange,
                Target = target,
                Components = changes,
                Provenance = provenance
            };
            var processed = ExpeditionConsequenceEngine.Process(
                effects, PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure), consequence, expedition.Party);
            effects = processed.State;
        }

        var resourceResult = ResourceConsequenceConsumer.Consume(
            expedition.Resources, effects, expedition.Party, occurrenceId, provenance);
        if (!resourceResult.StateChanged)
        {
            return new(expedition, ToSurvivalStatus(resourceResult.Status), resourceResult.Detail, occurrenceId);
        }
        var saved = await SaveAsync(expedition with { Resources = resourceResult.Resources, Effects = resourceResult.Effects },
            expectedVersion, cancellationToken);
        return new(saved, ToSurvivalStatus(resourceResult.Status), resourceResult.Detail, occurrenceId);
    }

    private static ExpeditionConsequence BuildResolvedConsequence(
        Guid id,
        string key,
        ExpeditionEffectTarget target,
        IReadOnlyList<ExpeditionConsequenceComponent> components,
        ExpeditionConsequenceProvenance provenance)
    {
        var category = components.Any(value => value is PersistentEffectChangeConsequenceComponent)
            ? ExpeditionConsequenceCategory.PersistentEffectChange
            : components.Any(value => value is ExternalStateConsequenceComponent)
                ? ExpeditionConsequenceCategory.DamageEndurance
                : throw new InvalidOperationException(
                    "Resolved survival failure requires persistent-effect and/or external-state consequence components.");
        return new ExpeditionConsequence
        {
            Id = id,
            ConsequenceKey = key,
            Category = category,
            Target = target,
            Components = components,
            Provenance = provenance
        };
    }

    private static void ValidateAssignments(CrawlPartySheet party, IReadOnlyList<Guid> assignmentIds)
    {
        if (assignmentIds.Any(value => value == Guid.Empty) || assignmentIds.Distinct().Count() != assignmentIds.Count)
        {
            throw new InvalidOperationException("Activity assignment ids must be non-empty and unique.");
        }
        foreach (var id in assignmentIds)
        {
            if (!party.ActivityAssignments.Any(value => value.Id == id))
            {
                throw new InvalidOperationException("Resolved operation references a participant activity assignment that does not exist.");
            }
        }
    }

    private static SurvivalOperationResult PolicyFailure(
        StoredExpedition expedition,
        Phase11PolicySupport support,
        string? reason,
        string moduleKey) => support == Phase11PolicySupport.None
        ? new(expedition, SurvivalOperationStatus.Unsupported,
            $"The exact pinned CampaignProcedure has no {moduleKey} focused policy.")
        : new(expedition, SurvivalOperationStatus.Unsupported,
            reason ?? $"The exact pinned {moduleKey} policy is unsupported.");

    private static string RequiredTrim(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"{label} is required.");

    private async Task<StoredExpedition> LoadAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        if (expedition.Version != expectedVersion)
        {
            throw new HexCrawlConcurrencyException("The expedition changed. Reload Phase 11 state before applying the operation.");
        }
        return expedition;
    }

    private async Task<StoredExpedition> SaveAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        expedition.Resources.Validate(expedition.Party);
        expedition.Survival.Validate(expedition.Party);
        expedition.Effects.Validate(expedition.Party);
        var result = await store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException("The expedition changed. Reload Phase 11 state before applying the operation."),
            _ => throw new HexCrawlNotFoundException("Crawl session was not found.")
        };
    }

    private static SurvivalOperationStatus ToSurvivalStatus(ExpeditionConsequenceStatus status) => status switch
    {
        ExpeditionConsequenceStatus.Applied => SurvivalOperationStatus.Applied,
        ExpeditionConsequenceStatus.AlreadyApplied => SurvivalOperationStatus.AlreadyApplied,
        ExpeditionConsequenceStatus.Recorded => SurvivalOperationStatus.Recorded,
        ExpeditionConsequenceStatus.InputRequired => SurvivalOperationStatus.InputRequired,
        ExpeditionConsequenceStatus.RequiresAdjudication => SurvivalOperationStatus.RequiresAdjudication,
        ExpeditionConsequenceStatus.Unsupported => SurvivalOperationStatus.Unsupported,
        ExpeditionConsequenceStatus.ExternalActionRequired => SurvivalOperationStatus.ExternalActionRequired,
        ExpeditionConsequenceStatus.Deferred => SurvivalOperationStatus.RequiresAdjudication,
        ExpeditionConsequenceStatus.Failed => SurvivalOperationStatus.RequiresAdjudication,
        _ => SurvivalOperationStatus.RequiresAdjudication
    };
}
