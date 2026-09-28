using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

public sealed record ExpeditionStateContract(
    Guid Id,
    HexCoordinate CurrentHex,
    WorldPoint? Position,
    WorldPositionPrecision? PositionPrecision,
    int? IntendedDirection,
    int? ActualDirection,
    bool IsLost,
    int VeerSteps,
    double VeerDegrees,
    DistanceContract DistanceTraveled,
    DistanceContract HexProgress,
    DistanceContract? ExitRequirement,
    double ElapsedTravelHours,
    int CompletedWatches,
    int? ActiveWatchNumber,
    string? ActivePaceKey,
    IReadOnlyList<string> ActiveActivities,
    string? ActiveNavigationAidKey)
{
    public static ExpeditionStateContract From(ExpeditionState expedition) => new(
        expedition.Id,
        expedition.CurrentHex,
        expedition.Position,
        expedition.PositionPrecision,
        expedition.IntendedDirection?.Value,
        expedition.ActualDirection?.Value,
        expedition.Navigation.IsLost,
        expedition.Navigation.VeerSteps,
        expedition.Navigation.VeerDegrees,
        DistanceContract.From(expedition.DistanceTraveled),
        DistanceContract.From(expedition.Traversal.Progress),
        expedition.Traversal.CurrentExitRequirement is { } requirement ? DistanceContract.From(requirement) : null,
        expedition.ElapsedTravelTime.TotalHours,
        expedition.CompletedWatches,
        expedition.ActiveWatch?.WatchNumber,
        expedition.ActiveWatch?.Plan.Mode.PaceKey,
        expedition.ActiveWatch?.Plan.Mode.Activities ?? [],
        expedition.ActiveWatch?.Plan.NavigationAid.Key);
}

public sealed record KnowledgeEntryContract(
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    KnowledgeState State,
    DateTimeOffset? LearnedAt,
    string? Source)
{
    public static KnowledgeEntryContract From(KnowledgeEntry entry) => new(
        entry.SubjectId, entry.SubjectType, entry.State, entry.LearnedAt, entry.Source);
}

public sealed record RuntimeEventContract(
    long Sequence,
    int WatchNumber,
    CrawlRuntimeEventKind Kind,
    double ExpeditionElapsedHours,
    HexCoordinate? Hex,
    string Message,
    double? DistanceValue,
    string? DistanceUnit,
    Guid? SubjectId,
    KnowledgeSubjectType? SubjectType)
{
    public static RuntimeEventContract From(CrawlRuntimeEvent runtimeEvent) => new(
        runtimeEvent.Sequence,
        runtimeEvent.WatchNumber,
        runtimeEvent.Kind,
        runtimeEvent.ExpeditionElapsedTime.TotalHours,
        runtimeEvent.Hex,
        runtimeEvent.Message,
        runtimeEvent.DistanceValue,
        runtimeEvent.DistanceUnit,
        runtimeEvent.SubjectId,
        runtimeEvent.SubjectType);
}

public sealed record ExpeditionContract(
    Guid Id,
    CrawlSessionContextKind ContextKind,
    Guid? OverworldId,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RuntimeProfileContract Profile,
    RuntimePauseReason? PauseReason,
    double RemainingWatchHours,
    ExpeditionStateContract? Expedition,
    IReadOnlyList<KnowledgeEntryContract> Knowledge,
    IReadOnlyList<RuntimeEventContract> History)
{
    public static ExpeditionContract From(StoredExpedition expedition) => new(
        expedition.Id,
        expedition.Context.Kind,
        expedition.Context.OverworldId,
        expedition.Name,
        expedition.Version,
        expedition.CreatedAt,
        expedition.UpdatedAt,
        RuntimeProfileContract.From(expedition.Procedure),
        expedition.PauseReason,
        expedition.RemainingWatchTime.TotalHours,
        expedition.Runtime is ExpeditionState spatial ? ExpeditionStateContract.From(spatial) : null,
        expedition.Knowledge?.Entries.Values
            .OrderBy(item => item.SubjectType)
            .ThenBy(item => item.SubjectId)
            .Select(KnowledgeEntryContract.From)
            .ToArray() ?? [],
        expedition.Runtime.History.Select(RuntimeEventContract.From).ToArray());
}

public sealed record StartExpeditionRequest(string Name, string ProcedureKey, HexCoordinate StartHex)
{
    public StartExpeditionCommand ToCommand() => new(Name, ProcedureKey, StartHex);
}

public sealed record AdvanceExpeditionRequest
{
    public long ExpectedVersion { get; init; }
    public int IntendedDirection { get; init; }
    public string PaceKey { get; init; } = "normal";
    public IReadOnlyList<string> Activities { get; init; } = [];
    public string NavigationAidKey { get; init; } = "none";
    public bool SuppressesNavigationCheck { get; init; }
    public bool ResetsVeerAtBoundary { get; init; }
    public double? ExpectedDistance { get; init; }
    public double? ActualDistance { get; init; }
    public int? HexSteps { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public NavigationCheckOutcome? NavigationOutcome { get; init; }
    public int? VeerSteps { get; init; }
    public EncounterOutcomeKind? EncounterOutcome { get; init; }
    public double? EncounterHour { get; init; }
    public Guid? LocationId { get; init; }
    public string? EncounterNote { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public bool ContinueAcrossBoundaries { get; init; }
    public bool? RecognizedLost { get; init; }
    public bool? Reorient { get; init; }
    public string? DmOverrideNote { get; init; }

    public AdvanceExpeditionCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        IntendedDirection = IntendedDirection,
        PaceKey = PaceKey,
        Activities = Activities,
        NavigationAidKey = NavigationAidKey,
        SuppressesNavigationCheck = SuppressesNavigationCheck,
        ResetsVeerAtBoundary = ResetsVeerAtBoundary,
        ExpectedDistance = ExpectedDistance,
        ActualDistance = ActualDistance,
        HexSteps = HexSteps,
        ResolutionSource = ResolutionSource,
        NavigationOutcome = NavigationOutcome,
        VeerSteps = VeerSteps,
        EncounterOutcome = EncounterOutcome,
        EncounterHour = EncounterHour,
        LocationId = LocationId,
        EncounterNote = EncounterNote,
        DeliberateDoubleBack = DeliberateDoubleBack,
        ContinueAcrossBoundaries = ContinueAcrossBoundaries,
        RecognizedLost = RecognizedLost,
        Reorient = Reorient,
        DmOverrideNote = DmOverrideNote
    };
}

public sealed record DiscoverSubjectRequest(
    long ExpectedVersion,
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    string? Source)
{
    public DiscoverSubjectCommand ToCommand() => new(ExpectedVersion, SubjectId, SubjectType, Source);
}
