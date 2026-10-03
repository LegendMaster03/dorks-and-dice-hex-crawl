using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record ResourceConsequenceApplyResult(
    ExpeditionResourceState Resources,
    ExpeditionEffectState Effects,
    ExpeditionConsequenceStatus Status,
    string Detail,
    bool StateChanged);

/// <summary>
/// Consumes the resource portion of a consequence already accepted by Phase 10. The same
/// consequence id is the only idempotency key: this consumer never re-submits the consequence to
/// ExpeditionConsequenceEngine.Process and never removes pending work before every resource change
/// has validated successfully.
/// </summary>
public static class ResourceConsequenceConsumer
{
    public static ResourceConsequenceApplyResult Consume(
        ExpeditionResourceState resources,
        ExpeditionEffectState effects,
        CrawlPartySheet party,
        Guid consequenceId,
        ExpeditionConsequenceProvenance resolutionProvenance)
    {
        resources.Validate(party);
        effects.Validate(party);
        resolutionProvenance.Validate();
        if (consequenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Resource consequence id is required.");
        }

        var applied = effects.AppliedConsequences.SingleOrDefault(value => value.ConsequenceId == consequenceId)
            ?? throw new InvalidOperationException("Resource consequence has not been accepted by the Phase 10 consequence engine.");
        var pending = effects.PendingConsequences.SingleOrDefault(value => value.Consequence.Id == consequenceId);
        if (pending is null)
        {
            return new(resources, effects, ExpeditionConsequenceStatus.AlreadyApplied,
                $"Consequence '{consequenceId:D}' has no pending resource work and can not mutate resources again.", false);
        }

        var components = pending.UnresolvedComponents.OfType<ResourceChangeConsequenceComponent>().ToArray();
        if (components.Length == 0)
        {
            return new(resources, effects, pending.Status,
                "The pending consequence contains no unresolved resource component.", false);
        }

        var working = resources.Resources.ToList();
        var audits = new List<ExpeditionResourceAuditRecord>();
        foreach (var component in components)
        {
            var match = FindResource(working, pending.Consequence.Target, component);
            if (match.Resource is null)
            {
                return new(resources, effects, match.Status, match.Detail!, false);
            }
            var resource = match.Resource;
            if (resource.InventoryModel == ExpeditionResourceInventoryModel.ExternalManual)
            {
                return new(resources, effects, ExpeditionConsequenceStatus.ExternalActionRequired,
                    $"Resource '{resource.ResourceKey}' is externally owned and requires explicit external/manual resolution.", false);
            }

            var mutation = TryApply(resource, component);
            if (mutation.Resource is null)
            {
                return new(resources, effects, mutation.Status, mutation.Detail!, false);
            }

            var updated = mutation.Resource with
            {
                Provenance = mutation.Resource.Provenance.Append(resolutionProvenance).ToArray()
            };
            working[working.IndexOf(resource)] = updated;
            audits.Add(new ExpeditionResourceAuditRecord(
                Guid.NewGuid(), resource.Id, resource.ResourceKey, $"consequence:{component.Operation}",
                resource.Quantity, updated.Quantity, resource.SymbolicState, updated.SymbolicState,
                resource.SupplyDieSides, updated.SupplyDieSides, consequenceId, resolutionProvenance));
        }

        var resourceState = resources with
        {
            Resources = working,
            History = resources.History.Concat(audits).ToArray()
        };
        resourceState.Validate(party);

        var remaining = pending.UnresolvedComponents.Where(value => value is not ResourceChangeConsequenceComponent).ToArray();
        var status = remaining.Length == 0 ? ExpeditionConsequenceStatus.Applied : RemainingStatus(remaining);
        var detail = remaining.Length == 0
            ? "Structured resource consequence applied to authoritative expedition resource state."
            : "Structured resource components applied; additional consequence components remain unresolved.";
        var records = effects.AppliedConsequences.Select(value => value.ConsequenceId == consequenceId
            ? value with
            {
                Status = status,
                Detail = detail,
                ResolutionProvenance = resolutionProvenance
            }
            : value).ToArray();
        var pendingItems = remaining.Length == 0
            ? effects.PendingConsequences.Where(value => value.Consequence.Id != consequenceId).ToArray()
            : effects.PendingConsequences.Select(value => value.Consequence.Id == consequenceId
                ? value with
                {
                    Status = status,
                    Reason = "Resource components were applied; other structured components remain unresolved.",
                    RequiredAction = RequiredAction(remaining),
                    UnresolvedComponents = remaining
                }
                : value).ToArray();
        var effectState = effects with
        {
            AppliedConsequences = records,
            PendingConsequences = pendingItems
        };
        effectState.Validate(party);
        return new(resourceState, effectState, status, detail, true);
    }

    private static (ExpeditionResource? Resource, ExpeditionConsequenceStatus Status, string? Detail) FindResource(
        IReadOnlyList<ExpeditionResource> resources,
        ExpeditionEffectTarget target,
        ResourceChangeConsequenceComponent component)
    {
        if (component.ResourceId.HasValue)
        {
            var resource = resources.SingleOrDefault(value => value.Id == component.ResourceId.Value);
            if (resource is null)
            {
                return (null, ExpeditionConsequenceStatus.InputRequired,
                    $"Resource id '{component.ResourceId.Value:D}' does not exist in expedition resource state.");
            }
            if (!string.Equals(resource.ResourceKey, component.ResourceKey, StringComparison.Ordinal)
                || resource.Target != target)
            {
                return (null, ExpeditionConsequenceStatus.RequiresAdjudication,
                    "The resolved resource id does not match the consequence resource key and target.");
            }
            return (resource, ExpeditionConsequenceStatus.Applied, null);
        }

        var matches = resources.Where(value =>
            string.Equals(value.ResourceKey, component.ResourceKey, StringComparison.Ordinal)
            && value.Target == target).ToArray();
        return matches.Length switch
        {
            1 => (matches[0], ExpeditionConsequenceStatus.Applied, null),
            0 => (null, ExpeditionConsequenceStatus.InputRequired,
                $"Resource '{component.ResourceKey}' does not exist for target '{target.MergeIdentity}'."),
            _ => (null, ExpeditionConsequenceStatus.RequiresAdjudication,
                $"More than one resource '{component.ResourceKey}' exists for the target; supply an explicit resource id.")
        };
    }

    private static (ExpeditionResource? Resource, ExpeditionConsequenceStatus Status, string? Detail) TryApply(
        ExpeditionResource resource,
        ResourceChangeConsequenceComponent component)
    {
        switch (component.Operation)
        {
            case ResourceChangeOperation.AdjustQuantity:
            case ResourceChangeOperation.SetQuantity:
                if (resource.InventoryModel != ExpeditionResourceInventoryModel.Counted)
                {
                    return (null, ExpeditionConsequenceStatus.Unsupported,
                        $"Quantity operation can not be applied to {resource.InventoryModel} resource '{resource.ResourceKey}'.");
                }
                if (!string.Equals(resource.Unit, component.Unit, StringComparison.Ordinal))
                {
                    return (null, ExpeditionConsequenceStatus.RequiresAdjudication,
                        $"Resource unit '{component.Unit}' does not match authoritative unit '{resource.Unit}'.");
                }
                var next = component.Operation == ResourceChangeOperation.AdjustQuantity
                    ? resource.Quantity!.Value + component.Quantity!.Value
                    : component.Quantity!.Value;
                if (!double.IsFinite(next) || next < 0)
                {
                    return (null, ExpeditionConsequenceStatus.RequiresAdjudication,
                        $"Resource '{resource.ResourceKey}' has insufficient quantity for the resolved operation; inventory was not silently clamped.");
                }
                return (resource with { Quantity = next }, ExpeditionConsequenceStatus.Applied, null);

            case ResourceChangeOperation.SetState:
                if (resource.InventoryModel != ExpeditionResourceInventoryModel.Abstract)
                {
                    return (null, ExpeditionConsequenceStatus.Unsupported,
                        $"Symbolic state can not be applied to {resource.InventoryModel} resource '{resource.ResourceKey}'.");
                }
                return (resource with { SymbolicState = component.State }, ExpeditionConsequenceStatus.Applied, null);

            case ResourceChangeOperation.SetSupplyDie:
                if (resource.InventoryModel != ExpeditionResourceInventoryModel.SupplyDie)
                {
                    return (null, ExpeditionConsequenceStatus.Unsupported,
                        $"Supply-die transition can not be applied to {resource.InventoryModel} resource '{resource.ResourceKey}'.");
                }
                return (resource with { SupplyDieSides = component.SupplyDieSides }, ExpeditionConsequenceStatus.Applied, null);

            case ResourceChangeOperation.Deplete:
                return resource.InventoryModel switch
                {
                    ExpeditionResourceInventoryModel.Counted =>
                        (resource with { Quantity = 0 }, ExpeditionConsequenceStatus.Applied, null),
                    ExpeditionResourceInventoryModel.SupplyDie =>
                        (resource with { SupplyDieSides = null }, ExpeditionConsequenceStatus.Applied, null),
                    _ => (null, ExpeditionConsequenceStatus.Unsupported,
                        $"Depletion is not a defined generic operation for {resource.InventoryModel} resource '{resource.ResourceKey}'.")
                };

            default:
                return (null, ExpeditionConsequenceStatus.Unsupported, "Unknown resource mutation operation is not executable.");
        }
    }

    private static ExpeditionConsequenceStatus RemainingStatus(IReadOnlyList<ExpeditionConsequenceComponent> remaining)
    {
        if (remaining.Any(value => value is ExternalStateConsequenceComponent)) return ExpeditionConsequenceStatus.ExternalActionRequired;
        if (remaining.Any(value => value is CustomConsequenceComponent)) return ExpeditionConsequenceStatus.Unsupported;
        if (remaining.Any(value => value is MovementChangeConsequenceComponent)) return ExpeditionConsequenceStatus.RequiresAdjudication;
        return ExpeditionConsequenceStatus.Deferred;
    }

    private static string RequiredAction(IReadOnlyList<ExpeditionConsequenceComponent> remaining)
    {
        if (remaining.Any(value => value is ExternalStateConsequenceComponent))
            return "Apply the remaining character-owned state in the owning Tool, then record its resolution.";
        if (remaining.Any(value => value is CustomConsequenceComponent))
            return "Resolve the remaining custom components with an explicit consumer or DM adjudication.";
        return "Resolve the remaining structured consequence components through their owning workflow.";
    }
}
