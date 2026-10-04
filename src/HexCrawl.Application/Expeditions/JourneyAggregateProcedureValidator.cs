using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

/// <summary>
/// Validates the persisted Phase 12 aggregate against the exact CampaignProcedure that owns its
/// execution semantics. This is intentionally separate from ExpeditionJourneyState.Validate,
/// which can validate only journey-internal relationships and party/consequence references.
/// </summary>
public static class JourneyAggregateProcedureValidator
{
    public static void Validate(CampaignProcedure procedure, ExpeditionJourneyState journey)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(journey);
        procedure.Validate();

        var processPolicy = JourneyProcedurePolicyResolver.ResolveProcess(procedure);
        var allProcesses = journey.ActiveProcesses.Concat(journey.ClosedProcesses).ToArray();
        if (allProcesses.Length > 0)
        {
            if (processPolicy.Support != JourneyPolicySupport.Supported)
            {
                throw new InvalidOperationException(
                    "Persisted journey processes require a supported exact-pinned journey.process policy.");
            }

            var execution = processPolicy.ToExecutionSnapshot();
            foreach (var process in allProcesses)
            {
                if (process.Execution != execution)
                {
                    throw new InvalidOperationException(
                        $"Journey process '{process.Id:D}' execution snapshot does not match the exact pinned journey.process policy.");
                }

                if (processPolicy.StageKeys.Count > 0)
                {
                    if (!processPolicy.StageKeys.SequenceEqual(process.Definition.StageOrder, StringComparer.Ordinal)
                        || !string.Equals(process.Definition.InitialStageKey, processPolicy.StageKeys[0], StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Journey process '{process.Id:D}' stage structure does not match the exact pinned journey.process policy.");
                    }
                }
            }
        }

        var activityPolicy = ParticipantActivityPolicyResolver.Resolve(procedure);
        var declaredRoles = activityPolicy.Support == ParticipantActivityPolicySupport.Supported
            ? activityPolicy.RoleKeys.ToHashSet(StringComparer.Ordinal)
            : [];
        var referencedRoles = allProcesses
            .SelectMany(process => process.Definition.Stages.SelectMany(stage => stage.RoleKeys))
            .Concat(allProcesses.SelectMany(process => process.PendingActions.Select(action => action.RequiredRoleKey)))
            .Concat(journey.Resolutions.Select(resolution => resolution.Actor?.RoleKey))
            .Concat(journey.EventOccurrences.Select(occurrence => occurrence.TargetRoleKey))
            .Concat(journey.EventOccurrences.Select(occurrence => occurrence.ParticipantSnapshot?.RoleKey))
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (referencedRoles.Any(role => !declaredRoles.Contains(role)))
        {
            throw new InvalidOperationException(
                "Persisted journey role references must be declared by the exact pinned party.activities policy.");
        }
        if (allProcesses.Any(process => process.Execution.RoleDriven)
            && (activityPolicy.Support != ParticipantActivityPolicySupport.Supported || declaredRoles.Count == 0))
        {
            throw new InvalidOperationException(
                "A persisted role-driven journey process requires a supported exact-pinned party.activities policy with role keys.");
        }

        var eventPolicy = JourneyProcedurePolicyResolver.ResolveEvents(procedure);
        if (journey.EventOccurrences.Count == 0) return;
        if (eventPolicy.Support != JourneyPolicySupport.Supported)
        {
            throw new InvalidOperationException(
                "Persisted journey events require a supported exact-pinned journey.events policy.");
        }

        foreach (var occurrence in journey.EventOccurrences)
        {
            if (!eventPolicy.Supports(occurrence.Trigger))
            {
                throw new InvalidOperationException(
                    $"Journey event '{occurrence.Id:D}' trigger is not declared by the exact pinned journey.events policy.");
            }
            if (occurrence.ProcessId.HasValue && eventPolicy.LinkMode == JourneyEventLinkMode.Standalone
                || !occurrence.ProcessId.HasValue && eventPolicy.LinkMode == JourneyEventLinkMode.ProcessLinked)
            {
                throw new InvalidOperationException(
                    $"Journey event '{occurrence.Id:D}' link relationship does not match the exact pinned journey.events policy.");
            }
            var allowUnresolved = occurrence.Status != JourneyEventStatus.Resolved;
            if (!eventPolicy.SupportsTarget(occurrence.TargetKind, allowUnresolved))
            {
                throw new InvalidOperationException(
                    $"Journey event '{occurrence.Id:D}' target does not match the exact pinned journey.events targeting model.");
            }
        }
    }
}
