using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record ApplyExpeditionConsequenceCommand(
    long ExpectedVersion,
    ExpeditionConsequence Consequence);

public sealed record ApplyExpeditionConsequenceResult(
    StoredExpedition Expedition,
    ConsequenceProcessingResult Processing);

public sealed record UpsertExpeditionEffectCommand(
    long ExpectedVersion,
    ExpeditionEffect Effect,
    ExpeditionConsequenceProvenance Provenance);

public sealed record RecoverExpeditionEffectCommand(
    long ExpectedVersion,
    string TriggerKey,
    int? LevelReduction,
    bool Clear,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ClearExpeditionEffectCommand(
    long ExpectedVersion,
    ExpeditionConsequenceProvenance Provenance);

public sealed record ResolvePendingConsequenceCommand(
    long ExpectedVersion,
    string ResolutionNote,
    ExpeditionConsequenceProvenance Provenance);

public sealed class ExpeditionEffectService(
    IHexCrawlStore store,
    HexCrawlService coreService)
{
    public async Task<ApplyExpeditionConsequenceResult> ApplyConsequenceAsync(
        Guid expeditionId,
        string ownerUserId,
        ApplyExpeditionConsequenceCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        command.Consequence.ValidateAgainst(expedition.Party);
        expedition.Effects.Validate(expedition.Party);

        if (expedition.Effects.AppliedConsequences.Any(value => value.ConsequenceId == command.Consequence.Id))
        {
            var prior = expedition.Effects.AppliedConsequences.Single(value => value.ConsequenceId == command.Consequence.Id);
            return new ApplyExpeditionConsequenceResult(
                expedition,
                new ConsequenceProcessingResult(
                    expedition.Effects,
                    ExpeditionConsequenceStatus.AlreadyApplied,
                    $"Consequence '{command.Consequence.Id:D}' was already consumed as {prior.Status}.",
                    prior.EffectIds,
                    false));
        }

        if (command.Consequence.Category == ExpeditionConsequenceCategory.TimeDelay)
        {
            return await ApplyTimeDelayAsync(expedition, command, cancellationToken);
        }

        var policy = PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure);
        var result = ExpeditionConsequenceEngine.Process(
            expedition.Effects,
            policy,
            command.Consequence,
            expedition.Party);
        if (!result.StateChanged)
        {
            return new ApplyExpeditionConsequenceResult(expedition, result);
        }

        var saved = await SaveAsync(
            expedition with { Effects = result.State },
            command.ExpectedVersion,
            cancellationToken);
        return new ApplyExpeditionConsequenceResult(saved, result with { State = saved.Effects });
    }

    public async Task<StoredExpedition> UpsertEffectAsync(
        Guid expeditionId,
        string ownerUserId,
        UpsertExpeditionEffectCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure);
        var effect = command.Effect;
        if (string.IsNullOrWhiteSpace(effect.RecoveryModel)
            && policy.Support == PersistentEffectPolicySupport.Supported
            && policy.Scope == effect.Target.Scope
            && policy.EffectKinds.Contains(effect.EffectKey, StringComparer.Ordinal))
        {
            effect = effect with { RecoveryModel = policy.RecoveryModel };
        }
        var effects = ExpeditionConsequenceEngine.UpsertManualEffect(
            expedition.Effects,
            expedition.Party,
            effect,
            command.Provenance);
        return await SaveAsync(expedition with { Effects = effects }, command.ExpectedVersion, cancellationToken);
    }

    public async Task<(StoredExpedition Expedition, EffectRecoveryResult Recovery)> RecoverAsync(
        Guid expeditionId,
        Guid effectId,
        string ownerUserId,
        RecoverExpeditionEffectCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var policy = PersistentEffectPolicyResolver.Resolve(expedition.CampaignProcedure);
        var result = ExpeditionConsequenceEngine.Recover(
            expedition.Effects,
            policy,
            expedition.Party,
            effectId,
            command.TriggerKey,
            command.LevelReduction,
            command.Clear,
            command.Provenance);
        if (!result.StateChanged)
        {
            return (expedition, result);
        }
        var saved = await SaveAsync(expedition with { Effects = result.State }, command.ExpectedVersion, cancellationToken);
        return (saved, result with { State = saved.Effects });
    }

    public async Task<StoredExpedition> ClearEffectAsync(
        Guid expeditionId,
        Guid effectId,
        string ownerUserId,
        ClearExpeditionEffectCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var effects = ExpeditionConsequenceEngine.ClearManualEffect(
            expedition.Effects,
            expedition.Party,
            effectId,
            command.Provenance);
        return await SaveAsync(expedition with { Effects = effects }, command.ExpectedVersion, cancellationToken);
    }

    public async Task<StoredExpedition> ResolvePendingAsync(
        Guid expeditionId,
        Guid consequenceId,
        string ownerUserId,
        ResolvePendingConsequenceCommand command,
        CancellationToken cancellationToken = default)
    {
        var expedition = await LoadAsync(expeditionId, ownerUserId, command.ExpectedVersion, cancellationToken);
        var effects = ExpeditionConsequenceEngine.ResolvePending(
            expedition.Effects,
            expedition.Party,
            consequenceId,
            command.Provenance,
            command.ResolutionNote);
        return await SaveAsync(expedition with { Effects = effects }, command.ExpectedVersion, cancellationToken);
    }

    private async Task<ApplyExpeditionConsequenceResult> ApplyTimeDelayAsync(
        StoredExpedition expedition,
        ApplyExpeditionConsequenceCommand command,
        CancellationToken cancellationToken)
    {
        var components = command.Consequence.Components.OfType<TimeDelayConsequenceComponent>().ToArray();
        if (components.Length != command.Consequence.Components.Count)
        {
            throw new InvalidOperationException("A time-delay consequence can contain only time-delay components.");
        }
        var delay = components.Aggregate(TimeSpan.Zero, (current, component) => current + component.ToTimeSpan());
        if (delay <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Resolved time delay must be positive.");
        }

        var hasActiveInterval = expedition.Runtime switch
        {
            ExpeditionState spatial => spatial.ActiveWatch is not null,
            NonSpatialSessionState nonSpatial => nonSpatial.ActiveWatch is not null,
            _ => true
        };
        if (hasActiveInterval)
        {
            var detail = "Time delay can not mutate the expedition clock while an active interval is in progress without corrupting its remaining duration.";
            var record = new AppliedConsequenceRecord(
                command.Consequence.Id,
                command.Consequence.ConsequenceKey,
                ExpeditionConsequenceStatus.Deferred,
                [],
                command.Consequence.Provenance,
                detail);
            var pending = new PendingExpeditionConsequence(
                command.Consequence,
                ExpeditionConsequenceStatus.Deferred,
                detail,
                "Complete or explicitly resolve the active interval, then adjudicate the structured delay.");
            var state = expedition.Effects with
            {
                AppliedConsequences = expedition.Effects.AppliedConsequences.Append(record).ToArray(),
                PendingConsequences = expedition.Effects.PendingConsequences.Append(pending).ToArray()
            };
            state.Validate(expedition.Party);
            var savedDeferred = await SaveAsync(
                expedition with { Effects = state },
                command.ExpectedVersion,
                cancellationToken);
            return new ApplyExpeditionConsequenceResult(
                savedDeferred,
                new ConsequenceProcessingResult(
                    savedDeferred.Effects,
                    ExpeditionConsequenceStatus.Deferred,
                    detail,
                    [],
                    true));
        }

        var recorded = ExpeditionConsequenceEngine.RecordAppliedExternalMutation(
            expedition.Effects,
            command.Consequence,
            expedition.Party,
            $"Advanced the existing expedition clock by {delay} without creating a second time authority.");
        if (recorded.Status == ExpeditionConsequenceStatus.AlreadyApplied)
        {
            return new ApplyExpeditionConsequenceResult(expedition, recorded);
        }

        var runtime = expedition.Runtime switch
        {
            ExpeditionState spatial => spatial with { ElapsedTravelTime = spatial.ElapsedTravelTime + delay },
            NonSpatialSessionState nonSpatial => nonSpatial with { ElapsedTime = nonSpatial.ElapsedTime + delay },
            _ => throw new InvalidOperationException("Unsupported crawl runtime state for time-delay application.")
        };
        var saved = await SaveAsync(
            expedition with { Runtime = runtime, Effects = recorded.State },
            command.ExpectedVersion,
            cancellationToken);
        return new ApplyExpeditionConsequenceResult(saved, recorded with { State = saved.Effects });
    }

    private async Task<StoredExpedition> LoadAsync(
        Guid expeditionId,
        string ownerUserId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var expedition = await coreService.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        if (expedition.Version != expectedVersion)
        {
            throw new HexCrawlConcurrencyException(
                "The expedition was changed by another request. Reload current effect state before applying this operation.");
        }
        return expedition;
    }

    private async Task<StoredExpedition> SaveAsync(
        StoredExpedition expedition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        expedition.Effects.Validate(expedition.Party);
        var result = await store.SaveExpeditionAsync(expedition, expectedVersion, cancellationToken);
        return result.Outcome switch
        {
            SaveOutcome.Saved => result.Value!,
            SaveOutcome.Conflict => throw new HexCrawlConcurrencyException(
                "The expedition was changed by another request. Reload current effect state before applying this operation."),
            _ => throw new HexCrawlNotFoundException("Crawl session was not found.")
        };
    }
}
