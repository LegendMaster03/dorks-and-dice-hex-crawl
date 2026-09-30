using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public static class ExpeditionProcedureRequirements
{
    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, CrawlSessionRuntimeState state) =>
        GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);

    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, ExpeditionState state) =>
        GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);

    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, NonSpatialSessionState state) =>
        GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);

    public static bool IsNavigationResolutionPotentiallyRequired(GenericProcedureRuntime procedure, ExpeditionState state) =>
        GenericProcedureRuntimeRequirements.IsNavigationResolutionPotentiallyRequired(procedure, state);

    public static TimeSpan ResolveIntervalDuration(CampaignProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);

        var selected = procedure.Modules.SingleOrDefault(module =>
            string.Equals(module.Module.Key, GenericProcedureCatalog.TimeIntervalModule, StringComparison.Ordinal));
        if (selected is null)
        {
            throw new InvalidOperationException("The pinned campaign procedure does not define a time.interval module.");
        }
        if (!string.Equals(
                selected.Mechanic.Key,
                GenericProcedureCatalog.FixedIntervalDurationMechanic,
                StringComparison.Ordinal)
            || selected.Mechanic.Version != 1)
        {
            throw new InvalidOperationException(
                $"The pinned time.interval mechanic '{selected.Mechanic.Key}' version {selected.Mechanic.Version} is not supported by focused interval bookkeeping.");
        }
        if (!selected.Parameters.TryGetValue("durationTicks", out var rawTicks)
            || !long.TryParse(rawTicks, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            || ticks <= 0)
        {
            throw new InvalidOperationException(
                "The pinned time.interval mechanic requires a positive durationTicks value.");
        }

        return TimeSpan.FromTicks(ticks);
    }
}
