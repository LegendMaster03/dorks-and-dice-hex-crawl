using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public enum SurvivalOperationStatus
{
    Applied,
    AlreadyApplied,
    NotDue,
    InputRequired,
    RequiresAdjudication,
    Unsupported,
    ExternalActionRequired,
    Recorded
}

public sealed record SurvivalStateTransition(
    ExpeditionSurvivalState State,
    SurvivalOperationStatus Status,
    string Detail,
    bool StateChanged);

public static class Phase11SurvivalEngine
{
    public static SurvivalStateTransition AccountForcedTravel(
        ExpeditionSurvivalState state,
        ForcedTravelPolicy policy,
        CrawlPartySheet party,
        double amount,
        string unit,
        string occurrenceKey,
        ExpeditionConsequenceProvenance provenance)
    {
        state.Validate(party);
        provenance.Validate();
        if (policy.Support == Phase11PolicySupport.None)
        {
            return new(state, SurvivalOperationStatus.Recorded,
                "The exact pinned CampaignProcedure has no time.forced-travel policy.", false);
        }
        if (policy.Support == Phase11PolicySupport.Unsupported)
        {
            return new(state, SurvivalOperationStatus.Unsupported,
                policy.UnsupportedReason ?? "The exact pinned forced-travel policy is unsupported.", false);
        }
        var normalLimit = policy.NormalTravelLimit!.Value;
        if (!double.IsFinite(amount) || amount <= 0)
        {
            throw new InvalidOperationException("Forced-travel accounting amount must be positive and finite.");
        }
        if (string.IsNullOrWhiteSpace(unit) || !string.Equals(unit.Trim(), policy.LimitUnit, StringComparison.Ordinal))
        {
            return new(state, SurvivalOperationStatus.RequiresAdjudication,
                $"Resolved travel unit '{unit}' does not match the pinned forced-travel unit '{policy.LimitUnit}'. No implicit conversion was performed.", false);
        }
        if (string.IsNullOrWhiteSpace(occurrenceKey))
        {
            throw new InvalidOperationException("Forced-travel accounting requires a stable occurrence identity.");
        }

        var current = state.ForcedTravel;
        if (current.AccountedTravelOccurrences.Contains(occurrenceKey, StringComparer.Ordinal))
        {
            return new(state, SurvivalOperationStatus.AlreadyApplied,
                $"Travel occurrence '{occurrenceKey}' was already counted toward forced travel.", false);
        }
        if (current.Unit is not null && !string.Equals(current.Unit, unit, StringComparison.Ordinal))
        {
            return new(state, SurvivalOperationStatus.RequiresAdjudication,
                "Current forced-travel progress uses a different unit. Reset explicitly before changing unit models.", false);
        }

        var nextAmount = current.AmountSinceReset + amount;
        if (!double.IsFinite(nextAmount))
        {
            throw new InvalidOperationException("Forced-travel progress exceeds the supported numeric range.");
        }
        var pending = current.PendingCheck;
        if (pending is null
            && nextAmount > normalLimit
            && nextAmount > current.LastResolvedAmount)
        {
            pending = new PendingForcedTravelCheck(
                Guid.NewGuid(), Guid.NewGuid(), nextAmount, unit.Trim(), provenance);
        }
        var next = current with
        {
            AmountSinceReset = nextAmount,
            Unit = unit.Trim(),
            PendingCheck = pending,
            AccountedTravelOccurrences = current.AccountedTravelOccurrences.Append(occurrenceKey.Trim()).ToArray()
        };
        var updated = state with { ForcedTravel = next };
        updated.Validate(party);
        var detail = pending is not null && current.PendingCheck is null
            ? "Travel was accounted and continuing beyond the pinned normal limit created a forced-travel check due state."
            : nextAmount >= normalLimit
                ? "Travel was accounted at or beyond the pinned normal-travel threshold."
                : "Travel was accounted below the pinned normal-travel threshold.";
        return new(updated, SurvivalOperationStatus.Applied, detail, true);
    }

    public static SurvivalStateTransition ResetForcedTravel(
        ExpeditionSurvivalState state,
        CrawlPartySheet party,
        ExpeditionConsequenceProvenance provenance)
    {
        state.Validate(party);
        provenance.Validate();
        var updated = state with { ForcedTravel = ForcedTravelState.Empty };
        updated.Validate(party);
        return new(updated, SurvivalOperationStatus.Applied,
            "Forced-travel progress was explicitly reset; camping or time passage does not reset it implicitly.", true);
    }
}
