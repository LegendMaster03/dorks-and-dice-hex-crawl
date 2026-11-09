using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

/// <summary>
/// Shared facade for procedure-derived due checks and interval resolution;
/// world-bound and nonspatial sessions use the same generic runtime policies.
/// </summary>
public static class ExpeditionProcedureRequirements
{
    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, CrawlSessionRuntimeState state) =>
        GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);

    public static bool IsEncounterCheckDue(GenericProcedureRuntime procedure, NonSpatialSessionState state) =>
        GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);

    public static TimeSpan ResolveIntervalDuration(CampaignProcedure procedure) =>
        FocusedIntervalPolicyResolver.RequireSupportedDuration(procedure);
}
