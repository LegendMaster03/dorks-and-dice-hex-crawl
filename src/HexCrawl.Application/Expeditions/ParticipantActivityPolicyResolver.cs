using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public enum ParticipantActivityPolicySupport
{
    None,
    Supported,
    Unsupported
}

public sealed record ParticipantActivityPolicy(
    ParticipantActivityPolicySupport Support,
    ParticipantActivityAssignmentScope? AssignmentScope,
    string? ActivityBudgetModel,
    IReadOnlyList<string> ActivityKeys,
    IReadOnlyList<string> RoleKeys,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static ParticipantActivityPolicy None { get; } = new(
        ParticipantActivityPolicySupport.None,
        null,
        null,
        [],
        [],
        null,
        null,
        null,
        null);
}

/// <summary>
/// Interprets only the exact materialized party.activities module stored on a CampaignProcedure.
/// It deliberately does not consult presets, origin metadata, or the current catalog parameter values.
/// </summary>
public static class ParticipantActivityPolicyResolver
{
    public static ParticipantActivityPolicy Resolve(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        var selected = procedure.Modules.SingleOrDefault(module =>
            string.Equals(module.Module.Key, GenericProcedureCatalog.PartyActivitiesModule, StringComparison.Ordinal));
        if (selected is null)
        {
            return ParticipantActivityPolicy.None;
        }

        if (!IsSupportedMechanic(selected.Mechanic))
        {
            return Unsupported(
                selected,
                $"Activity mechanic '{selected.Mechanic.Key}' version {selected.Mechanic.Version} is not supported by the typed participant-activity editor.");
        }

        if (!selected.Parameters.TryGetValue("assignmentScope", out var scopeValue)
            || !TryParseScope(scopeValue, out var scope))
        {
            return Unsupported(selected, "The stored activity policy has an unsupported assignmentScope value.");
        }
        if (!selected.Parameters.TryGetValue("activityBudgetModel", out var budgetModel)
            || string.IsNullOrWhiteSpace(budgetModel))
        {
            return Unsupported(selected, "The stored activity policy has no usable activityBudgetModel value.");
        }
        if (!selected.Parameters.TryGetValue("activityKeys", out var activityValue)
            || !TryParseKeyList(activityValue, noneSentinel: false, out var activityKeys))
        {
            return Unsupported(selected, "The stored activity policy has an invalid activityKeys list.");
        }
        if (!selected.Parameters.TryGetValue("roleKeys", out var roleValue)
            || !TryParseKeyList(roleValue, noneSentinel: true, out var roleKeys))
        {
            return Unsupported(selected, "The stored activity policy has an invalid roleKeys list.");
        }

        return new ParticipantActivityPolicy(
            ParticipantActivityPolicySupport.Supported,
            scope,
            budgetModel.Trim(),
            activityKeys,
            roleKeys,
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            null);
    }

    public static void ValidateAssignments(
        CrawlPartySheet party,
        ParticipantActivityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(policy);
        party.Validate();

        if (policy.Support == ParticipantActivityPolicySupport.Unsupported)
        {
            return;
        }
        if (policy.Support == ParticipantActivityPolicySupport.None)
        {
            if (party.ActivityAssignments.Count > 0)
            {
                throw new InvalidOperationException(
                    "The stored campaign procedure does not define participant activity assignments.");
            }
            return;
        }

        var scope = policy.AssignmentScope
            ?? throw new InvalidOperationException("Supported participant activity policy has no assignment scope.");
        foreach (var assignment in party.ActivityAssignments)
        {
            if (assignment.Scope != scope)
            {
                throw new InvalidOperationException(
                    $"Activity assignment scope '{assignment.Scope}' does not match the stored procedure scope '{scope}'.");
            }
            if (assignment.ActivityKey is not null
                && !policy.ActivityKeys.Contains(assignment.ActivityKey, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Activity key '{assignment.ActivityKey}' is not available in the stored campaign procedure.");
            }
            if (assignment.RoleKey is not null
                && !policy.RoleKeys.Contains(assignment.RoleKey, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Role key '{assignment.RoleKey}' is not available in the stored campaign procedure.");
            }
        }
    }

    public static IReadOnlyList<ParticipantActivityAssignment> SnapshotAssignments(CrawlPartySheet party)
    {
        ArgumentNullException.ThrowIfNull(party);
        party.Validate();
        return party.ActivityAssignments.Select(value => value with { }).ToArray();
    }

    private static bool IsSupportedMechanic(MechanicDefinition mechanic) =>
        mechanic.Version == 1
        && string.Equals(mechanic.ExecutionHandler, GenericProcedureExecutionHandlers.DeclarativeContract, StringComparison.Ordinal)
        && (string.Equals(mechanic.Key, GenericProcedureCatalog.ParticipantActivityPolicyMechanic, StringComparison.Ordinal)
            || string.Equals(mechanic.Key, GenericProcedureCatalog.JourneyRoleActivityPolicyMechanic, StringComparison.Ordinal));

    private static ParticipantActivityPolicy Unsupported(
        MaterializedProcedureModule selected,
        string reason) => new(
            ParticipantActivityPolicySupport.Unsupported,
            null,
            null,
            [],
            [],
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            reason);

    private static bool TryParseScope(
        string? value,
        out ParticipantActivityAssignmentScope scope)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "party":
                scope = ParticipantActivityAssignmentScope.Party;
                return true;
            case "participant":
                scope = ParticipantActivityAssignmentScope.Participant;
                return true;
            case "role":
                scope = ParticipantActivityAssignmentScope.Role;
                return true;
            default:
                scope = default;
                return false;
        }
    }

    private static bool TryParseKeyList(
        string? value,
        bool noneSentinel,
        out IReadOnlyList<string> keys)
    {
        keys = [];
        if (value is null)
        {
            return false;
        }

        var raw = value.Split(';', StringSplitOptions.None)
            .Select(item => item.Trim())
            .ToArray();
        if (raw.Length == 0 || raw.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }
        if (noneSentinel
            && raw.Length == 1
            && string.Equals(raw[0], "none", StringComparison.OrdinalIgnoreCase))
        {
            keys = [];
            return true;
        }

        keys = raw.Distinct(StringComparer.Ordinal).ToArray();
        return true;
    }
}
