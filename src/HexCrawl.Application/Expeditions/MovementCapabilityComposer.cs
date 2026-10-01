using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application;

public enum MovementCompositionStatus
{
    Resolved,
    ReferenceFallback,
    InputRequired,
    RequiresAdjudication,
    Unsupported,
    Unavailable,
    Failed
}

public enum MovementReferenceUse
{
    None,
    AuthoritativeBase,
    Fallback,
    InformationalOnly
}

public sealed record MovementCompositionInput(
    string? PaceKey = null,
    string? TerrainKey = null,
    string? RouteKey = null,
    IReadOnlyList<MovementCapabilityContributor>? ResolvedContributors = null,
    MovementCompositionStatus? ExternalStatus = null,
    IReadOnlyList<string>? MissingInputs = null,
    string? ExternalDiagnostic = null);

public sealed record MovementAppliedContributor(
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
    string? Detail);

public sealed record MovementCapabilityComposition(
    MovementCompositionPolicy Policy,
    MovementCompositionStatus Status,
    double? EffectiveValue,
    string? EffectiveUnit,
    string? EffectivePerUnit,
    DistanceUnit? EffectiveDistanceUnit,
    string? LimitingContributorKey,
    Guid? LimitingParticipantId,
    double? PreOverrideValue,
    IReadOnlyList<MovementAppliedContributor> Contributors,
    IReadOnlyList<string> Provenance,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> Diagnostics,
    MovementReferenceUse ReferenceUse,
    DistanceMeasure? SuggestedExpectedDistance);

public static class MovementCapabilityComposer
{
    private sealed record Quantity(double Value, string Unit, string? PerUnit, DistanceUnit? DistanceUnit);

    public static MovementCapabilityComposition Compose(
        StoredExpedition expedition,
        MovementCompositionInput? input = null)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        input ??= new MovementCompositionInput();
        expedition.Party.Validate();

        var policy = MovementCompositionPolicyResolver.Resolve(expedition.CampaignProcedure);
        var contributors = new List<MovementAppliedContributor>();
        var provenance = new List<string>();
        var diagnostics = new List<string>();
        var missing = new List<string>();
        if (input.MissingInputs is { Count: > 0 })
        {
            missing.AddRange(input.MissingInputs.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        if (!string.IsNullOrWhiteSpace(input.ExternalDiagnostic))
        {
            diagnostics.Add(input.ExternalDiagnostic.Trim());
        }

        var all = expedition.Party.MovementContributors
            .Concat(input.ResolvedContributors ?? [])
            .Where(value => value.Enabled)
            .OrderBy(value => Stage(value.Kind))
            .ThenBy(value => value.Id)
            .ToArray();

        var dmOverride = all.LastOrDefault(value => value.Kind == MovementCapabilityContributorKind.DmOverride);
        var status = MovementCompositionStatus.InputRequired;
        Quantity? quantity = null;
        string? limitingKey = null;
        Guid? limitingParticipantId = null;
        var referenceUse = MovementReferenceUse.None;

        if (policy.Support == MovementCompositionPolicySupport.Unsupported)
        {
            diagnostics.Add(policy.UnsupportedReason ?? "The pinned movement policy is unsupported.");
            status = MovementCompositionStatus.Unsupported;
        }
        else if (policy.Support == MovementCompositionPolicySupport.None)
        {
            (quantity, status, referenceUse) = ReferenceFallback(expedition, contributors, provenance, authoritative: true);
        }
        else
        {
            var baseResolution = ResolveBase(expedition, policy, all, contributors, diagnostics, missing);
            quantity = baseResolution.Quantity;
            limitingKey = baseResolution.LimitingKey;
            limitingParticipantId = baseResolution.LimitingParticipantId;
            status = baseResolution.Status;

            if (quantity is null)
            {
                var reference = ReferenceFallback(expedition, contributors, provenance, authoritative: false);
                if (reference.Quantity is not null)
                {
                    quantity = reference.Quantity;
                    status = reference.Status;
                    referenceUse = reference.ReferenceUse;
                }
            }
            else
            {
                referenceUse = expedition.Party.BaseMovement is null
                    ? MovementReferenceUse.None
                    : MovementReferenceUse.InformationalOnly;
                if (referenceUse == MovementReferenceUse.InformationalOnly)
                {
                    contributors.Add(new MovementAppliedContributor(
                        null,
                        null,
                        "party-movement-reference",
                        MovementCapabilityOperation.Base,
                        false,
                        null,
                        null,
                        null,
                        null,
                        expedition.Party.BaseMovement.LimitingMemberId,
                        null,
                        "Explicit party movement reference",
                        "Informational only because an automatic/manual capability base was composed."));
                }
            }

            if (quantity is not null && status != MovementCompositionStatus.ReferenceFallback)
            {
                status = ApplyAdjustments(
                    expedition,
                    policy,
                    input,
                    all,
                    ref quantity,
                    contributors,
                    provenance,
                    diagnostics,
                    missing,
                    status);
            }
        }

        var preOverride = quantity?.Value;
        if (dmOverride is not null)
        {
            if (!TryQuantity(dmOverride, quantity, out var replacement, out var problem))
            {
                diagnostics.Add(problem ?? "The DM movement override could not be interpreted.");
                status = MovementCompositionStatus.RequiresAdjudication;
            }
            else
            {
                quantity = replacement;
                status = MovementCompositionStatus.Resolved;
                contributors.Add(Applied(dmOverride, true, "Explicit DM final override."));
                provenance.Add(dmOverride.Provenance?.Trim() ?? "Explicit DM movement override.");
            }
        }

        if (quantity is null && dmOverride is null && input.ExternalStatus.HasValue)
        {
            status = input.ExternalStatus.Value;
        }
        if (quantity is null && missing.Count > 0 && status == MovementCompositionStatus.InputRequired)
        {
            status = MovementCompositionStatus.InputRequired;
        }

        var suggestion = quantity is null
            ? SuggestFromReference(expedition)
            : Suggest(expedition, quantity);
        if (suggestion is null && status == MovementCompositionStatus.ReferenceFallback)
        {
            suggestion = SuggestFromReference(expedition);
        }

        return new MovementCapabilityComposition(
            policy,
            status,
            quantity?.Value,
            quantity?.Unit,
            quantity?.PerUnit,
            quantity?.DistanceUnit,
            limitingKey,
            limitingParticipantId,
            dmOverride is null ? null : preOverride,
            contributors,
            provenance.Distinct(StringComparer.Ordinal).ToArray(),
            missing.Distinct(StringComparer.Ordinal).ToArray(),
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            referenceUse,
            suggestion);
    }

    private static (Quantity? Quantity, MovementCompositionStatus Status, string? LimitingKey, Guid? LimitingParticipantId) ResolveBase(
        StoredExpedition expedition,
        MovementCompositionPolicy policy,
        IReadOnlyList<MovementCapabilityContributor> all,
        List<MovementAppliedContributor> applied,
        List<string> diagnostics,
        List<string> missing)
    {
        var memberById = expedition.Party.Members.ToDictionary(value => value.Id);
        var replaced = all
            .Where(value => value.Kind is MovementCapabilityContributorKind.Mount or MovementCapabilityContributorKind.Vehicle)
            .SelectMany(value => value.ReplacesParticipantIds)
            .ToHashSet();

        var candidates = new List<MovementCapabilityContributor>();
        foreach (var contributor in all.Where(value => value.Operation is MovementCapabilityOperation.Base or MovementCapabilityOperation.Replace))
        {
            if (contributor.Kind == MovementCapabilityContributorKind.Participant)
            {
                var participantId = contributor.ParticipantId!.Value;
                if (!memberById.TryGetValue(participantId, out var member)
                    || !member.CountsTowardPartyMovement
                    || replaced.Contains(participantId))
                {
                    applied.Add(Applied(contributor, false, replaced.Contains(participantId)
                        ? "Participant movement is replaced by an assigned conveyance."
                        : "Participant is excluded from party movement."));
                    continue;
                }
                candidates.Add(contributor);
                continue;
            }

            if (contributor.Kind is MovementCapabilityContributorKind.Mount or MovementCapabilityContributorKind.Vehicle)
            {
                var carriesLimitingParticipant = contributor.ReplacesParticipantIds.Any(id =>
                    memberById.TryGetValue(id, out var member) && member.CountsTowardPartyMovement);
                if (!carriesLimitingParticipant)
                {
                    applied.Add(Applied(contributor, false, "Unassigned conveyance does not affect party movement."));
                    continue;
                }
                candidates.Add(contributor);
            }
        }

        if (candidates.Count == 0)
        {
            if (policy.BaseBudget is > 0 && policy.BudgetUnit is { Length: > 0 })
            {
                var baseQuantity = new Quantity(policy.BaseBudget.Value, policy.BudgetUnit, null, null);
                applied.Add(new MovementAppliedContributor(
                    null,
                    null,
                    "procedure-base-budget",
                    MovementCapabilityOperation.Base,
                    true,
                    baseQuantity.Value,
                    null,
                    baseQuantity.Unit,
                    null,
                    null,
                    null,
                    "Pinned CampaignProcedure",
                    $"Stored movement.budget baseBudget for model '{policy.BudgetModel}'."));
                return (baseQuantity, MovementCompositionStatus.Resolved, "procedure-base-budget", null);
            }
            missing.Add("movement base capability");
            return (null, MovementCompositionStatus.InputRequired, null, null);
        }

        if (string.Equals(policy.LimitingScope, "guide", StringComparison.Ordinal))
        {
            var guideIds = expedition.Party.ActivityAssignments
                .Where(value => string.Equals(value.RoleKey, "guide", StringComparison.Ordinal))
                .Select(value => value.ParticipantId)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToHashSet();
            var guide = candidates.Where(value => value.ParticipantId.HasValue && guideIds.Contains(value.ParticipantId.Value)).ToArray();
            if (guide.Length == 0)
            {
                missing.Add("guide movement capability");
                diagnostics.Add("The pinned movement policy limits by the guide role, but no guide capability is currently available.");
                foreach (var candidate in candidates)
                {
                    applied.Add(Applied(candidate, false, "Not selected because the guide movement unit is unresolved."));
                }
                return (null, MovementCompositionStatus.InputRequired, null, null);
            }
            candidates = guide.ToList();
        }

        Quantity? selected = null;
        MovementCapabilityContributor? selectedContributor = null;
        foreach (var candidate in candidates)
        {
            if (!TryQuantity(candidate, selected, out var candidateQuantity, out var problem))
            {
                diagnostics.Add(problem ?? $"Movement contributor '{candidate.Key}' could not be compared.");
                applied.Add(Applied(candidate, false, problem));
                return (null, MovementCompositionStatus.RequiresAdjudication, null, null);
            }

            if (selected is null)
            {
                selected = candidateQuantity;
                selectedContributor = candidate;
                continue;
            }
            if (!TryConvert(candidateQuantity!, selected, out var converted))
            {
                diagnostics.Add($"Movement contributor '{candidate.Key}' uses an incompatible unit and can not be silently converted.");
                applied.Add(Applied(candidate, false, "Incompatible movement unit."));
                return (null, MovementCompositionStatus.RequiresAdjudication, null, null);
            }
            if (converted.Value < selected.Value
                || (Math.Abs(converted.Value - selected.Value) < 0.000000001
                    && string.CompareOrdinal(candidate.Key, selectedContributor!.Key) < 0))
            {
                selected = converted;
                selectedContributor = candidate;
            }
        }

        foreach (var candidate in candidates)
        {
            applied.Add(Applied(
                candidate,
                candidate.Id == selectedContributor!.Id,
                candidate.Id == selectedContributor.Id
                    ? "Selected by the pinned limiting scope."
                    : "Faster movement unit did not limit the party."));
        }
        return (
            selected,
            MovementCompositionStatus.Resolved,
            selectedContributor.Key,
            selectedContributor.ParticipantId);
    }

    private static MovementCompositionStatus ApplyAdjustments(
        StoredExpedition expedition,
        MovementCompositionPolicy policy,
        MovementCompositionInput input,
        IReadOnlyList<MovementCapabilityContributor> all,
        ref Quantity? quantity,
        List<MovementAppliedContributor> applied,
        List<string> provenance,
        List<string> diagnostics,
        List<string> missing,
        MovementCompositionStatus currentStatus)
    {
        var paceKey = input.PaceKey ?? (expedition.Runtime as ExpeditionState)?.ActiveWatch?.Plan.Mode.PaceKey;
        var adjustments = all.Where(value => value.Kind is
                MovementCapabilityContributorKind.Load
                or MovementCapabilityContributorKind.TravelMode
                or MovementCapabilityContributorKind.TerrainRoute
                or MovementCapabilityContributorKind.Environment
                or MovementCapabilityContributorKind.PersistentEffect)
            .OrderBy(value => Stage(value.Kind))
            .ThenBy(value => value.Id)
            .ToArray();

        foreach (var adjustment in adjustments)
        {
            if (adjustment.Kind == MovementCapabilityContributorKind.TravelMode
                && paceKey is not null
                && !string.Equals(adjustment.Key, paceKey, StringComparison.Ordinal))
            {
                applied.Add(Applied(adjustment, false, $"Travel mode '{paceKey}' is selected instead."));
                continue;
            }
            if (adjustment.Kind == MovementCapabilityContributorKind.TravelMode && paceKey is null)
            {
                applied.Add(Applied(adjustment, false, "No travel mode/pace is currently selected."));
                continue;
            }

            var outcome = ApplyOperation(quantity!, adjustment);
            if (outcome.Quantity is null)
            {
                diagnostics.Add(outcome.Diagnostic ?? $"Movement contributor '{adjustment.Key}' requires adjudication.");
                applied.Add(Applied(adjustment, false, outcome.Diagnostic));
                currentStatus = MovementCompositionStatus.RequiresAdjudication;
                continue;
            }
            quantity = outcome.Quantity;
            applied.Add(Applied(adjustment, true, outcome.Diagnostic));
            if (!string.IsNullOrWhiteSpace(adjustment.Provenance))
            {
                provenance.Add(adjustment.Provenance.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(input.TerrainKey))
        {
            currentStatus = ApplyPinnedTerrain(
                policy,
                input.TerrainKey.Trim(),
                ref quantity,
                applied,
                diagnostics,
                missing,
                currentStatus);
        }
        else if (policy.Terrain.Support == MovementTerrainPolicySupport.Unsupported)
        {
            diagnostics.Add(policy.Terrain.UnsupportedReason ?? "The pinned terrain policy is unsupported.");
        }

        return currentStatus;
    }

    private static MovementCompositionStatus ApplyPinnedTerrain(
        MovementCompositionPolicy policy,
        string terrainKey,
        ref Quantity? quantity,
        List<MovementAppliedContributor> applied,
        List<string> diagnostics,
        List<string> missing,
        MovementCompositionStatus currentStatus)
    {
        var terrain = policy.Terrain;
        if (terrain.Support == MovementTerrainPolicySupport.None)
        {
            diagnostics.Add("Terrain was supplied, but the pinned procedure has no movement.terrain policy.");
            return currentStatus;
        }
        if (terrain.Support == MovementTerrainPolicySupport.Unsupported)
        {
            diagnostics.Add(terrain.UnsupportedReason ?? "The pinned terrain policy is unsupported.");
            return MovementCompositionStatus.Unsupported;
        }
        if (!terrain.TerrainAdjustments.TryGetValue(terrainKey, out var raw))
        {
            missing.Add($"terrain adjustment for '{terrainKey}'");
            return MovementCompositionStatus.InputRequired;
        }

        switch (terrain.AdjustmentModel)
        {
            case "multiplier":
            case "distance-per-hour-multiplier":
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
                    || !double.IsFinite(factor)
                    || factor < 0)
                {
                    diagnostics.Add($"Terrain '{terrainKey}' has an invalid numeric factor in the pinned procedure.");
                    return MovementCompositionStatus.Unsupported;
                }
                quantity = quantity! with { Value = quantity!.Value * factor };
                applied.Add(new MovementAppliedContributor(
                    null,
                    MovementCapabilityContributorKind.TerrainRoute,
                    terrainKey,
                    MovementCapabilityOperation.Multiply,
                    true,
                    factor,
                    null,
                    "factor",
                    null,
                    null,
                    null,
                    "Pinned CampaignProcedure movement.terrain",
                    $"Applied {terrain.AdjustmentModel}."));
                return currentStatus;

            case "maximum-pace":
                applied.Add(new MovementAppliedContributor(
                    null,
                    MovementCapabilityContributorKind.TerrainRoute,
                    terrainKey,
                    MovementCapabilityOperation.SymbolicLimit,
                    false,
                    null,
                    raw,
                    null,
                    null,
                    null,
                    null,
                    "Pinned CampaignProcedure movement.terrain",
                    "Symbolic maximum pace retained for adjudication."));
                diagnostics.Add($"Terrain '{terrainKey}' limits pace to '{raw}'. This symbolic condition has not been guessed as a numeric movement factor.");
                return MovementCompositionStatus.RequiresAdjudication;

            case "activity-cost":
            case "movement-points-per-distance":
            case "hexes-per-quarter-day":
            case "terrain-difficulty":
                applied.Add(new MovementAppliedContributor(
                    null,
                    MovementCapabilityContributorKind.TerrainRoute,
                    terrainKey,
                    terrain.AdjustmentModel == "terrain-difficulty"
                        ? MovementCapabilityOperation.SymbolicLimit
                        : MovementCapabilityOperation.Cost,
                    false,
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
                        ? parsed
                        : null,
                    raw,
                    terrain.AdjustmentModel,
                    null,
                    null,
                    null,
                    "Pinned CampaignProcedure movement.terrain",
                    "Model-specific terrain relationship preserved without reinterpreting it as a distance multiplier."));
                diagnostics.Add($"Terrain adjustment model '{terrain.AdjustmentModel}' is preserved structurally and requires model-specific input/adjudication before it can alter this composed quantity.");
                return MovementCompositionStatus.RequiresAdjudication;

            default:
                diagnostics.Add($"Terrain adjustment model '{terrain.AdjustmentModel}' is not supported.");
                return MovementCompositionStatus.Unsupported;
        }
    }

    private static (Quantity? Quantity, MovementCompositionStatus Status, MovementReferenceUse ReferenceUse) ReferenceFallback(
        StoredExpedition expedition,
        List<MovementAppliedContributor> contributors,
        List<string> provenance,
        bool authoritative)
    {
        var reference = expedition.Party.BaseMovement;
        if (reference is null)
        {
            return (null, MovementCompositionStatus.InputRequired, MovementReferenceUse.None);
        }

        Quantity? quantity = null;
        string detail;
        if (reference.PerHour is { } perHour)
        {
            quantity = Physical(perHour, "hour");
            detail = "Explicit per-hour party movement reference.";
        }
        else if (reference.PerWatch is { } perWatch)
        {
            quantity = Physical(perWatch, "watch");
            detail = "Explicit per-watch party movement reference.";
        }
        else if (reference.PerMarch is { } perMarch)
        {
            quantity = Physical(perMarch, "march");
            detail = "Explicit per-march party movement reference.";
        }
        else
        {
            return (null, MovementCompositionStatus.InputRequired, MovementReferenceUse.None);
        }

        contributors.Add(new MovementAppliedContributor(
            null,
            null,
            "party-movement-reference",
            MovementCapabilityOperation.Base,
            true,
            quantity.Value,
            null,
            quantity.Unit,
            quantity.PerUnit,
            reference.LimitingMemberId,
            null,
            "Explicit party movement reference",
            detail));
        provenance.Add("Explicit PartyMovementReference supplied by the expedition DM.");
        return (
            quantity,
            MovementCompositionStatus.ReferenceFallback,
            authoritative ? MovementReferenceUse.AuthoritativeBase : MovementReferenceUse.Fallback);
    }

    private static (Quantity? Quantity, string? Diagnostic) ApplyOperation(Quantity current, MovementCapabilityContributor contributor)
    {
        if (contributor.Operation == MovementCapabilityOperation.SymbolicLimit)
        {
            return (null, $"'{contributor.Key}' imposes symbolic limit '{contributor.SymbolicValue}'.");
        }
        if (contributor.Operation == MovementCapabilityOperation.Multiply)
        {
            return (current with { Value = current.Value * contributor.Value!.Value }, "Applied numeric multiplier.");
        }
        if (contributor.Operation == MovementCapabilityOperation.Replace)
        {
            return TryQuantity(contributor, current, out var replacement, out var problem)
                ? (replacement, "Applied replacement capability.")
                : (null, problem);
        }

        if (!TryQuantity(contributor, current, out var value, out var conversionProblem))
        {
            return (null, conversionProblem);
        }
        if (!TryConvert(value!, current, out var converted))
        {
            return (null, $"'{contributor.Key}' uses an incompatible movement unit.");
        }

        return contributor.Operation switch
        {
            MovementCapabilityOperation.Add => (current with { Value = current.Value + converted.Value }, "Applied additive movement adjustment."),
            MovementCapabilityOperation.Cap => (current with { Value = Math.Min(current.Value, converted.Value) }, "Applied movement cap."),
            MovementCapabilityOperation.Floor => (current with { Value = Math.Max(current.Value, converted.Value) }, "Applied movement floor."),
            MovementCapabilityOperation.Cost => (current with { Value = Math.Max(0, current.Value - converted.Value) }, "Applied movement-budget cost."),
            MovementCapabilityOperation.Base => (current, "Base contributor ignored after base selection."),
            _ => (null, $"Movement operation '{contributor.Operation}' is not supported in this composition stage.")
        };
    }

    private static bool TryQuantity(
        MovementCapabilityContributor contributor,
        Quantity? reference,
        out Quantity? quantity,
        out string? problem)
    {
        quantity = null;
        problem = null;
        if (!contributor.Value.HasValue)
        {
            problem = $"Movement contributor '{contributor.Key}' has no numeric value.";
            return false;
        }
        if (contributor.DistanceUnit is { } distanceUnit)
        {
            quantity = new Quantity(
                contributor.Value.Value,
                contributor.Unit ?? distanceUnit.Symbol,
                contributor.PerUnit,
                distanceUnit);
            if (reference is not null && !TryConvert(quantity, reference, out quantity))
            {
                problem = $"Movement contributor '{contributor.Key}' can not be converted to the selected movement unit.";
                return false;
            }
            return true;
        }
        if (string.IsNullOrWhiteSpace(contributor.Unit))
        {
            problem = $"Movement contributor '{contributor.Key}' has no explicit unit.";
            return false;
        }
        quantity = new Quantity(contributor.Value.Value, contributor.Unit.Trim(), contributor.PerUnit, null);
        if (reference is not null && !TryConvert(quantity, reference, out quantity))
        {
            problem = $"Movement contributor '{contributor.Key}' uses unit '{contributor.Unit}', which can not be silently converted to '{reference.Unit}'.";
            return false;
        }
        return true;
    }

    private static bool TryConvert(Quantity source, Quantity target, out Quantity converted)
    {
        converted = source;
        if (!string.Equals(source.PerUnit, target.PerUnit, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (source.DistanceUnit.HasValue && target.DistanceUnit.HasValue)
        {
            try
            {
                var measure = new DistanceMeasure(source.Value, source.DistanceUnit.Value)
                    .ConvertTo(target.DistanceUnit.Value);
                converted = target with { Value = measure.Value };
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        if (source.DistanceUnit.HasValue != target.DistanceUnit.HasValue
            || !string.Equals(source.Unit, target.Unit, StringComparison.Ordinal))
        {
            return false;
        }
        converted = target with { Value = source.Value };
        return true;
    }

    private static DistanceMeasure? Suggest(StoredExpedition expedition, Quantity quantity)
    {
        if (expedition.Runtime is not ExpeditionState spatial || !quantity.DistanceUnit.HasValue)
        {
            return null;
        }
        var targetUnit = spatial.DistanceTraveled.Unit;
        double value;
        try
        {
            value = new DistanceMeasure(quantity.Value, quantity.DistanceUnit.Value).ConvertTo(targetUnit).Value;
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (string.Equals(quantity.PerUnit, "hour", StringComparison.OrdinalIgnoreCase))
        {
            var interval = FocusedIntervalPolicyResolver.Resolve(expedition.CampaignProcedure);
            var hours = spatial.ActiveWatch?.Remaining.TotalHours
                ?? interval.IntervalDuration?.TotalHours;
            return hours.HasValue && double.IsFinite(hours.Value) && hours.Value >= 0
                ? new DistanceMeasure(value * hours.Value, targetUnit)
                : null;
        }
        if (string.Equals(quantity.PerUnit, "watch", StringComparison.OrdinalIgnoreCase))
        {
            if (spatial.ActiveWatch is null)
            {
                return new DistanceMeasure(value, targetUnit);
            }
            var interval = FocusedIntervalPolicyResolver.Resolve(expedition.CampaignProcedure);
            var full = interval.IntervalDuration?.TotalHours;
            if (full.HasValue && Math.Abs(spatial.ActiveWatch.Remaining.TotalHours - full.Value) < 0.000001)
            {
                return new DistanceMeasure(value, targetUnit);
            }
        }
        return null;
    }

    private static DistanceMeasure? SuggestFromReference(StoredExpedition expedition)
    {
        if (expedition.Runtime is not ExpeditionState spatial || expedition.Party.BaseMovement is not { } reference)
        {
            return null;
        }
        var interval = FocusedIntervalPolicyResolver.Resolve(expedition.CampaignProcedure);
        var remaining = spatial.ActiveWatch?.Remaining.TotalHours
            ?? interval.IntervalDuration?.TotalHours;
        try
        {
            if (spatial.ActiveWatch is null && reference.PerWatch is { } perWatch)
            {
                return perWatch.ConvertTo(spatial.DistanceTraveled.Unit);
            }
            if (reference.PerHour is { } perHour && remaining.HasValue && remaining.Value >= 0)
            {
                var hourly = perHour.ConvertTo(spatial.DistanceTraveled.Unit);
                return new DistanceMeasure(hourly.Value * remaining.Value, hourly.Unit);
            }
            if (spatial.ActiveWatch is not null
                && reference.PerWatch is { } activeWatch
                && interval.IntervalDuration is { } intervalDuration
                && Math.Abs(spatial.ActiveWatch.Remaining.TotalHours - intervalDuration.TotalHours) < 0.000001)
            {
                return activeWatch.ConvertTo(spatial.DistanceTraveled.Unit);
            }
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        return null;
    }

    private static Quantity Physical(DistanceMeasure measure, string perUnit) =>
        new(measure.Value, measure.Unit.Symbol, perUnit, measure.Unit);

    private static MovementAppliedContributor Applied(
        MovementCapabilityContributor contributor,
        bool applied,
        string? detail) => new(
            contributor.Id,
            contributor.Kind,
            contributor.Key,
            contributor.Operation,
            applied,
            contributor.Value,
            contributor.SymbolicValue,
            contributor.Unit,
            contributor.PerUnit,
            contributor.ParticipantId,
            contributor.MovementUnitKey,
            contributor.Provenance,
            detail);

    private static int Stage(MovementCapabilityContributorKind kind) => kind switch
    {
        MovementCapabilityContributorKind.Participant => 0,
        MovementCapabilityContributorKind.Mount => 0,
        MovementCapabilityContributorKind.Vehicle => 0,
        MovementCapabilityContributorKind.Load => 10,
        MovementCapabilityContributorKind.TravelMode => 20,
        MovementCapabilityContributorKind.TerrainRoute => 30,
        MovementCapabilityContributorKind.Environment => 40,
        MovementCapabilityContributorKind.PersistentEffect => 50,
        MovementCapabilityContributorKind.DmOverride => 100,
        _ => 90
    };
}
