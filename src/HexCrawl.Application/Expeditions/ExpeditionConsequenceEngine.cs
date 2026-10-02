using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record ConsequenceProcessingResult(
    ExpeditionEffectState State,
    ExpeditionConsequenceStatus Status,
    string Detail,
    IReadOnlyList<Guid> EffectIds,
    bool StateChanged);

public sealed record EffectRecoveryResult(
    ExpeditionEffectState State,
    ExpeditionConsequenceStatus Status,
    string Detail,
    bool StateChanged);

/// <summary>
/// Pure consequence/effect state transition logic. It never calls providers, reads preset identity,
/// mutates environment truth, runs journey/forced-travel producers, or composes movement itself.
/// </summary>
public static class ExpeditionConsequenceEngine
{
    private static readonly HashSet<string> ExplicitLevelAccumulationModels = new(StringComparer.Ordinal)
    {
        "levels",
        "per-failed-check",
        "journey-events"
    };

    public static ConsequenceProcessingResult Process(
        ExpeditionEffectState state,
        PersistentEffectPolicy policy,
        ExpeditionConsequence consequence,
        CrawlPartySheet party)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(consequence);
        ArgumentNullException.ThrowIfNull(party);
        consequence.ValidateAgainst(party);
        state.Validate(party);

        var existingRecord = state.AppliedConsequences.SingleOrDefault(value => value.ConsequenceId == consequence.Id);
        if (existingRecord is not null)
        {
            return new ConsequenceProcessingResult(
                state,
                ExpeditionConsequenceStatus.AlreadyApplied,
                $"Consequence '{consequence.Id:D}' was already consumed as {existingRecord.Status}.",
                existingRecord.EffectIds,
                false);
        }

        return consequence.Category switch
        {
            ExpeditionConsequenceCategory.ExposureFatigue or ExpeditionConsequenceCategory.PersistentEffectChange
                => ApplyPersistentEffectChange(state, policy, consequence, party),
            ExpeditionConsequenceCategory.ResourceChange => Pend(
                state, consequence, ExpeditionConsequenceStatus.Deferred,
                "Resource inventory and survival execution are owned by Phase 11.",
                "Apply this structured resource change through Phase 11 or resolve it manually."),
            ExpeditionConsequenceCategory.DamageEndurance => Pend(
                state, consequence, ExpeditionConsequenceStatus.ExternalActionRequired,
                "Participant damage/endurance is character-owned state, not Hex Crawl expedition state.",
                "Apply the structured consequence in the owning character Tool or resolve it manually."),
            ExpeditionConsequenceCategory.NavigationChange => Pend(
                state, consequence, ExpeditionConsequenceStatus.Deferred,
                "The supplied navigation consequence has no Phase 10 mutation contract that is safer than the existing navigation workflow.",
                "Resolve it through the established navigation operation or record a DM adjudication."),
            ExpeditionConsequenceCategory.EncounterCircumstance => Pend(
                state, consequence, ExpeditionConsequenceStatus.Deferred,
                "Encounter circumstances are preserved for the Phase 13 encounter handoff expansion.",
                "Carry this circumstance into the later encounter handoff or resolve it manually."),
            ExpeditionConsequenceCategory.MovementChange => Pend(
                state, consequence, ExpeditionConsequenceStatus.RequiresAdjudication,
                "A transient movement change does not state whether it is one-shot or ongoing persistent effect state.",
                "Apply an explicit persistent effect with a resolved movement component or adjudicate this occurrence."),
            ExpeditionConsequenceCategory.Custom => Pend(
                state, consequence, ExpeditionConsequenceStatus.Unsupported,
                "Custom consequence identity is preserved, but Phase 10 does not execute arbitrary custom semantics.",
                "Use a consumer that understands this custom consequence or resolve it manually."),
            ExpeditionConsequenceCategory.TimeDelay => throw new InvalidOperationException(
                "Time-delay consequences must be routed through ExpeditionEffectService so the existing runtime clock is the only time authority."),
            _ => throw new InvalidOperationException("Consequence category is not supported.")
        };
    }

    public static ConsequenceProcessingResult RecordAppliedExternalMutation(
        ExpeditionEffectState state,
        ExpeditionConsequence consequence,
        CrawlPartySheet party,
        string detail)
    {
        consequence.ValidateAgainst(party);
        state.Validate(party);
        var existing = state.AppliedConsequences.SingleOrDefault(value => value.ConsequenceId == consequence.Id);
        if (existing is not null)
        {
            return new ConsequenceProcessingResult(
                state,
                ExpeditionConsequenceStatus.AlreadyApplied,
                $"Consequence '{consequence.Id:D}' was already consumed as {existing.Status}.",
                existing.EffectIds,
                false);
        }

        var record = AppliedRecord(consequence, ExpeditionConsequenceStatus.Applied, [], detail);
        var updated = state with
        {
            AppliedConsequences = state.AppliedConsequences.Append(record).ToArray()
        };
        updated.Validate(party);
        return new ConsequenceProcessingResult(updated, ExpeditionConsequenceStatus.Applied, detail, [], true);
    }

    public static EffectRecoveryResult Recover(
        ExpeditionEffectState state,
        PersistentEffectPolicy policy,
        CrawlPartySheet party,
        Guid effectId,
        string triggerKey,
        int? levelReduction,
        bool clear,
        ExpeditionConsequenceProvenance provenance)
    {
        state.Validate(party);
        provenance.Validate();
        if (effectId == Guid.Empty)
        {
            throw new InvalidOperationException("Effect id is required for recovery.");
        }
        if (string.IsNullOrWhiteSpace(triggerKey))
        {
            throw new InvalidOperationException("Recovery trigger key is required.");
        }
        if (levelReduction is <= 0)
        {
            throw new InvalidOperationException("Explicit recovery level reduction must be positive.");
        }

        var effect = state.ActiveEffects.SingleOrDefault(value => value.Id == effectId);
        if (effect is null)
        {
            return new EffectRecoveryResult(state, ExpeditionConsequenceStatus.Recorded, "The effect is no longer active.", false);
        }

        var manual = string.Equals(triggerKey, "manual", StringComparison.Ordinal);
        var supportedTrigger = manual || string.Equals(policy.RecoveryModel, triggerKey, StringComparison.Ordinal);
        if (!supportedTrigger)
        {
            return new EffectRecoveryResult(
                state,
                ExpeditionConsequenceStatus.Recorded,
                $"Recovery trigger '{triggerKey}' does not match the pinned recovery model and changed no state.",
                false);
        }

        if (!clear && !levelReduction.HasValue)
        {
            return new EffectRecoveryResult(
                state,
                ExpeditionConsequenceStatus.RequiresAdjudication,
                $"Recovery model '{policy.RecoveryModel ?? "none"}' identifies a trigger but does not define a generic reduction amount.",
                false);
        }

        var active = state.ActiveEffects.ToList();
        var beforeLevel = effect.Level;
        var beforeMagnitude = effect.Magnitude;
        int? afterLevel = effect.Level;
        double? afterMagnitude = effect.Magnitude;
        string operation;

        if (clear)
        {
            active.Remove(effect);
            afterLevel = null;
            afterMagnitude = null;
            operation = $"recover:{triggerKey}:clear";
        }
        else
        {
            if (!effect.Level.HasValue)
            {
                return new EffectRecoveryResult(
                    state,
                    ExpeditionConsequenceStatus.RequiresAdjudication,
                    "The effect has no generic level to reduce. Supply an explicit clear/manual mutation instead.",
                    false);
            }
            var next = Math.Max(0, effect.Level.Value - levelReduction!.Value);
            if (next == 0)
            {
                active.Remove(effect);
                afterLevel = null;
            }
            else
            {
                var updatedEffect = effect with
                {
                    Level = next,
                    Provenance = effect.Provenance.Append(provenance).ToArray()
                };
                active[active.IndexOf(effect)] = updatedEffect;
                afterLevel = next;
            }
            operation = $"recover:{triggerKey}:reduce";
        }

        var audit = new ExpeditionEffectAuditRecord(
            Guid.NewGuid(), effect.Id, effect.EffectKey, operation,
            beforeLevel, afterLevel, beforeMagnitude, afterMagnitude, null, provenance);
        var updatedState = state with
        {
            ActiveEffects = active,
            History = state.History.Append(audit).ToArray()
        };
        updatedState.Validate(party);
        return new EffectRecoveryResult(
            updatedState,
            ExpeditionConsequenceStatus.Applied,
            clear ? "Effect cleared by explicit recovery." : "Effect reduced by explicit recovery.",
            true);
    }

    public static ExpeditionEffectState UpsertManualEffect(
        ExpeditionEffectState state,
        CrawlPartySheet party,
        ExpeditionEffect effect,
        ExpeditionConsequenceProvenance provenance)
    {
        provenance.Validate();
        effect.Validate(party);
        state.Validate(party);
        var active = state.ActiveEffects.ToList();
        var existing = active.SingleOrDefault(value => value.Id == effect.Id);
        if (existing is null)
        {
            if (effect.MergeKey is { } mergeKey && active.Any(value => value.MergeKey == mergeKey))
            {
                throw new InvalidOperationException("Manual effect merge key already belongs to another active effect.");
            }
            var added = effect with { Provenance = effect.Provenance.Append(provenance).ToArray() };
            active.Add(added);
        }
        else
        {
            var index = active.IndexOf(existing);
            active[index] = effect with
            {
                Provenance = existing.Provenance.Concat(effect.Provenance).Append(provenance).ToArray(),
                SourceConsequenceIds = existing.SourceConsequenceIds
                    .Concat(effect.SourceConsequenceIds).Distinct().ToArray()
            };
        }

        var current = active.Single(value => value.Id == effect.Id);
        var audit = new ExpeditionEffectAuditRecord(
            Guid.NewGuid(), current.Id, current.EffectKey,
            existing is null ? "manual:add" : "manual:update",
            existing?.Level, current.Level, existing?.Magnitude, current.Magnitude,
            null, provenance);
        var updated = state with
        {
            ActiveEffects = active,
            History = state.History.Append(audit).ToArray()
        };
        updated.Validate(party);
        return updated;
    }

    public static ExpeditionEffectState ClearManualEffect(
        ExpeditionEffectState state,
        CrawlPartySheet party,
        Guid effectId,
        ExpeditionConsequenceProvenance provenance)
    {
        provenance.Validate();
        state.Validate(party);
        var effect = state.ActiveEffects.SingleOrDefault(value => value.Id == effectId)
            ?? throw new InvalidOperationException("The requested expedition effect is not active.");
        var audit = new ExpeditionEffectAuditRecord(
            Guid.NewGuid(), effect.Id, effect.EffectKey, "manual:clear",
            effect.Level, null, effect.Magnitude, null, null, provenance);
        var updated = state with
        {
            ActiveEffects = state.ActiveEffects.Where(value => value.Id != effectId).ToArray(),
            History = state.History.Append(audit).ToArray()
        };
        updated.Validate(party);
        return updated;
    }

    public static ExpeditionEffectState ResolvePending(
        ExpeditionEffectState state,
        CrawlPartySheet party,
        Guid consequenceId,
        ExpeditionConsequenceProvenance provenance,
        string resolutionNote)
    {
        provenance.Validate();
        state.Validate(party);
        _ = state.PendingConsequences.SingleOrDefault(value => value.Consequence.Id == consequenceId)
            ?? throw new InvalidOperationException("The requested pending consequence was not found.");
        if (string.IsNullOrWhiteSpace(resolutionNote))
        {
            throw new InvalidOperationException("Deferred consequence resolution requires a note describing the adjudication or external action.");
        }

        var records = state.AppliedConsequences
            .Select(value => value.ConsequenceId == consequenceId
                ? value with
                {
                    Status = ExpeditionConsequenceStatus.Recorded,
                    Detail = $"{value.Detail} Resolution: {resolutionNote.Trim()}",
                    ResolutionProvenance = provenance
                }
                : value)
            .ToArray();
        var updated = state with
        {
            AppliedConsequences = records,
            PendingConsequences = state.PendingConsequences
                .Where(value => value.Consequence.Id != consequenceId)
                .ToArray()
        };
        updated.Validate(party);
        return updated;
    }

    private static ConsequenceProcessingResult ApplyPersistentEffectChange(
        ExpeditionEffectState state,
        PersistentEffectPolicy policy,
        ExpeditionConsequence consequence,
        CrawlPartySheet party)
    {
        if (policy.Support == PersistentEffectPolicySupport.None)
        {
            return Pend(state, consequence, ExpeditionConsequenceStatus.Unsupported,
                "The exact pinned CampaignProcedure has no effects.expedition policy.",
                "Add an explicit manual effect or use a procedure with a supported effect policy.");
        }
        if (policy.Support == PersistentEffectPolicySupport.Unsupported)
        {
            return Pend(state, consequence, ExpeditionConsequenceStatus.Unsupported,
                policy.UnsupportedReason ?? "The exact pinned effect policy is unsupported.",
                "Adjudicate the consequence manually without re-resolving the current preset catalog.");
        }
        if (policy.Scope != consequence.Target.Scope)
        {
            return Pend(state, consequence, ExpeditionConsequenceStatus.RequiresAdjudication,
                $"Consequence scope '{consequence.Target.Scope}' does not match pinned effect scope '{policy.Scope}'.",
                "Supply an explicitly resolved manual effect or correct the consequence target.");
        }

        var components = consequence.Components.OfType<PersistentEffectChangeConsequenceComponent>().ToArray();
        var active = state.ActiveEffects.ToList();
        var audit = state.History.ToList();
        var touched = new List<Guid>();
        foreach (var component in components)
        {
            if (!policy.EffectKinds.Contains(component.EffectKey, StringComparer.Ordinal))
            {
                return Pend(state, consequence, ExpeditionConsequenceStatus.Unsupported,
                    $"Effect key '{component.EffectKey}' is not declared by the exact pinned effects.expedition policy.",
                    "Use a policy that declares the effect key or add a manual effect explicitly.");
            }

            if (!CanApply(policy.AccumulationModel!, component, out var reason))
            {
                return Pend(state, consequence, ExpeditionConsequenceStatus.RequiresAdjudication,
                    reason!,
                    "Supply an explicitly resolved effect change or adjudicate it manually.");
            }

            var mergeKey = $"policy:{component.EffectKey}:{consequence.Target.MergeIdentity}";
            var existing = active.SingleOrDefault(value => string.Equals(value.MergeKey, mergeKey, StringComparison.Ordinal));
            var beforeLevel = existing?.Level;
            var beforeMagnitude = existing?.Magnitude;
            var next = ApplyComponent(existing, mergeKey, consequence, component, policy.RecoveryModel!);
            if (next is null)
            {
                if (existing is not null)
                {
                    active.Remove(existing);
                    touched.Add(existing.Id);
                    audit.Add(new ExpeditionEffectAuditRecord(
                        Guid.NewGuid(), existing.Id, existing.EffectKey, $"consequence:{component.Operation}:clear",
                        beforeLevel, null, beforeMagnitude, null, consequence.Id, consequence.Provenance));
                }
                continue;
            }

            if (existing is null)
            {
                active.Add(next);
            }
            else
            {
                active[active.IndexOf(existing)] = next;
            }
            touched.Add(next.Id);
            audit.Add(new ExpeditionEffectAuditRecord(
                Guid.NewGuid(), next.Id, next.EffectKey, $"consequence:{component.Operation}",
                beforeLevel, next.Level, beforeMagnitude, next.Magnitude,
                consequence.Id, consequence.Provenance));
        }

        var unresolved = consequence.Components
            .Where(value => value is not PersistentEffectChangeConsequenceComponent)
            .ToArray();
        var status = ExpeditionConsequenceStatus.Applied;
        var detail = "Structured persistent effect consequence applied through the exact pinned effect policy.";
        PendingExpeditionConsequence? pending = null;
        if (unresolved.Length > 0)
        {
            var disposition = DescribeUnresolved(unresolved);
            status = disposition.Status;
            detail = $"Structured persistent effect components applied through the exact pinned effect policy. {disposition.Reason}";
            pending = new PendingExpeditionConsequence(
                consequence,
                disposition.Status,
                disposition.Reason,
                disposition.RequiredAction)
            {
                UnresolvedComponents = unresolved
            };
        }

        var record = AppliedRecord(consequence, status, touched.Distinct().ToArray(), detail);
        var updated = state with
        {
            ActiveEffects = active,
            AppliedConsequences = state.AppliedConsequences.Append(record).ToArray(),
            PendingConsequences = pending is null
                ? state.PendingConsequences
                : state.PendingConsequences.Append(pending).ToArray(),
            History = audit
        };
        updated.Validate(party);
        return new ConsequenceProcessingResult(
            updated,
            status,
            record.Detail!,
            record.EffectIds,
            true);
    }

    private static (ExpeditionConsequenceStatus Status, string Reason, string RequiredAction) DescribeUnresolved(
        IReadOnlyList<ExpeditionConsequenceComponent> components)
    {
        if (components.Any(value => value is CustomConsequenceComponent))
        {
            return (
                ExpeditionConsequenceStatus.Unsupported,
                "The occurrence also contains custom structured components that Phase 10 can not execute generically.",
                "Resolve the preserved custom components with a consumer that understands them or record a DM adjudication.");
        }
        if (components.Any(value => value is MovementChangeConsequenceComponent))
        {
            return (
                ExpeditionConsequenceStatus.RequiresAdjudication,
                "The occurrence also contains transient movement components whose duration semantics are unresolved.",
                "Resolve the preserved movement components explicitly without reapplying the persistent effect portion.");
        }
        if (components.Any(value => value is ExternalStateConsequenceComponent))
        {
            return (
                ExpeditionConsequenceStatus.ExternalActionRequired,
                "The occurrence also contains character-owned state changes that Hex Crawl must not apply.",
                "Apply the preserved external-state components in the owning Tool, then record their resolution here.");
        }
        if (components.Any(value => value is ResourceChangeConsequenceComponent))
        {
            return (
                ExpeditionConsequenceStatus.Deferred,
                "The occurrence also contains resource changes owned by the Phase 11 survival/resource workflow.",
                "Apply the preserved resource components through Phase 11 or resolve them manually.");
        }
        if (components.Any(value => value is NavigationConsequenceComponent))
        {
            return (
                ExpeditionConsequenceStatus.Deferred,
                "The occurrence also contains navigation changes that remain owned by the established navigation workflow.",
                "Resolve the preserved navigation components through that workflow or record a DM adjudication.");
        }
        if (components.Any(value => value is EncounterCircumstanceConsequenceComponent))
        {
            return (
                ExpeditionConsequenceStatus.Deferred,
                "The occurrence also contains encounter circumstances preserved for the encounter handoff workflow.",
                "Carry the preserved encounter components into that workflow or resolve them manually.");
        }
        if (components.Any(value => value is TimeDelayConsequenceComponent))
        {
            return (
                ExpeditionConsequenceStatus.Deferred,
                "The occurrence also contains time-delay components that must use the existing runtime clock.",
                "Route the preserved time-delay components through the expedition effect service or resolve them manually.");
        }

        return (
            ExpeditionConsequenceStatus.RequiresAdjudication,
            "The occurrence contains additional structured components that Phase 10 did not consume.",
            "Resolve the preserved components explicitly without reapplying the persistent effect portion.");
    }

    private static bool CanApply(
        string accumulationModel,
        PersistentEffectChangeConsequenceComponent component,
        out string? reason)
    {
        if (ExplicitLevelAccumulationModels.Contains(accumulationModel)
            && component.Operation is PersistentEffectChangeOperation.AdjustLevel or PersistentEffectChangeOperation.SetLevel)
        {
            reason = null;
            return true;
        }

        if (component.ExplicitlyResolved)
        {
            reason = null;
            return true;
        }

        reason = accumulationModel switch
        {
            "procedure-defined" => "The pinned procedure-defined accumulation model does not provide a generic formula for this occurrence.",
            _ => $"Accumulation model '{accumulationModel}' is not generically executable for this effect change without an explicitly resolved value."
        };
        return false;
    }

    private static ExpeditionEffect? ApplyComponent(
        ExpeditionEffect? existing,
        string mergeKey,
        ExpeditionConsequence consequence,
        PersistentEffectChangeConsequenceComponent component,
        string recoveryModel)
    {
        if (component.Operation == PersistentEffectChangeOperation.Clear)
        {
            return null;
        }

        var id = existing?.Id ?? Guid.NewGuid();
        int? level = existing?.Level;
        double? magnitude = existing?.Magnitude;
        string? unit = existing?.Unit;
        string? effectState = existing?.State;

        switch (component.Operation)
        {
            case PersistentEffectChangeOperation.AdjustLevel:
                level = Math.Max(0, (level ?? 0) + component.LevelDelta!.Value);
                if (level == 0)
                {
                    return null;
                }
                break;
            case PersistentEffectChangeOperation.SetLevel:
                level = component.Level!.Value;
                if (level == 0)
                {
                    return null;
                }
                break;
            case PersistentEffectChangeOperation.SetMagnitude:
                magnitude = component.Magnitude;
                unit = component.Unit;
                break;
            case PersistentEffectChangeOperation.SetState:
                effectState = component.State;
                break;
        }

        var movement = MergeMovement(existing?.MovementComponents ?? [], component.MovementComponents);
        return new ExpeditionEffect
        {
            Id = id,
            EffectKey = component.EffectKey,
            Target = consequence.Target,
            MergeKey = mergeKey,
            Level = level,
            Magnitude = magnitude,
            Unit = unit,
            State = effectState,
            MovementComponents = movement,
            SourceConsequenceIds = (existing?.SourceConsequenceIds ?? [])
                .Append(consequence.Id).Distinct().ToArray(),
            Provenance = (existing?.Provenance ?? [])
                .Append(consequence.Provenance).ToArray(),
            RecoveryModel = recoveryModel
        };
    }

    private static IReadOnlyList<MovementConsequenceComponent> MergeMovement(
        IReadOnlyList<MovementConsequenceComponent> existing,
        IReadOnlyList<MovementConsequenceComponent> incoming)
    {
        if (incoming.Count == 0)
        {
            return existing;
        }
        var result = existing.ToDictionary(value => value.Id);
        foreach (var item in incoming)
        {
            result[item.Id] = item;
        }
        return result.Values.OrderBy(value => value.Id).ToArray();
    }

    private static ConsequenceProcessingResult Pend(
        ExpeditionEffectState state,
        ExpeditionConsequence consequence,
        ExpeditionConsequenceStatus status,
        string reason,
        string requiredAction,
        IReadOnlyList<ExpeditionConsequenceComponent>? unresolvedComponents = null)
    {
        var record = AppliedRecord(consequence, status, [], reason);
        var pending = new PendingExpeditionConsequence(consequence, status, reason, requiredAction)
        {
            UnresolvedComponents = unresolvedComponents ?? consequence.Components
        };
        var updated = state with
        {
            AppliedConsequences = state.AppliedConsequences.Append(record).ToArray(),
            PendingConsequences = state.PendingConsequences.Append(pending).ToArray()
        };
        return new ConsequenceProcessingResult(updated, status, reason, [], true);
    }

    private static AppliedConsequenceRecord AppliedRecord(
        ExpeditionConsequence consequence,
        ExpeditionConsequenceStatus status,
        IReadOnlyList<Guid> effectIds,
        string detail) =>
        new(consequence.Id, consequence.ConsequenceKey, status, effectIds, consequence.Provenance, detail);
}