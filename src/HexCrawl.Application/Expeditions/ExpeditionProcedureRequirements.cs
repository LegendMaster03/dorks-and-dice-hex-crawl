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

    // Compatibility overloads retained for historical profile-only callers and tests.
    public static bool IsEncounterCheckDue(CrawlProcedureProfile profile, CrawlSessionRuntimeState state) =>
        IsEncounterCheckDue(GenericProcedureRuntime.FromLegacyProfile(profile), state);

    public static bool IsEncounterCheckDue(CrawlProcedureProfile profile, ExpeditionState state) =>
        IsEncounterCheckDue(GenericProcedureRuntime.FromLegacyProfile(profile), state);

    public static bool IsEncounterCheckDue(CrawlProcedureProfile profile, NonSpatialSessionState state) =>
        IsEncounterCheckDue(GenericProcedureRuntime.FromLegacyProfile(profile), state);

    public static bool IsNavigationResolutionPotentiallyRequired(CrawlProcedureProfile profile, ExpeditionState state) =>
        IsNavigationResolutionPotentiallyRequired(GenericProcedureRuntime.FromLegacyProfile(profile), state);
}
