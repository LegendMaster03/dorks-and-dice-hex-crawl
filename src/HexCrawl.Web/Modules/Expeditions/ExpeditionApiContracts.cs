using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
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

public sealed record PendingEncounterContract(
    Guid Id,
    long TriggerSequence,
    int WatchNumber,
    EncounterOutcomeKind Outcome,
    double ExpeditionElapsedHours,
    HexCoordinate? Hex,
    Guid? LocationId,
    string? Note)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorldCellId? Cell { get; init; }

    public static PendingEncounterContract? From(PendingEncounterOccurrence? occurrence) =>
        occurrence is null ? null : new PendingEncounterContract(
            occurrence.Id, occurrence.TriggerSequence, occurrence.WatchNumber, occurrence.Outcome,
            occurrence.ExpeditionElapsedTime.TotalHours, occurrence.Hex, occurrence.LocationId, occurrence.Note)
        { Cell = occurrence.Cell };
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
    IReadOnlyList<ParticipantActivityAssignmentContract> ActiveActivityAssignments,
    string? ActiveNavigationAidKey,
    bool ActiveSuppressesNavigationCheck,
    bool ActiveResetsVeerAtBoundary,
    bool ActiveDeliberateDoubleBack,
    bool ActiveContinueAcrossBoundaries,
    EncounterOutcomeKind? ActiveEncounterKind,
    double? ActiveEncounterHour,
    bool? ActiveEncounterHandled,
    PendingEncounterContract? PendingEncounter)
{
    // Additive qualified-cell authority. These fields are absent from legacy
    // hex and nonspatial JSON rather than populated with invented coordinates.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorldCellId? CurrentCell { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PeriodicCellTraversal? CellTraversal { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorldPoint? IntendedHeading { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorldPoint? ActualHeading { get; init; }

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
            expedition.ActiveWatch?.Plan.Mode.ActivityAssignments
                .Select(ParticipantActivityAssignmentContract.From)
                .ToArray() ?? [],
            expedition.ActiveWatch?.Plan.NavigationAid.Key,
            expedition.ActiveWatch?.Plan.NavigationAid.SuppressesNavigationCheck ?? false,
            expedition.ActiveWatch?.Plan.NavigationAid.ResetsVeerAtBoundary ?? false,
            expedition.ActiveWatch?.Plan.DeliberateDoubleBack ?? false,
            expedition.ActiveWatch?.Plan.ContinueAcrossBoundaries ?? false,
            expedition.ActiveWatch?.Encounter.Kind,
            expedition.ActiveWatch?.Encounter.OccursAt?.TotalHours,
            expedition.ActiveWatch?.EncounterHandled,
            PendingEncounterContract.From(expedition.PendingEncounter)),
        CellExpeditionState cells => FromCells(cells),
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
            nonSpatial.ActiveWatch?.ActivityAssignments
                .Select(ParticipantActivityAssignmentContract.From)
                .ToArray() ?? [],
            null,
            false,
            false,
            false,
            false,
            null,
            null,
            null,
            PendingEncounterContract.From(nonSpatial.PendingEncounter)),
        _ => throw new ArgumentOutOfRangeException(nameof(runtime))
    };
    private static WorkbenchExpeditionStateContract FromCells(CellExpeditionState cells) => new(
        Id: cells.Id,
        IsSpatial: true,
        CurrentHex: null,
        Position: cells.Traversal.Position,
        PositionPrecision: WorldPositionPrecision.Exact,
        EntryDirection: null,
        LastTravelDirection: null,
        IntendedDirection: null,
        ActualDirection: null,
        IsLost: cells.IsLost,
        VeerSteps: null,
        VeerDegrees: cells.ResolvedVeerDegrees,
        DistanceTraveled: cells.DistanceTraveled is { } distance ? DistanceContract.From(distance) : null,
        HexProgress: null,
        ExitRequirement: null,
        ElapsedTravelHours: cells.ElapsedTravelTime.TotalHours,
        CurrentDay: (int)Math.Floor(cells.ElapsedTravelTime.TotalDays) + 1,
        CompletedWatches: cells.CompletedWatches,
        ActiveWatchNumber: cells.ActiveWatch?.WatchNumber,
        ActiveWatchTotalHours: cells.ActiveWatch?.TotalDuration.TotalHours,
        ActiveWatchElapsedHours: cells.ActiveWatch?.Elapsed.TotalHours,
        ActiveWatchRemainingHours: cells.ActiveWatch?.Remaining.TotalHours,
        ActiveWatchPendingDecision: cells.ActiveWatch?.PendingDecision,
        ActivePaceKey: cells.ActiveWatch?.Plan.Mode.PaceKey,
        ActiveActivityAssignments: cells.ActiveWatch?.Plan.Mode.ActivityAssignments
            .Select(ParticipantActivityAssignmentContract.From).ToArray() ?? [],
        ActiveNavigationAidKey: cells.ActiveWatch?.Plan.NavigationAid.Key,
        ActiveSuppressesNavigationCheck: cells.ActiveWatch?.Plan.NavigationAid.SuppressesNavigationCheck ?? false,
        ActiveResetsVeerAtBoundary: cells.ActiveWatch?.Plan.NavigationAid.ResetsVeerAtBoundary ?? false,
        ActiveDeliberateDoubleBack: cells.ActiveWatch?.Plan.DeliberateDoubleBack ?? false,
        ActiveContinueAcrossBoundaries: cells.ActiveWatch?.Plan.ContinueAcrossBoundaries ?? false,
        ActiveEncounterKind: cells.ActiveWatch?.Encounter.Kind,
        ActiveEncounterHour: cells.ActiveWatch?.Encounter.OccursAt?.TotalHours,
        ActiveEncounterHandled: cells.ActiveWatch?.EncounterHandled,
        PendingEncounter: PendingEncounterContract.From(cells.PendingEncounter))
    {
        CurrentCell = cells.Traversal.CurrentCell,
        CellTraversal = cells.Traversal,
        IntendedHeading = cells.IntendedHeading,
        ActualHeading = cells.Traversal.TravelHeading
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
            world is { HasLegacyHexGrid: true } ? world.Grid.Orientation : null,
            world is { HasLegacyHexGrid: true }
                ? DistanceContract.From(world.Grid.NeighborCenterDistance) : null),
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
    CampaignProcedureContract Procedure,
    PresentationProfileContract? Presentation,
    RuntimePauseReason? PauseReason,
    double RemainingWatchHours,
    WorkbenchExpeditionStateContract Expedition,
    ExpeditionPartyContract Party,
    ParticipantActivityPolicyContract ParticipantActivityPolicy,
    MovementCapabilityCompositionContract MovementComposition,
    IReadOnlyList<HexCoordinate> KnownHexes,
    IReadOnlyList<KnowledgeEntryContract> Knowledge,
    IReadOnlyList<RuntimeEventContract> History)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WorldCellId>? KnownCells { get; init; }

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
            CampaignProcedureContract.From(expedition.CampaignProcedure),
            presentation is null ? null : PresentationProfileContract.From(presentation),
            expedition.PauseReason,
            expedition.RemainingWatchTime.TotalHours,
            WorkbenchExpeditionStateContract.From(expedition.Runtime),
            ExpeditionPartyContract.From(expedition.Party),
            ParticipantActivityPolicyContract.From(
                ParticipantActivityPolicyResolver.Resolve(expedition.CampaignProcedure)),
            MovementCapabilityCompositionContract.From(
                MovementCapabilityComposer.Compose(expedition)),
            expedition.Knowledge?.KnownHexes ?? [],
            expedition.Knowledge?.Entries.Values
                .OrderBy(item => item.SubjectType)
                .ThenBy(item => item.SubjectId)
                .Select(KnowledgeEntryContract.From)
                .ToArray() ?? [],
            expedition.Runtime.History.Select(RuntimeEventContract.From).ToArray())
        {
            KnownCells = expedition.Runtime is CellExpeditionState
                ? expedition.Knowledge?.KnownCells ?? []
                : null
        };
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
                (DistanceUnit ?? throw new ArgumentException(
                    "Abstract-hex context requires an explicit distance unit.")).ToDomain()))),
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
    HexCoordinate? StartHex = null)
{
    public StartStandaloneCrawlSessionCommand ToCommand() => new(
        Name,
        ProcedureKey,
        Context.ToDomain(),
        StartHex);
}

public sealed record StartExpeditionWorkbenchRequest(
    string Name,
    string ProcedureKey,
    HexCoordinate StartHex,
    string? PresentationKey = null)
{
    public StartExpeditionWorkbenchCommand ToCommand() => new(
        Name,
        ProcedureKey,
        string.IsNullOrWhiteSpace(PresentationKey) ? "exploration-map" : PresentationKey.Trim(),
        StartHex);
}

public sealed record AdvanceExpeditionWorkbenchRequest
{
    public long ExpectedVersion { get; init; }
    public int IntendedDirection { get; init; }
    public string PaceKey { get; init; } = "normal";
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

public sealed record ResolveEncounterRequest(
    long ExpectedVersion,
    ResolutionSource ResolutionSource = ResolutionSource.DmOverride,
    string? ResolutionNote = null,
    string? ResultNote = null)
{
    public ResolveEncounterCommand ToCommand(Guid occurrenceId) => new(
        ExpectedVersion, occurrenceId, ResolutionSource, ResolutionNote, ResultNote);
}

public sealed record ResolveBoundaryDecisionRequest(
    long ExpectedVersion,
    bool RecognizedLost,
    bool Reorient,
    ResolutionSource ResolutionSource = ResolutionSource.ManualRoll,
    string? ResolutionNote = null)
{
    public ResolveBoundaryDecisionWorkbenchCommand ToCommand() => new(
        ExpectedVersion,
        RecognizedLost,
        Reorient,
        ResolutionSource,
        ResolutionNote);
}

public sealed record SetExpeditionCourseIntentRequest(
    long ExpectedVersion,
    int? IntendedDirection)
{
    public SetExpeditionCourseIntentCommand ToCommand() => new(
        ExpectedVersion,
        IntendedDirection);
}

public sealed record RepositionExpeditionRequest(
    long ExpectedVersion,
    HexCoordinate TargetHex,
    string? Note = null)
{
    public RepositionExpeditionCommand ToCommand() => new(
        ExpectedVersion,
        TargetHex,
        Note);
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
