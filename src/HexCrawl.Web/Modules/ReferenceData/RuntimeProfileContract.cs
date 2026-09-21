using HexCrawl.Domain.Procedure;

namespace HexCrawl.Web.Api;

public sealed record RuntimeProfileContract(
    string Key,
    string Name,
    double WatchHours,
    TravelResolutionMode TravelResolution,
    ActualDistanceResolutionMode ActualDistanceResolution,
    EncounterCheckCadence EncounterCadence,
    bool UsesNavigationChecks,
    bool UsesPersistentVeer,
    bool TracksIntraHexProgress,
    bool DirectionChangesCostProgress,
    bool SupportsDeliberateDoubleBack,
    double StartingExitProgressFactor,
    double NearExitProgressFactor,
    double FarExitProgressFactor,
    double BackExitProgressFactor,
    double DirectionChangeProgressCostFactor)
{
    public static RuntimeProfileContract From(CrawlProcedureProfile profile) => new(
        profile.Key,
        profile.Name,
        profile.WatchLength.TotalHours,
        profile.TravelResolution,
        profile.ActualDistanceResolution,
        profile.EncounterCadence,
        profile.UsesNavigationChecks,
        profile.UsesPersistentVeer,
        profile.TracksIntraHexProgress,
        profile.DirectionChangesCostProgress,
        profile.SupportsDeliberateDoubleBack,
        profile.StartingExitProgressFactor,
        profile.NearExitProgressFactor,
        profile.FarExitProgressFactor,
        profile.BackExitProgressFactor,
        profile.DirectionChangeProgressCostFactor);
}

