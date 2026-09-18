using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public sealed record PresentationProfileContract(
    string Key,
    string Name,
    GridVisibility PlayerGrid,
    TerrainPresentationMode TerrainMode,
    PresentationAutomationMode AutomationMode,
    bool MarkEnteredHexKnown,
    IReadOnlyList<string> InitiallyKnownFeatureCategories,
    IReadOnlyList<string> InitiallyKnownLocationCategories,
    bool AllowPlayerAnnotations)
{
    public static PresentationProfileContract From(MapPresentationPolicy policy) => new(
        policy.Key,
        policy.Name,
        policy.PlayerGrid,
        policy.TerrainMode,
        policy.AutomationMode,
        policy.MarkEnteredHexKnown,
        policy.InitiallyKnownFeatureCategories,
        policy.InitiallyKnownLocationCategories,
        policy.AllowPlayerAnnotations);
}

public sealed record WorkbenchExpeditionStateContract(
    Guid Id,
    HexCoordinate CurrentHex,
    WorldPoint Position,
    WorldPositionPrecision PositionPrecision,
    int? EntryDirection,
    int? LastTravelDirection,
    int? IntendedDirection,
    int? ActualDirection,
    bool IsLost,
    int VeerSteps,
    double VeerDegrees,
    DistanceContract DistanceTraveled,
    DistanceContract HexProgress,
    DistanceContract? ExitRequirement,
    double ElapsedTravelHours,
    int CurrentDay,
    int CompletedWatches,
    int? ActiveWatchNumber,
    double? ActiveWatchTotalHours,
    double? ActiveWatchElapsedHours,
    double? ActiveWatchRemainingHours,
    RuntimePauseReason? ActiveWatchPendingDecision,
    string? ActivePaceKey,
    IReadOnlyList<string> ActiveActivities,
    string? ActiveNavigationAidKey,
    bool ActiveDeliberateDoubleBack,
    bool ActiveContinueAcrossBoundaries,
    EncounterOutcomeKind? ActiveEncounterKind,
    double? ActiveEncounterHour,
    bool? ActiveEncounterHandled)
{
    public static WorkbenchExpeditionStateContract From(ExpeditionState expedition) => new(
        expedition.Id,
        expedition.CurrentHex,
        expedition.Position,
        expedition.PositionPrecision,
        expedition.Traversal.EntryDirection?.Value,
        expedition.Traversal.LastTravelDirection?.Value,
        expedition.IntendedDirection?.Value,
        expedition.ActualDirection?.Value,
        expedition.Navigation.IsLost,
        expedition.Navigation.VeerSteps,
        expedition.Navigation.VeerDegrees,
        DistanceContract.From(expedition.DistanceTraveled),
        DistanceContract.From(expedition.Traversal.Progress),
        expedition.Traversal.CurrentExitRequirement is { } requirement ? DistanceContract.From(requirement) : null,
        expedition.ElapsedTravelTime.TotalHours,
        (int)Math.Floor(expedition.ElapsedTravelTime.TotalDays) + 1,
        expedition.CompletedWatches,
        expedition.ActiveWatch?.WatchNumber,
        expedition.ActiveWatch?.TotalDuration.TotalHours,
        expedition.ActiveWatch?.Elapsed.TotalHours,
        expedition.ActiveWatch?.Remaining.TotalHours,
        expedition.ActiveWatch?.PendingDecision,
        expedition.ActiveWatch?.Plan.Mode.PaceKey,
        expedition.ActiveWatch?.Plan.Mode.Activities ?? [],
        expedition.ActiveWatch?.Plan.NavigationAid.Key,
        expedition.ActiveWatch?.Plan.DeliberateDoubleBack ?? false,
        expedition.ActiveWatch?.Plan.ContinueAcrossBoundaries ?? false,
        expedition.ActiveWatch?.Encounter.Kind,
        expedition.ActiveWatch?.Encounter.OccursAt?.TotalHours,
        expedition.ActiveWatch?.EncounterHandled);
}

public sealed record CrawlContextContract(
    Guid Id,
    string Name,
    DistanceContract HexCenterDistance)
{
    public static CrawlContextContract From(OverworldDefinition world) => new(
        world.Id,
        world.Name,
        DistanceContract.From(world.Grid.NeighborCenterDistance));
}

public sealed record ExpeditionWorkbenchContract(
    Guid Id,
    Guid OverworldId,
    CrawlContextContract Context,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RuntimeProfileContract Profile,
    PresentationProfileContract Presentation,
    RuntimePauseReason? PauseReason,
    double RemainingWatchHours,
    WorkbenchExpeditionStateContract Expedition,
    IReadOnlyList<HexCoordinate> KnownHexes,
    IReadOnlyList<KnowledgeEntryContract> Knowledge,
    IReadOnlyList<RuntimeEventContract> History)
{
    public static ExpeditionWorkbenchContract From(StoredExpedition expedition, OverworldDefinition world)
    {
        var presentation = expedition.Knowledge.PresentationPolicy ?? MapPresentationPolicy.DmControlled();
        return new ExpeditionWorkbenchContract(
            expedition.State.Id,
            expedition.State.OverworldId,
            CrawlContextContract.From(world),
            expedition.Name,
            expedition.Version,
            expedition.CreatedAt,
            expedition.UpdatedAt,
            RuntimeProfileContract.From(expedition.Procedure),
            PresentationProfileContract.From(presentation),
            expedition.PauseReason,
            expedition.RemainingWatchTime.TotalHours,
            WorkbenchExpeditionStateContract.From(expedition.State),
            expedition.Knowledge.KnownHexes,
            expedition.Knowledge.Entries.Values
                .OrderBy(item => item.SubjectType)
                .ThenBy(item => item.SubjectId)
                .Select(KnowledgeEntryContract.From)
                .ToArray(),
            expedition.State.History.Select(RuntimeEventContract.From).ToArray());
    }
}

public sealed record StartExpeditionWorkbenchRequest(
    string Name,
    string ProcedureKey,
    HexCoordinate StartHex,
    string? PresentationKey = null,
    RuntimeProfileContract? ProcedureSnapshot = null)
{
    public StartExpeditionWorkbenchCommand ToCommand() => new(
        Name,
        ProcedureKey,
        string.IsNullOrWhiteSpace(PresentationKey) ? "exploration-map" : PresentationKey.Trim(),
        StartHex,
        ProcedureSnapshot is null ? null : ToProcedure(ProcedureSnapshot));

    private static CrawlProcedureProfile ToProcedure(RuntimeProfileContract profile) => new()
    {
        Key = profile.Key,
        Name = profile.Name,
        WatchLength = TimeSpan.FromHours(profile.WatchHours),
        TravelResolution = profile.TravelResolution,
        ActualDistanceResolution = profile.ActualDistanceResolution,
        EncounterCadence = profile.EncounterCadence,
        UsesNavigationChecks = profile.UsesNavigationChecks,
        UsesPersistentVeer = profile.UsesPersistentVeer,
        TracksIntraHexProgress = profile.TracksIntraHexProgress,
        DirectionChangesCostProgress = profile.DirectionChangesCostProgress,
        SupportsDeliberateDoubleBack = profile.SupportsDeliberateDoubleBack,
        StartingExitProgressFactor = profile.StartingExitProgressFactor,
        NearExitProgressFactor = profile.NearExitProgressFactor,
        FarExitProgressFactor = profile.FarExitProgressFactor,
        BackExitProgressFactor = profile.BackExitProgressFactor,
        DirectionChangeProgressCostFactor = profile.DirectionChangeProgressCostFactor
    };
}

public sealed record AdvanceExpeditionWorkbenchRequest
{
    public long ExpectedVersion { get; init; }
    public int IntendedDirection { get; init; }
    public string PaceKey { get; init; } = "normal";
    public IReadOnlyList<string> Activities { get; init; } = [];
    public string NavigationAidKey { get; init; } = "none";
    public bool SuppressesNavigationCheck { get; init; }
    public bool ResetsVeerAtBoundary { get; init; }
    public double? EffectiveDistance { get; init; }
    public double? ExpectedDistance { get; init; }
    public double? ActualDistance { get; init; }
    public int? HexSteps { get; init; }
    public ResolutionSource ResolutionSource { get; init; } = ResolutionSource.ManualRoll;
    public ResolutionSource? TravelResolutionSource { get; init; }
    public string? TravelResolutionNote { get; init; }
    public ResolutionSource? NavigationResolutionSource { get; init; }
    public string? NavigationResolutionNote { get; init; }
    public ResolutionSource? EncounterResolutionSource { get; init; }
    public string? EncounterResolutionNote { get; init; }
    public ResolutionSource? BoundaryResolutionSource { get; init; }
    public string? BoundaryResolutionNote { get; init; }
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

    public AdvanceExpeditionWorkbenchCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        IntendedDirection = IntendedDirection,
        PaceKey = PaceKey,
        Activities = Activities,
        NavigationAidKey = NavigationAidKey,
        SuppressesNavigationCheck = SuppressesNavigationCheck,
        ResetsVeerAtBoundary = ResetsVeerAtBoundary,
        EffectiveDistance = EffectiveDistance,
        ExpectedDistance = ExpectedDistance,
        ActualDistance = ActualDistance,
        HexSteps = HexSteps,
        ResolutionSource = ResolutionSource,
        TravelResolutionSource = TravelResolutionSource,
        TravelResolutionNote = TravelResolutionNote,
        NavigationResolutionSource = NavigationResolutionSource,
        NavigationResolutionNote = NavigationResolutionNote,
        EncounterResolutionSource = EncounterResolutionSource,
        EncounterResolutionNote = EncounterResolutionNote,
        BoundaryResolutionSource = BoundaryResolutionSource,
        BoundaryResolutionNote = BoundaryResolutionNote,
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


public sealed record TravelWatchAssistantRequest(
    long ExpectedVersion,
    double ElapsedHours,
    double? Distance,
    int? HexSteps,
    HexCoordinate ResultingHex,
    double? HexProgress,
    int? IntendedDirection,
    int? ActualDirection,
    bool CompleteWatch,
    ResolutionSource ResolutionSource = ResolutionSource.ManualRoll,
    string? ResolutionNote = null,
    string? Note = null)
{
    public TravelWatchAssistantCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        ElapsedHours = ElapsedHours,
        Distance = Distance,
        HexSteps = HexSteps,
        ResultingHex = ResultingHex,
        HexProgress = HexProgress,
        IntendedDirection = IntendedDirection,
        ActualDirection = ActualDirection,
        CompleteWatch = CompleteWatch,
        ResolutionSource = ResolutionSource,
        ResolutionNote = ResolutionNote,
        Note = Note
    };
}

public sealed record NavigationAssistantRequest(
    long ExpectedVersion,
    bool IsLost,
    int VeerSteps,
    int? IntendedDirection,
    ResolutionSource ResolutionSource = ResolutionSource.ManualRoll,
    string? ResolutionNote = null,
    string? Note = null)
{
    public NavigationAssistantCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        IsLost = IsLost,
        VeerSteps = VeerSteps,
        IntendedDirection = IntendedDirection,
        ResolutionSource = ResolutionSource,
        ResolutionNote = ResolutionNote,
        Note = Note
    };
}

public sealed record EncounterCadenceAssistantRequest(
    long ExpectedVersion,
    EncounterOutcomeKind Outcome,
    ResolutionSource ResolutionSource = ResolutionSource.ManualRoll,
    string? ResolutionNote = null,
    string? Note = null)
{
    public EncounterCadenceAssistantCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        Outcome = Outcome,
        ResolutionSource = ResolutionSource,
        ResolutionNote = ResolutionNote,
        Note = Note
    };
}
