using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public static class ExpeditionProcedureRequirements
{
    public static bool IsEncounterCheckDue(CrawlProcedureProfile profile, ExpeditionState state)
    {
        if (state.ActiveWatch is not null)
        {
            return false;
        }

        return IsEncounterCheckDue(
            profile,
            state.History,
            Math.Max(1, state.CompletedWatches + 1),
            state.ElapsedTravelTime);
    }

    public static bool IsEncounterCheckDue(CrawlProcedureProfile profile, NonSpatialSessionState state) =>
        IsEncounterCheckDue(
            profile,
            state.History,
            state.ActiveWatch?.WatchNumber ?? Math.Max(1, state.CompletedWatches + 1),
            state.ElapsedTime);

    public static bool IsNavigationResolutionPotentiallyRequired(CrawlProcedureProfile profile, ExpeditionState state) =>
        state.ActiveWatch is null && profile.UsesNavigationChecks;

    private static bool IsEncounterCheckDue(
        CrawlProcedureProfile profile,
        IReadOnlyList<CrawlRuntimeEvent> history,
        int watchNumber,
        TimeSpan elapsed)
    {
        if (profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return false;
        }

        if (profile.EncounterCadence is EncounterCheckCadence.PerWatch or EncounterCheckCadence.Custom)
        {
            return !history.Any(runtimeEvent =>
                runtimeEvent.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
                && runtimeEvent.WatchNumber == watchNumber);
        }

        var day = DayIndex(elapsed);
        return !history.Any(runtimeEvent =>
            runtimeEvent.Kind == CrawlRuntimeEventKind.EncounterCheckPerformed
            && DayIndex(runtimeEvent.ExpeditionElapsedTime) == day);
    }

    private static int DayIndex(TimeSpan elapsed) => (int)Math.Floor(elapsed.TotalDays);
}
