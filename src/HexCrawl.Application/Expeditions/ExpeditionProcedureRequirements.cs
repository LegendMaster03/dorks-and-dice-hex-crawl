using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public static class ExpeditionProcedureRequirements
{
    public static bool IsEncounterCheckDue(CrawlProcedureProfile profile, ExpeditionState state)
    {
        if (state.ActiveWatch is not null || profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return false;
        }
        if (profile.EncounterCadence is EncounterCheckCadence.PerWatch or EncounterCheckCadence.Custom)
        {
            return true;
        }

        var day = DayIndex(state.ElapsedTravelTime);
        return !state.History.Any(runtimeEvent =>
            runtimeEvent.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
            && DayIndex(runtimeEvent.ExpeditionElapsedTime) == day);
    }

    public static bool IsNavigationResolutionPotentiallyRequired(CrawlProcedureProfile profile, ExpeditionState state) =>
        state.ActiveWatch is null && profile.UsesNavigationChecks;

    private static int DayIndex(TimeSpan elapsed) => (int)Math.Floor(elapsed.TotalDays);
}
