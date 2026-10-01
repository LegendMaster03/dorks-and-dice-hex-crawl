using System.Globalization;
using HexCrawl.Application.Persistence;
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
    private static readonly HashSet<string> DirectBudgetModels = new(StringComparer.Ordinal)
    {
        "fixed-per-day",
        "movement-points",
        "quarter-day-activities",
        "journey-progress"
    };

    private sealed record Quantity(
        double Value,
        string Unit,
        string? PerUnit,
        DistanceUnit? DistanceUnit);

    private sealed record BaseResolution(
        Quantity? Quantity,
        MovementCompositionStatus Status,
        string? LimitingKey,
        Guid? LimitingParticipantId);

    private sealed record ReferenceResolution(
        Quantity? Quantity,
        MovementReferenceUse Use);

    public static MovementCapabilityComposition Compose(
        StoredExpedition expedition,
        MovementCompositionInput? input = null)
    {
        ArgumentNullException.ThrowIfNull(expedition);
        input ??= new MovementCompositionInput();
        expedition.Party.Validate();

        var memberIds = expedition.Party.Members.Select(value => value.Id).ToHashSet();
        foreach (var contributor in input.ResolvedContributors ?? [])
        {
            contributor.Validate(memberIds);
        }

        var policy = MovementCompositionPolicyResolver.Resolve(expedition.CampaignProcedure);
        var applied = new List<MovementAppliedContributor>();
        var provenance = new List<string>();
        var diagnostics = new List<string>();
        var missingInputs = new List<string>();
        if (input.MissingInputs is { Count: > 0 })
        {
            missingInputs.AddRange(input.MissingInputs.Where(value => !string.IsNullOrWhiteSpace(value)));
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

        Quantity? quantity = null;
        var status = MovementCompositionStatus.InputRequired;
        var referenceUse = MovementReferenceUse.None;
        string? limitingKey = null;
        Guid? limitingParticipantId = null;

        if (policy.Support == MovementCompositionPolicySupport.Unsupported)
        {
            diagnostics.Add(policy.UnsupportedReason ?? "The pinned movement policy is unsupported.");
            var fallback = ResolveReference(expedition, applied, provenance, MovementReferenceUse.Fallback);
            quantity = fallback.Quantity;
            referenceUse = fallback.Use;
            status = quantity is null
                ? MovementCompositionStatus.Unsupported
                : MovementCompositionStatus.ReferenceFallback;
        }
        else if (policy.Support == MovementCompositionPolicySupport.None)
        {
            var policyFree = ResolvePolicyFreeBase(all, applied, provenance, diagnostics);
            quantity = policyFree.Quantity;
            status = policyFree.Status;
            limitingKey = policyFree.LimitingKey;
            limitingParticipantId = policyFree.LimitingParticipantId;

            if (quantity is null)
            {
                var reference = ResolveReference(
                    expedition,
                    applied,
                    provenance,
                    MovementReferenceUse.AuthoritativeBase);
                quantity = reference.Quantity;
                referenceUse = reference.Use;
                status = quantity is null
                    ? MovementCompositionStatus.InputRequired
                    : MovementCompositionStatus.ReferenceFallback;
            }
            else
            {
                MarkReferenceInformational(expedition, applied, ref referenceUse);
                status = ApplyAdjustments(
                    expedition,
                    policy,
                    input,
                    all,
                    quantity,
                    applyPinnedTerrain: false,
                    applied,
                    provenance,
                    diagnostics,
                    missingInputs,
                    status,
                    out quantity);
            }
        }
        else
        {
            var baseResolution = ResolveBase(
                expedition,
                policy,
                all,
                applied,
                provenance,
                diagnostics,
                missingInputs);
            quantity = baseResolution.Quantity;
            status = baseResolution.Status;
            limitingKey = baseResolution.LimitingKey;
            limitingParticipantId = baseResolution.LimitingParticipantId;

            if (quantity is null)
            {
                var fallback = ResolveReference(expedition, applied, provenance, MovementReferenceUse.Fallback);
                if (fallback.Quantity is not null)
                {
                    quantity = fallback.Quantity;
                    referenceUse = fallback.Use;
                    status = MovementCompositionStatus.ReferenceFallback;
                }
            }
            else
            {
                MarkReferenceInformational(expedition, applied, ref referenceUse);
            }

            if (quantity is not null && status != MovementCompositionStatus.ReferenceFallback)
            {
                status = ApplyAdjustments(
                    expedition,
                    policy,
                    input,
                    all,
                    quantity,
                    applyPinnedTerrain: true,
                    applied,
                    provenance,
                    diagnostics,
                    missingInputs,
                    status,
                    out quantity);
            }
        }

        var preOverride = quantity?.Value;
        if (dmOverride is not null)
        {
            if (!TryQuantity(dmOverride, quantity, out var replacement, out var problem) || replacement is null)
            {
                diagnostics.Add(problem ?? "The DM movement override could not be interpreted.");
                status = MovementCompositionStatus.RequiresAdjudication;
            }
            else
            {
                quantity = replacement;
                status = MovementCompositionStatus.Resolved;
                applied.Add(Applied(dmOverride, true, "Explicit DM final override."));
                AddProvenance(provenance, dmOverride.Provenance, "Explicit DM movement override.");
            }
        }

        if (quantity is null && dmOverride is null && input.ExternalStatus.HasValue
            && status != MovementCompositionStatus.Unsupported)
        {
            status = input.ExternalStatus.Value;
        }

        var suggestion = status is MovementCompositionStatus.Resolved or MovementCompositionStatus.ReferenceFallback
            && quantity is not null
            ? Suggest(expedition, quantity)
            : null;

        if (policy.BudgetModel == "journey-progress" && suggestion is not null)
        {
            throw new InvalidOperationException("Journey progress must never be reinterpreted as a physical watch-distance suggestion.");
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
            applied,
            provenance.Distinct(StringComparer.Ordinal).ToArray(),
            missingInputs.Distinct(StringComparer.Ordinal).ToArray(),
            diagnostics.Distinct(StringComparer.Ordinal).ToArray(),
            referenceUse,
            suggestion);
    }

    private static BaseResolution ResolvePolicyFreeBase(
        IReadOnlyList<MovementCapabilityContributor> all,
        List<MovementAppliedContributor> applied,
        List<string> provenance,
        List<string> diagnostics)
    {
        var candidates = all
            .Where(value => value.Scope == MovementCapabilityScope.Party)
            .Where(value => value.Kind == MovementCapabilityContributorKind.Environment)
            .Where(value => value.Operation is MovementCapabilityOperation.Base or MovementCapabilityOperation.Replace)
            .ToArray();

        if (candidates.Length == 0)
        {
            return new BaseResolution(null, MovementCompositionStatus.InputRequired, null, null);
        }
        if (candidates.Length > 1)
        {
            diagnostics.Add("Multiple party-level base movement capabilities are available without a pinned movement.budget policy to select between them.");
            foreach (var candidate in candidates)
            {
                applied.Add(Applied(candidate, false, "Requires adjudication because no pinned policy selects among multiple party-level base capabilities."));
            }
            return new BaseResolution(null, MovementCompositionStatus.RequiresAdjudication, null, null);
        }

        var selected = candidates[0];
        if (!TryQuantity(selected, null, out var quantity, out var problem) || quantity is null)
        {
            diagnostics.Add(problem ?? "The party-level movement capability could not be interpreted.");
            applied.Add(Applied(selected, false, problem));
            return new BaseResolution(null, MovementCompositionStatus.RequiresAdjudication, null, null);
        }

        applied.Add(Applied(selected, true, "Party-level base movement capability used without requiring a structural movement.budget module."));
        AddProvenance(provenance, selected.Provenance, "Resolved party-level movement capability.");
        return new BaseResolution(quantity, MovementCompositionStatus.Resolved, selected.Key, selected.ParticipantId);
    }

    private static BaseResolution ResolveBase(
        StoredExpedition expedition,
        MovementCompositionPolicy policy,
        IReadOnlyList<MovementCapabilityContributor> all,
        List<MovementAppliedContributor> applied,
        List<string> provenance,
        List<string> diagnostics,
        List<string> missingInputs)
    {
        var memberById = expedition.Party.Members.ToDictionary(value => value.Id);
        var replacedParticipants = all
            .Where(value => value.Kind is MovementCapabilityContributorKind.Mount or MovementCapabilityContributorKind.Vehicle)
            .SelectMany(value => value.ReplacesParticipantIds)
            .ToHashSet();
        var candidates = new List<MovementCapabilityContributor>();

        foreach (var contributor in all.Where(IsBaseCandidate))
        {
            if (contributor.Kind == MovementCapabilityContributorKind.Participant)
            {
                var participantId = contributor.ParticipantId!.Value;
                if (!memberById.TryGetValue(participantId, out var member)
                    || !member.CountsTowardPartyMovement)
                {
                    applied.Add(Applied(contributor, false, "Participant is excluded from party movement."));
                    continue;
                }
                if (replacedParticipants.Contains(participantId))
                {
                    applied.Add(Applied(contributor, false, "Participant movement is replaced by an assigned conveyance."));
                    continue;
                }
                candidates.Add(contributor);
                continue;
            }

            if (contributor.Kind is MovementCapabilityContributorKind.Mount or MovementCapabilityContributorKind.Vehicle)
            {
                var carriesIncludedParticipant = contributor.ReplacesParticipantIds.Any(id =>
                    memberById.TryGetValue(id, out var member) && member.CountsTowardPartyMovement);
                if (!carriesIncludedParticipant)
                {
                    applied.Add(Applied(contributor, false, "Unassigned conveyance does not affect party movement."));
                    continue;
                }
                candidates.Add(contributor);
                continue;
            }

            candidates.Add(contributor);
        }

        if (string.Equals(policy.LimitingScope, "party", StringComparison.Ordinal))
        {
            var partyCandidates = candidates
                .Where(value => value.Scope == MovementCapabilityScope.Party)
                .ToList();
            if (partyCandidates.Count > 0)
            {
                candidates = partyCandidates;
            }
        }

        if (candidates.Count == 0 && TryDirectProcedureBudget(policy, out var procedureBase))
        {
            applied.Add(new MovementAppliedContributor(
                null,
                null,
                "procedure-base-budget",
                MovementCapabilityOperation.Base,
                true,
                procedureBase!.Value,
                null,
                procedureBase.Unit,
                procedureBase.PerUnit,
                null,
                null,
                "Pinned CampaignProcedure",
                $"Stored movement.budget baseBudget for direct budget model '{policy.BudgetModel}'."));
            provenance.Add("Pinned CampaignProcedure movement.budget.");
            return new BaseResolution(
                procedureBase,
                MovementCompositionStatus.Resolved,
                "procedure-base-budget",
                null);
        }

        if (string.Equals(policy.LimitingScope, "guide", StringComparison.Ordinal))
        {
            var guideIds = expedition.Party.ActivityAssignments
                .Where(value => string.Equals(value.RoleKey, "guide", StringComparison.Ordinal))
                .Select(value => value.ParticipantId)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToHashSet();
            candidates = candidates
                .Where(value => value.ParticipantId.HasValue && guideIds.Contains(value.ParticipantId.Value))
                .ToList();
            if (candidates.Count == 0)
            {
                missingInputs.Add("guide movement capability");
                diagnostics.Add("The pinned movement policy limits by the guide role, but no guide movement capability is available.");
                return new BaseResolution(null, MovementCompositionStatus.InputRequired, null, null);
            }
        }

        if (candidates.Count == 0)
        {
            missingInputs.Add("movement base capability");
            if (policy.BaseBudget.HasValue)
            {
                diagnostics.Add($"The pinned '{policy.BudgetModel}' policy retains baseBudget={policy.BaseBudget.Value.ToString(CultureInfo.InvariantCulture)} {policy.BudgetUnit}, but that value is not sufficient to derive movement safely for this model.");
            }
            return new BaseResolution(null, MovementCompositionStatus.InputRequired, null, null);
        }

        Quantity? selected = null;
        MovementCapabilityContributor? selectedContributor = null;
        foreach (var candidate in candidates)
        {
            if (!TryQuantity(candidate, selected, out var candidateQuantity, out var problem)
                || candidateQuantity is null)
            {
                diagnostics.Add(problem ?? $"Movement contributor '{candidate.Key}' could not be compared.");
                applied.Add(Applied(candidate, false, problem));
                return new BaseResolution(null, MovementCompositionStatus.RequiresAdjudication, null, null);
            }

            if (selected is null)
            {
                selected = candidateQuantity;
                selectedContributor = candidate;
                continue;
            }

            if (!TryConvert(candidateQuantity, selected, out var converted))
            {
                diagnostics.Add($"Movement contributor '{candidate.Key}' uses an incompatible unit and can not be silently converted.");
                applied.Add(Applied(candidate, false, "Incompatible movement unit."));
                return new BaseResolution(null, MovementCompositionStatus.RequiresAdjudication, null, null);
            }

            if (converted.Value < selected.Value
                || (NearlyEqual(converted.Value, selected.Value)
                    && string.CompareOrdinal(candidate.Key, selectedContributor!.Key) < 0))
            {
                selected = converted;
                selectedContributor = candidate;
            }
        }

        if (selected is null || selectedContributor is null)
        {
            throw new InvalidOperationException("Movement base selection produced no deterministic candidate.");
        }

        foreach (var candidate in candidates)
        {
            var isSelected = candidate.Id == selectedContributor.Id;
            applied.Add(Applied(
                candidate,
                isSelected,
                isSelected
                    ? "Selected by the pinned limiting scope."
                    : "Faster movement unit did not limit the party."));
        }
        AddProvenance(provenance, selectedContributor.Provenance, "Resolved movement capability.");

        return new BaseResolution(
            selected,
            MovementCompositionStatus.Resolved,
            selectedContributor.Key,
            selectedContributor.ParticipantId);
    }

    private static bool TryDirectProcedureBudget(
        MovementCompositionPolicy policy,
        out Quantity? quantity)
    {
        quantity = null;
        if (!policy.BaseBudget.HasValue
            || string.IsNullOrWhiteSpace(policy.BudgetUnit)
            || string.IsNullOrWhiteSpace(policy.BudgetModel)
            || !DirectBudgetModels.Contains(policy.BudgetModel))
        {
            return false;
        }

        quantity = new Quantity(policy.BaseBudget.Value, policy.BudgetUnit, null, null);
        return true;
    }

    private static bool IsBaseCandidate(MovementCapabilityContributor contributor)
    {
        if (contributor.Operation is not MovementCapabilityOperation.Base and not MovementCapabilityOperation.Replace)
        {
            return false;
        }

        return contributor.Kind is MovementCapabilityContributorKind.Participant
            or MovementCapabilityContributorKind.Mount
            or MovementCapabilityContributorKind.Vehicle
            or MovementCapabilityContributorKind.Environment;
    }

    private static MovementCompositionStatus ApplyAdjustments(
        StoredExpedition expedition,
        MovementCompositionPolicy policy,
        MovementCompositionInput input,
        IReadOnlyList<MovementCapabilityContributor> all,
        Quantity current,
        bool applyPinnedTerrain,
        List<MovementAppliedContributor> applied,
        List<string> provenance,
        List<string> diagnostics,
        List<string> missingInputs,
        MovementCompositionStatus currentStatus,
        out Quantity result)
    {
        result = current;
        var paceKey = input.PaceKey ?? (expedition.Runtime as ExpeditionState)?.ActiveWatch?.Plan.Mode.PaceKey;
        var adjustments = all
            .Where(value => value.Kind is MovementCapabilityContributorKind.Load
                or MovementCapabilityContributorKind.TravelMode
                or MovementCapabilityContributorKind.TerrainRoute
                or MovementCapabilityContributorKind.Environment
                or MovementCapabilityContributorKind.PersistentEffect)
            .Where(value => !IsBaseCandidate(value))
            .OrderBy(value => Stage(value.Kind))
            .ThenBy(value => value.Id)
            .ToArray();

        foreach (var adjustment in adjustments)
        {
            if (adjustment.Kind == MovementCapabilityContributorKind.TravelMode)
            {
                if (paceKey is null)
                {
                    applied.Add(Applied(adjustment, false, "No travel mode/pace is currently selected."));
                    continue;
                }
                if (!string.Equals(adjustment.Key, paceKey, StringComparison.Ordinal))
                {
                    applied.Add(Applied(adjustment, false, $"Travel mode '{paceKey}' is selected instead."));
                    continue;
                }
            }

            var operation = ApplyOperation(result, adjustment);
            if (operation.Quantity is null)
            {
                diagnostics.Add(operation.Diagnostic ?? $"Movement contributor '{adjustment.Key}' requires adjudication.");
                applied.Add(Applied(adjustment, false, operation.Diagnostic));
                currentStatus = MovementCompositionStatus.RequiresAdjudication;
                continue;
            }

            result = operation.Quantity;
            applied.Add(Applied(adjustment, true, operation.Diagnostic));
            AddProvenance(provenance, adjustment.Provenance, null);
        }

        if (applyPinnedTerrain && !string.IsNullOrWhiteSpace(input.TerrainKey))
        {
            currentStatus = ApplyPinnedTerrain(
                policy,
                input.TerrainKey.Trim(),
                result,
                applied,
                diagnostics,
                missingInputs,
                currentStatus,
                out result);
        }
        else if (applyPinnedTerrain && policy.Terrain.Support == MovementTerrainPolicySupport.Unsupported)
        {
            diagnostics.Add(policy.Terrain.UnsupportedReason ?? "The pinned terrain policy is unsupported.");
        }

        if (applyPinnedTerrain && !string.IsNullOrWhiteSpace(input.RouteKey))
        {
            currentStatus = ApplyPinnedRoute(
                policy,
                input.RouteKey.Trim(),
                applied,
                diagnostics,
                currentStatus);
        }

        return currentStatus;
    }

    private static MovementCompositionStatus ApplyPinnedTerrain(
        MovementCompositionPolicy policy,
        string terrainKey,
        Quantity current,
        List<MovementAppliedContributor> applied,
        List<string> diagnostics,
        List<string> missingInputs,
        MovementCompositionStatus currentStatus,
        out Quantity result)
    {
        result = current;
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
            missingInputs.Add($"terrain adjustment for '{terrainKey}'");
            return MovementCompositionStatus.InputRequired;
        }

        switch (terrain.AdjustmentModel)
        {
            case "multiplier":
            case "distance-per-hour-multiplier":
                if (!TryFiniteNonNegative(raw, out var factor))
                {
                    diagnostics.Add($"Terrain '{terrainKey}' has an invalid numeric factor in the pinned procedure.");
                    return MovementCompositionStatus.Unsupported;
                }
                result = current with { Value = current.Value * factor };
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
                diagnostics.Add($"Terrain '{terrainKey}' limits pace to '{raw}'. The symbolic condition was not guessed as a numeric movement factor.");
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
                    TryFiniteNonNegative(raw, out var parsed) ? parsed : null,
                    raw,
                    terrain.AdjustmentModel,
                    null,
                    null,
                    null,
                    "Pinned CampaignProcedure movement.terrain",
                    "Relationship preserved without reinterpreting it as a distance multiplier."));
                diagnostics.Add($"Terrain model '{terrain.AdjustmentModel}' requires model-specific input or adjudication before it can alter the composed quantity.");
                return MovementCompositionStatus.RequiresAdjudication;

            default:
                diagnostics.Add($"Terrain adjustment model '{terrain.AdjustmentModel}' is unsupported.");
                return MovementCompositionStatus.Unsupported;
        }
    }

    private static MovementCompositionStatus ApplyPinnedRoute(
        MovementCompositionPolicy policy,
        string routeKey,
        List<MovementAppliedContributor> applied,
        List<string> diagnostics,
        MovementCompositionStatus currentStatus)
    {
        if (policy.Terrain.Support != MovementTerrainPolicySupport.Supported
            || string.IsNullOrWhiteSpace(policy.Terrain.RouteAdjustmentModel))
        {
            diagnostics.Add($"Route '{routeKey}' was supplied, but no supported route movement policy is available in the pinned procedure.");
            return currentStatus;
        }

        var symbolic = $"{policy.Terrain.RouteAdjustmentModel}:{routeKey}";
        applied.Add(new MovementAppliedContributor(
            null,
            MovementCapabilityContributorKind.TerrainRoute,
            routeKey,
            MovementCapabilityOperation.SymbolicLimit,
            false,
            null,
            symbolic,
            null,
            null,
            null,
            null,
            "Pinned CampaignProcedure movement.terrain",
            "Route relationship retained symbolically until an explicit resolved route contributor is available."));
        diagnostics.Add($"Route model '{policy.Terrain.RouteAdjustmentModel}' for route '{routeKey}' requires resolved input or DM adjudication; Hex Crawl will not invent a route formula.");
        return MovementCompositionStatus.RequiresAdjudication;
    }

    private static ReferenceResolution ResolveReference(
        StoredExpedition expedition,
        List<MovementAppliedContributor> applied,
        List<string> provenance,
        MovementReferenceUse use)
    {
        var reference = expedition.Party.BaseMovement;
        if (reference is null)
        {
            return new ReferenceResolution(null, MovementReferenceUse.None);
        }

        Quantity? quantity = null;
        string? detail = null;
        if (expedition.Runtime is ExpeditionState { ActiveWatch: null } && reference.PerWatch is { } readyWatch)
        {
            quantity = Physical(readyWatch, "watch");
            detail = "Explicit per-watch party movement reference.";
        }
        else if (reference.PerHour is { } perHour)
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

        if (quantity is null)
        {
            return new ReferenceResolution(null, MovementReferenceUse.None);
        }

        applied.Add(new MovementAppliedContributor(
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
        provenance.Add(string.IsNullOrWhiteSpace(reference.Note)
            ? "Explicit PartyMovementReference supplied by the expedition DM."
            : $"Explicit PartyMovementReference supplied by the expedition DM: {reference.Note.Trim()}");
        return new ReferenceResolution(quantity, use);
    }

    private static void MarkReferenceInformational(
        StoredExpedition expedition,
        List<MovementAppliedContributor> applied,
        ref MovementReferenceUse referenceUse)
    {
        if (expedition.Party.BaseMovement is not { } reference)
        {
            return;
        }

        referenceUse = MovementReferenceUse.InformationalOnly;
        applied.Add(new MovementAppliedContributor(
            null,
            null,
            "party-movement-reference",
            MovementCapabilityOperation.Base,
            false,
            null,
            null,
            null,
            null,
            reference.LimitingMemberId,
            null,
            "Explicit party movement reference",
            "Informational only because a capability base was composed; the reference was not double-counted."));
    }

    private static (Quantity? Quantity, string? Diagnostic) ApplyOperation(
        Quantity current,
        MovementCapabilityContributor contributor)
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
        if (!TryQuantity(contributor, current, out var adjustment, out var conversionProblem)
            || adjustment is null)
        {
            return (null, conversionProblem);
        }
        if (!TryConvert(adjustment, current, out var converted))
        {
            return (null, $"'{contributor.Key}' uses an incompatible movement unit.");
        }

        return contributor.Operation switch
        {
            MovementCapabilityOperation.Add =>
                (current with { Value = current.Value + converted.Value }, "Applied additive movement adjustment."),
            MovementCapabilityOperation.Cap =>
                (current with { Value = Math.Min(current.Value, converted.Value) }, "Applied movement cap."),
            MovementCapabilityOperation.Floor =>
                (current with { Value = Math.Max(current.Value, converted.Value) }, "Applied movement floor."),
            MovementCapabilityOperation.Cost =>
                (current with { Value = Math.Max(0, current.Value - converted.Value) }, "Applied movement-budget cost."),
            MovementCapabilityOperation.Base =>
                (current, "Base contributor ignored after base selection."),
            _ => (null, $"Movement operation '{contributor.Operation}' is not supported in this stage.")
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

        Quantity candidate;
        if (contributor.DistanceUnit is { } distanceUnit)
        {
            candidate = new Quantity(
                contributor.Value.Value,
                contributor.Unit ?? distanceUnit.Symbol,
                contributor.PerUnit,
                distanceUnit);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(contributor.Unit))
            {
                problem = $"Movement contributor '{contributor.Key}' has no explicit unit.";
                return false;
            }
            candidate = new Quantity(
                contributor.Value.Value,
                contributor.Unit.Trim(),
                contributor.PerUnit,
                null);
        }

        if (reference is not null)
        {
            if (!TryConvert(candidate, reference, out var converted))
            {
                problem = $"Movement contributor '{contributor.Key}' can not be converted to the selected movement unit.";
                return false;
            }
            candidate = converted;
        }

        quantity = candidate;
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
                var convertedMeasure = new DistanceMeasure(source.Value, source.DistanceUnit.Value)
                    .ConvertTo(target.DistanceUnit.Value);
                converted = target with { Value = convertedMeasure.Value };
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

        DistanceMeasure converted;
        try
        {
            converted = new DistanceMeasure(quantity.Value, quantity.DistanceUnit.Value)
                .ConvertTo(spatial.DistanceTraveled.Unit);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (string.Equals(quantity.PerUnit, "hour", StringComparison.OrdinalIgnoreCase))
        {
            var interval = FocusedIntervalPolicyResolver.Resolve(expedition.CampaignProcedure);
            var hours = spatial.ActiveWatch?.Remaining.TotalHours ?? interval.IntervalDuration?.TotalHours;
            return hours.HasValue && double.IsFinite(hours.Value) && hours.Value >= 0
                ? new DistanceMeasure(converted.Value * hours.Value, converted.Unit)
                : null;
        }

        if (string.Equals(quantity.PerUnit, "watch", StringComparison.OrdinalIgnoreCase))
        {
            if (spatial.ActiveWatch is null)
            {
                return converted;
            }
            var interval = FocusedIntervalPolicyResolver.Resolve(expedition.CampaignProcedure);
            if (interval.IntervalDuration is { } duration
                && NearlyEqual(spatial.ActiveWatch.Remaining.TotalHours, duration.TotalHours))
            {
                return converted;
            }
        }

        return null;
    }

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

    private static Quantity Physical(DistanceMeasure measure, string perUnit) =>
        new(measure.Value, measure.Unit.Symbol, perUnit, measure.Unit);

    private static void AddProvenance(
        List<string> provenance,
        string? value,
        string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            provenance.Add(value.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(fallback))
        {
            provenance.Add(fallback);
        }
    }

    private static bool TryFiniteNonNegative(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result)
        && result >= 0;

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) < 0.000001;

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
