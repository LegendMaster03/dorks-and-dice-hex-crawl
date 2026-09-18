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
    bool IsSpatial,
    HexCoordinate? CurrentHex,
    WorldPoint? Position,
    WorldPositionPrecision? PositionPrecision,
    int? EntryDirection,
    int? LastTravelDirection,
    int? IntendedDirection,
    int? ActualDirection,
    bool? IsLost,
    int? VeerSteps,
    double? VeerDegrees,
    DistanceContract? DistanceTraveled,
    DistanceContract? HexProgress,
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
    public static WorkbenchExpeditionStateContract From(CrawlSessionRuntimeState runtime) => runtime switch
    {
        ExpeditionState expedition => new(
            expedition.Id,
            true,
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
            expedition.ActiveWatch?.EncounterHandled),
        NonSpatialSessionState nonSpatial => new(
            nonSpatial.Id,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            nonSpatial.ElapsedTime.TotalHours,
            (int)Math.Floor(nonSpatial.ElapsedTime.TotalDays) + 1,
            nonSpatial.CompletedWatches,
            nonSpatial.ActiveWatch?.WatchNumber,
            nonSpatial.ActiveWatch?.TotalDuration.TotalHours,
            nonSpatial.ActiveWatch?.Elapsed.TotalHours,
            nonSpatial.ActiveWatch?.Remaining.TotalHours,
            null,
            null,
            [],
            null,
            false,
            false,
            null,
            null,
            null),
        _ => throw new ArgumentOutOfRangeException(nameof(runtime))
    };
}

public sealed record CrawlContextContract(
    CrawlSessionContextKind Kind,
    string Name,
    Guid? OverworldId,
    HexOrientation? Orientation,
    DistanceContract? HexCenterDistance)
{
    public static CrawlContextContract From(CrawlSessionContext context, OverworldDefinition? world = null) => context switch
    {
        WorldBoundCrawlSessionContext worldContext => new(
            context.Kind,
            world?.Name ?? "World-bound crawl",
            worldContext.WorldId,
            world?.Grid.Orientation,
            world is null ? null : DistanceContract.From(world.Grid.NeighborCenterDistance)),
        AbstractHexCrawlSessionContext abstractContext => new(
            context.Kind,
            abstractContext.DisplayName,
            null,
            abstractContext.Orientation,
            DistanceContract.From(abstractContext.HexContext.HexCenterDistance)),
        NonSpatialCrawlSessionContext nonSpatial => new(
            context.Kind,
            nonSpatial.DisplayName,
            null,
            null,
            null),
        _ => throw new ArgumentOutOfRangeException(nameof(context))
    };
}

public sealed record ExpeditionSummaryContract(
    Guid Id,
    CrawlContextContract Context,
    string Name,
    string ProcedureName,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ExpeditionSummaryContract From(ExpeditionSummary summary) => new(
        summary.Id,
        CrawlContextContract.From(summary.Context),
        summary.Name,
        summary.ProcedureName,
        summary.Version,
        summary.CreatedAt,
        summary.UpdatedAt);
}

public sealed record ExpeditionWorkbenchContract(
    Guid Id,
    Guid? OverworldId,
    CrawlContextContract Context,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    RuntimeProfileContract Profile,
    PresentationProfileContract? Presentation,
    RuntimePauseReason? PauseReason,
    double RemainingWatchHours,
    WorkbenchExpeditionStateContract Expedition,
    IReadOnlyList<HexCoordinate> KnownHexes,
    IReadOnlyList<KnowledgeEntryContract> Knowledge,
    IReadOnlyList<RuntimeEventContract> History)
{
    public static ExpeditionWorkbenchContract From(StoredExpedition expedition, OverworldDefinition? world = null)
    {
        var presentation = expedition.Knowledge?.PresentationPolicy;
        if (expedition.Context is WorldBoundCrawlSessionContext && presentation is null)
        {
            presentation = MapPresentationPolicy.DmControlled();
        }

        return new ExpeditionWorkbenchContract(
            expedition.Id,
            expedition.Context.OverworldId,
            CrawlContextContract.From(expedition.Context, world),
            expedition.Name,
            expedition.Version,
            expedition.CreatedAt,
            expedition.UpdatedAt,
            RuntimeProfileContract.From(expedition.Procedure),
            presentation is null ? null : PresentationProfileContract.From(presentation),
            expedition.PauseReason,
            expedition.RemainingWatchTime.TotalHours,
            WorkbenchExpeditionStateContract.From(expedition.Runtime),
            expedition.Knowledge?.KnownHexes ?? [],
            expedition.Knowledge?.Entries.Values
                .OrderBy(item => item.SubjectType)
                .ThenBy(item => item.SubjectId)
                .Select(KnowledgeEntryContract.From)
                .ToArray() ?? [],
            expedition.Runtime.History.Select(RuntimeEventContract.From).ToArray());
    }
}

public sealed record StandaloneCrawlContextRequest(
    CrawlSessionContextKind Kind,
    string? Name = null,
    HexOrientation? Orientation = null,
    double? HexCenterDistance = null,
    DistanceUnitContract? DistanceUnit = null)
{
    public CrawlSessionContext ToDomain() => Kind switch
    {
        CrawlSessionContextKind.AbstractHex => new AbstractHexCrawlSessionContext(
            string.IsNullOrWhiteSpace(Name) ? "Abstract hex crawl" : Name.Trim(),
            Orientation ?? HexOrientation.PointyTop,
            new CrawlRuntimeContext(new DistanceMeasure(
                HexCenterDistance is > 0 and < double.PositiveInfinity
                    ? HexCenterDistance.Value
                    : throw new ArgumentException("Abstract-hex context requires a positive finite hex-center distance."),
                DistanceUnit?.ToDomain() ?? HexCrawl.Domain.Spatial.DistanceUnit.Miles))),
        CrawlSessionContextKind.NonSpatial => new NonSpatialCrawlSessionContext(
            string.IsNullOrWhiteSpace(Name) ? "Non-spatial session" : Name.Trim()),
        CrawlSessionContextKind.WorldBound => throw new ArgumentException(
            "World-bound sessions must be started through an overworld endpoint."),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
}

public sealed record StartStandaloneCrawlSessionRequest(
    string Name,
    string ProcedureKey,
    StandaloneCrawlContextRequest Context,
    HexCoordinate? StartHex = null,
    RuntimeProfileContract? ProcedureSnapshot = null)
{
    public StartStandaloneCrawlSessionCommand ToCommand() => new(
        Name,
        ProcedureKey,
        Context.ToDomain(),
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
        DirectionChangeProgressCostFactor = profile.DirectionChangeProgressCostFactor,
        ResolutionHelpers = profile.ResolutionHelpers?.ToDomain()
    };
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
        DirectionChangeProgressCostFactor = profile.DirectionChangeProgressCostFactor,
        ResolutionHelpers = profile.ResolutionHelpers?.ToDomain()
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
    public Guid? GeneratedProcedureResolutionId { get; init; }

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
        DmOverrideNote = DmOverrideNote,
        GeneratedProcedureResolutionId = GeneratedProcedureResolutionId
    };
}


public sealed record ResolveProcedureInputsRequest
{
    public long ExpectedVersion { get; init; }
    public double? ExpectedDistance { get; init; }
    public bool SuppressesNavigationCheck { get; init; }
    public bool DeliberateDoubleBack { get; init; }
    public int? NavigationDifficultyClass { get; init; }
    public int NavigationModifier { get; init; }
    public int? FailureVeerSteps { get; init; }
    public Guid? KeyedLocationId { get; init; }

    public ProcedureResolutionHelperCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        ExpectedDistance = ExpectedDistance,
        SuppressesNavigationCheck = SuppressesNavigationCheck,
        DeliberateDoubleBack = DeliberateDoubleBack,
        NavigationDifficultyClass = NavigationDifficultyClass,
        NavigationModifier = NavigationModifier,
        FailureVeerSteps = FailureVeerSteps,
        KeyedLocationId = KeyedLocationId
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

public sealed record NonSpatialWatchAssistantRequest(
    long ExpectedVersion,
    double ElapsedHours,
    ResolutionSource ResolutionSource = ResolutionSource.ProcedureDefault,
    string? ResolutionNote = null,
    string? Note = null)
{
    public NonSpatialWatchAssistantCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        ElapsedHours = ElapsedHours,
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
