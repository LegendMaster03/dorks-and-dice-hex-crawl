using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record ForcedTravelAccountingResult(
    ExpeditionSurvivalState Survival,
    SurvivalOperationStatus Status,
    string Detail,
    bool StateChanged);

/// <summary>
/// Projects an authoritative runtime travel mutation into the exact pinned forced-travel unit.
/// Unit relationships are accepted only when they are explicit in the stored procedure: hours are
/// measured directly, intervals use the exact pinned interval duration, and other units require a
/// matching movement-budget unit on the same pinned procedure. No preset identity participates.
/// </summary>
public static class ForcedTravelAccounting
{
    public static ForcedTravelAccountingResult AccountTravelMutation(
        StoredExpedition expedition,
        CrawlSessionRuntimeState before,
        CrawlSessionRuntimeState after,
        long expectedVersion,
        string operationKey,
        ExpeditionConsequenceProvenance provenance)
    {
        var policy = Phase11ProcedurePolicyResolver.ResolveForcedTravel(expedition.CampaignProcedure);
        if (policy.Support == Phase11PolicySupport.None)
        {
            return new(expedition.Survival, SurvivalOperationStatus.Recorded,
                "The exact pinned procedure has no forced-travel policy.", false);
        }
        if (policy.Support == Phase11PolicySupport.Unsupported)
        {
            return new(expedition.Survival, SurvivalOperationStatus.Unsupported,
                policy.UnsupportedReason ?? "The exact pinned forced-travel policy is unsupported.", false);
        }

        var elapsed = Elapsed(after) - Elapsed(before);
        if (elapsed < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Authoritative travel elapsed time can not move backward during forced-travel accounting.");
        }
        if (elapsed == TimeSpan.Zero)
        {
            return new(expedition.Survival, SurvivalOperationStatus.NotDue,
                "The successful mutation advanced no travel time, so forced-travel usage did not change.", false);
        }

        var unit = policy.LimitUnit!;
        var amount = ResolveAmount(expedition.CampaignProcedure, elapsed, unit);
        if (!amount.HasValue)
        {
            throw new InvalidOperationException(
                $"The pinned forced-travel policy uses '{unit}', but the exact stored procedure does not establish a safe relationship from authoritative travel time to that unit. Resolve usage through the typed forced-travel operation instead of guessing a conversion.");
        }

        var occurrenceKey = $"{operationKey}:{expedition.Id:D}:{expectedVersion.ToString(CultureInfo.InvariantCulture)}";
        var transition = Phase11SurvivalEngine.AccountForcedTravel(
            expedition.Survival,
            policy,
            expedition.Party,
            amount.Value,
            unit,
            occurrenceKey,
            provenance);
        return new(transition.State, transition.Status, transition.Detail, transition.StateChanged);
    }

    private static double? ResolveAmount(
        HexCrawl.Domain.Procedure.CampaignProcedure procedure,
        TimeSpan elapsed,
        string limitUnit)
    {
        if (string.Equals(limitUnit, "hours", StringComparison.Ordinal))
        {
            return elapsed.TotalHours;
        }

        var intervalPolicy = FocusedIntervalPolicyResolver.Resolve(procedure);
        if (intervalPolicy.Support != FocusedIntervalSupport.Supported
            || intervalPolicy.Duration is not { } interval
            || interval <= TimeSpan.Zero)
        {
            return null;
        }

        if (string.Equals(limitUnit, "intervals", StringComparison.Ordinal))
        {
            return elapsed.TotalMilliseconds / interval.TotalMilliseconds;
        }

        var movementBudget = procedure.Modules.SingleOrDefault(value =>
            string.Equals(value.Module.Key, GenericProcedureCatalog.MovementBudgetModule, StringComparison.Ordinal));
        if (movementBudget is null
            || !movementBudget.Parameters.TryGetValue("budgetUnit", out var budgetUnit)
            || !SameOpenUnit(budgetUnit, limitUnit)
            || !movementBudget.Parameters.TryGetValue("baseBudget", out var rawBaseBudget)
            || !double.TryParse(rawBaseBudget, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseBudget)
            || !double.IsFinite(baseBudget)
            || baseBudget <= 0)
        {
            return null;
        }

        return elapsed.TotalMilliseconds / interval.TotalMilliseconds * baseBudget;
    }

    private static bool SameOpenUnit(string left, string right)
    {
        static string Normalize(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            return normalized.EndsWith('s') ? normalized[..^1] : normalized;
        }
        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
    }

    private static TimeSpan Elapsed(CrawlSessionRuntimeState state) => state switch
    {
        ExpeditionState spatial => spatial.ElapsedTravelTime,
        NonSpatialSessionState nonSpatial => nonSpatial.ElapsedTime,
        _ => throw new InvalidOperationException("Unsupported crawl session runtime state for forced-travel accounting.")
    };
}
