using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public static class ExpeditionProcedureRequirements
{
    public static bool IsEncounterCheckDue(CrawlProcedureProfile profile, CrawlSessionRuntimeState state)
    {
        var activeWatch = state switch
        {
            ExpeditionState spatial => spatial.ActiveWatch is not null,
            NonSpatialSessionState nonSpatial => nonSpatial.ActiveWatch is not null,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        if (activeWatch || profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return false;
        }
        if (profile.EncounterCadence is EncounterCheckCadence.PerWatch or EncounterCheckCadence.Custom)
        {
            return true;
        }

        var elapsed = state switch
        {
            ExpeditionState spatial => spatial.ElapsedTravelTime,
            NonSpatialSessionState nonSpatial => nonSpatial.ElapsedTime,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        var day = DayIndex(elapsed);
        return !state.History.Any(runtimeEvent =>
            runtimeEvent.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
            && DayIndex(runtimeEvent.ExpeditionElapsedTime) == day);
    }

    public static bool IsNavigationResolutionPotentiallyRequired(CrawlProcedureProfile profile, ExpeditionState state) =>
        state.ActiveWatch is null && profile.UsesNavigationChecks;

    private static int DayIndex(TimeSpan elapsed) => (int)Math.Floor(elapsed.TotalDays);
}
