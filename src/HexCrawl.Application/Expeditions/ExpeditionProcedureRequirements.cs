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

    public static TimeSpan ResolveIntervalDuration(CampaignProcedure procedure) =>
        FocusedIntervalPolicyResolver.RequireSupportedDuration(procedure);
}
