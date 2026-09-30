using System.Globalization;
using HexCrawl.Domain.Procedure;

namespace HexCrawl.Application;

public enum FocusedIntervalPolicySupport
{
    None,
    Supported,
    Unsupported
}

public sealed record FocusedIntervalPolicy(
    FocusedIntervalPolicySupport Support,
    TimeSpan? IntervalDuration,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static FocusedIntervalPolicy None { get; } = new(
        FocusedIntervalPolicySupport.None,
        null,
        null,
        null,
        null,
        null);
}

/// <summary>
/// Interprets only the exact materialized time.interval module stored on a CampaignProcedure.
/// It deliberately does not consult origin preset identity, current preset recipes, or current
/// catalog parameter values.
/// </summary>
public static class FocusedIntervalPolicyResolver
{
    public static FocusedIntervalPolicy Resolve(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);

        var selected = procedure.Modules.SingleOrDefault(module =>
            string.Equals(module.Module.Key, GenericProcedureCatalog.TimeIntervalModule, StringComparison.Ordinal));
        if (selected is null)
        {
            return FocusedIntervalPolicy.None;
        }

        if (!string.Equals(
                selected.Mechanic.Key,
                GenericProcedureCatalog.FixedIntervalDurationMechanic,
                StringComparison.Ordinal)
            || selected.Mechanic.Version != 1
            || !string.Equals(
                selected.Mechanic.ExecutionHandler,
                GenericProcedureExecutionHandlers.FixedIntervalDuration,
                StringComparison.Ordinal))
        {
            return Unsupported(
                selected,
                $"Interval mechanic '{selected.Mechanic.Key}' version {selected.Mechanic.Version} is not supported by focused interval bookkeeping.");
        }

        if (!selected.Parameters.TryGetValue("durationTicks", out var rawTicks)
            || !long.TryParse(rawTicks, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            || ticks <= 0)
        {
            return Unsupported(
                selected,
                "The stored fixed interval mechanic requires a positive durationTicks value.");
        }

        return new FocusedIntervalPolicy(
            FocusedIntervalPolicySupport.Supported,
            TimeSpan.FromTicks(ticks),
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            null);
    }

    public static TimeSpan RequireSupportedDuration(CampaignProcedure procedure)
    {
        var policy = Resolve(procedure);
        return policy.Support switch
        {
            FocusedIntervalPolicySupport.Supported when policy.IntervalDuration is { } duration => duration,
            FocusedIntervalPolicySupport.None => throw new InvalidOperationException(
                "The pinned campaign procedure does not define a time.interval module."),
            FocusedIntervalPolicySupport.Unsupported => throw new InvalidOperationException(
                policy.UnsupportedReason
                ?? "The pinned time.interval mechanic is not supported by focused interval bookkeeping."),
            _ => throw new InvalidOperationException(
                "The focused interval policy is supported but does not contain an interval duration.")
        };
    }

    private static FocusedIntervalPolicy Unsupported(
        MaterializedProcedureModule selected,
        string reason) => new(
            FocusedIntervalPolicySupport.Unsupported,
            null,
            selected.Mechanic.Key,
            selected.Mechanic.Version,
            selected.Mechanic.ExecutionHandler,
            reason);
}
