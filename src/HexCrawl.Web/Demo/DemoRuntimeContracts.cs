using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Demo;

public sealed record DemoRuntimeStartRequest(
    string? ProfileKey,
    string? Orientation = null,
    double? Scale = null,
    string? Unit = null);

public sealed record DemoRuntimeAdvanceRequest
{
    public int IntendedDirection { get; init; }
    public string PaceKey { get; init; } = "normal";
    public IReadOnlyList<string> Activities { get; init; } = [];
    public string NavigationAidKey { get; init; } = "none";
    public bool SuppressesNavigationCheck { get; init; }
    public bool ResetsVeerAtBoundary { get; init; }
    public double? ExpectedDistance { get; init; }
    public double? ActualDistance { get; init; }
    public int? HexSteps { get; init; }
    public string ResolutionSource { get; init; } = "ManualRoll";
    public string? NavigationOutcome { get; init; }
    public int? VeerSteps { get; init; }
    public string? EncounterOutcome { get; init; }
    public double? EncounterHour { get; init; }
    public Guid? LocationId { get; init; }
    public string? EncounterNote { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public bool ContinueAcrossBoundaries { get; init; }
    public bool? RecognizedLost { get; init; }
    public bool? Reorient { get; init; }
    public string? DmOverrideNote { get; init; }
}

public sealed record DemoDiscoveryRequest(Guid SubjectId, string SubjectType, string? Source);

public sealed record DemoRuntimeProfileResponse(
    string Key,
    string Name,
    double WatchHours,
    string TravelResolution,
    string ActualDistanceResolution,
    string EncounterCadence,
    bool UsesNavigationChecks,
    bool UsesPersistentVeer,
    bool TracksIntraHexProgress)
{
    public static DemoRuntimeProfileResponse From(CrawlProcedureProfile profile) => new(
        profile.Key,
        profile.Name,
        profile.WatchLength.TotalHours,
        profile.TravelResolution.ToString(),
        profile.ActualDistanceResolution.ToString(),
        profile.EncounterCadence.ToString(),
        profile.UsesNavigationChecks,
        profile.UsesPersistentVeer,
        profile.TracksIntraHexProgress);
}

public sealed record DemoRuntimeResponse(
    DemoRuntimeProfileResponse Profile,
    DemoDistanceResponse HexCenterDistance,
    DemoExpeditionResponse Expedition,
    string? PauseReason,
    double RemainingWatchHours,
    IReadOnlyList<DemoKnowledgeEntryResponse> Knowledge,
    IReadOnlyList<DemoRuntimeEventResponse> History)
{
    public static DemoRuntimeResponse From(
        CrawlProcedureProfile profile,
        DistanceMeasure hexCenterDistance,
        ExpeditionState expedition,
        PlayerKnowledgeState knowledge,
        RuntimePauseReason? pauseReason,
        TimeSpan remainingWatchTime) => new(
            DemoRuntimeProfileResponse.From(profile),
            DemoDistanceResponse.From(hexCenterDistance),
            DemoExpeditionResponse.From(expedition),
            pauseReason?.ToString(),
            remainingWatchTime.TotalHours,
            knowledge.Entries.Values
                .OrderBy(item => item.SubjectType)
                .ThenBy(item => item.SubjectId)
                .Select(DemoKnowledgeEntryResponse.From)
                .ToArray(),
            expedition.History.Select(DemoRuntimeEventResponse.From).ToArray());
}

public sealed record DemoExpeditionResponse(
    Guid Id,
    HexCoordinate CurrentHex,
    WorldPoint? Position,
    string? PositionPrecision,
    int? IntendedDirection,
    int? ActualDirection,
    bool IsLost,
    int VeerSteps,
    double VeerDegrees,
    DemoDistanceResponse DistanceTraveled,
    DemoDistanceResponse HexProgress,
    DemoDistanceResponse? ExitRequirement,
    double ElapsedTravelHours,
    int CompletedWatches,
    int? ActiveWatchNumber,
    string? ActivePaceKey,
    IReadOnlyList<string> ActiveActivities,
    string? ActiveNavigationAidKey)
{
    public static DemoExpeditionResponse From(ExpeditionState expedition) => new(
        expedition.Id,
        expedition.CurrentHex,
        expedition.Position,
        expedition.PositionPrecision?.ToString(),
        expedition.IntendedDirection?.Value,
        expedition.ActualDirection?.Value,
        expedition.Navigation.IsLost,
        expedition.Navigation.VeerSteps,
        expedition.Navigation.VeerDegrees,
        DemoDistanceResponse.From(expedition.DistanceTraveled),
        DemoDistanceResponse.From(expedition.Traversal.Progress),
        expedition.Traversal.CurrentExitRequirement is { } requirement
            ? DemoDistanceResponse.From(requirement)
            : null,
        expedition.ElapsedTravelTime.TotalHours,
        expedition.CompletedWatches,
        expedition.ActiveWatch?.WatchNumber,
        expedition.ActiveWatch?.Plan.Mode.PaceKey,
        expedition.ActiveWatch?.Plan.Mode.Activities ?? [],
        expedition.ActiveWatch?.Plan.NavigationAid.Key);
}

public sealed record DemoKnowledgeEntryResponse(
    Guid SubjectId,
    string SubjectType,
    string State,
    string? Source)
{
    public static DemoKnowledgeEntryResponse From(KnowledgeEntry entry) => new(
        entry.SubjectId,
        entry.SubjectType.ToString(),
        entry.State.ToString(),
        entry.Source);
}

public sealed record DemoRuntimeEventResponse(
    long Sequence,
    int WatchNumber,
    string Kind,
    double ExpeditionElapsedHours,
    HexCoordinate? Hex,
    string Message,
    double? DistanceValue,
    string? DistanceUnit,
    Guid? SubjectId,
    string? SubjectType)
{
    public static DemoRuntimeEventResponse From(CrawlRuntimeEvent runtimeEvent) => new(
        runtimeEvent.Sequence,
        runtimeEvent.WatchNumber,
        runtimeEvent.Kind.ToString(),
        runtimeEvent.ExpeditionElapsedTime.TotalHours,
        runtimeEvent.Hex,
        runtimeEvent.Message,
        runtimeEvent.DistanceValue,
        runtimeEvent.DistanceUnit,
        runtimeEvent.SubjectId,
        runtimeEvent.SubjectType?.ToString());
}
