using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

/// <summary>
/// Projects explicit movement components from active Phase 10 effects into the existing Phase 8
/// movement composer. The effect remains authoritative state; the projected contributor is derived.
/// </summary>
public static class ExpeditionEffectMovementProjection
{
    public static IReadOnlyList<MovementCapabilityContributor> Project(StoredExpedition expedition)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        expedition.Effects.Validate(expedition.Party);
        return expedition.Effects.ActiveEffects
            .SelectMany(effect => Project(expedition.Party, effect))
            .OrderBy(value => value.Id)
            .ToArray();
    }

    public static MovementCapabilityComposition Compose(
        StoredExpedition expedition,
        MovementCompositionInput? input = null)
    {
        var projected = Project(expedition);
        var existing = input?.ResolvedContributors ?? [];
        var enriched = (input ?? new MovementCompositionInput()) with
        {
            ResolvedContributors = existing.Concat(projected).ToArray()
        };
        return MovementCapabilityComposer.Compose(expedition, enriched);
    }

    private static IEnumerable<MovementCapabilityContributor> Project(
        CrawlPartySheet party,
        ExpeditionEffect effect)
    {
        var movementUnitKey = ResolveMovementUnitKey(party, effect);
        foreach (var component in effect.MovementComponents)
        {
            var movementScope = effect.Target.Scope switch
            {
                ExpeditionEffectScope.Participant => MovementCapabilityScope.Participant,
                ExpeditionEffectScope.Mount or ExpeditionEffectScope.Vehicle => MovementCapabilityScope.MovementUnit,
                _ => MovementCapabilityScope.Party
            };
            var provenance = effect.Provenance.Count == 0
                ? $"Persistent effect: {effect.EffectKey}"
                : string.Join("; ", effect.Provenance.Select(value =>
                    $"{value.SourceKind}:{value.SourceKey}"));
            yield return new MovementCapabilityContributor
            {
                Id = component.Id,
                Kind = MovementCapabilityContributorKind.PersistentEffect,
                Key = $"effect:{effect.EffectKey}:{component.Key}",
                Operation = component.Operation,
                Scope = movementScope,
                Value = component.Value,
                Unit = component.Unit,
                PerUnit = component.PerUnit,
                DistanceUnit = component.DistanceUnit,
                SymbolicValue = component.SymbolicValue,
                ParticipantId = effect.Target.Scope == ExpeditionEffectScope.Participant
                    ? effect.Target.TargetId
                    : null,
                MovementUnitKey = movementUnitKey,
                Provenance = provenance,
                Note = component.Note,
                Enabled = true
            };
        }
    }

    private static string? ResolveMovementUnitKey(
        CrawlPartySheet party,
        ExpeditionEffect effect)
    {
        if (effect.Target.Scope is not (ExpeditionEffectScope.Mount or ExpeditionEffectScope.Vehicle))
        {
            return null;
        }

        var expectedKind = effect.Target.Scope == ExpeditionEffectScope.Mount
            ? MovementCapabilityContributorKind.Mount
            : MovementCapabilityContributorKind.Vehicle;
        return party.MovementContributors.Single(value =>
                value.Id == effect.Target.TargetId && value.Kind == expectedKind)
            .MovementUnitKey;
    }
}