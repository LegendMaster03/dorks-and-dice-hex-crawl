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
    bool MarkEnteredCellKnown,
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
        policy.MarkEnteredCellKnown,
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
    Guid? LocationId,
    string? Note)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorldCellId? Cell { get; init; }

    public static PendingEncounterContract? From(PendingEncounterOccurrence? occurrence) =>
        occurrence is null ? null : new PendingEncounterContract(
            occurrence.Id, occurrence.TriggerSequence, occurrence.WatchNumber, occurrence.Outcome,
            occurrence.ExpeditionElapsedTime.TotalHours, occurrence.LocationId, occurrence.Note)
        { Cell = occurrence.Cell };
}

public sealed record WorkbenchExpeditionStateContract(
    Guid Id,
    bool IsSpatial,
    WorldPoint? Position,
    WorldPositionPrecision? PositionPrecision,
    bool? IsLost,
    double? VeerDegrees,
    DistanceContract? DistanceTraveled,
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
    // Qualified-cell fields are present for spatial expeditions only.
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
        CellExpeditionState cells => FromCells(cells),
        NonSpatialSessionState nonSpatial => new(
            Id: nonSpatial.Id,
            IsSpatial: false,
            Position: null,
            PositionPrecision: null,
            IsLost: null,
            VeerDegrees: null,
            DistanceTraveled: null,
            ElapsedTravelHours: nonSpatial.ElapsedTime.TotalHours,
            CurrentDay: (int)Math.Floor(nonSpatial.ElapsedTime.TotalDays) + 1,
            CompletedWatches: nonSpatial.CompletedWatches,
            ActiveWatchNumber: nonSpatial.ActiveWatch?.WatchNumber,
            ActiveWatchTotalHours: nonSpatial.ActiveWatch?.TotalDuration.TotalHours,
            ActiveWatchElapsedHours: nonSpatial.ActiveWatch?.Elapsed.TotalHours,
            ActiveWatchRemainingHours: nonSpatial.ActiveWatch?.Remaining.TotalHours,
            ActiveWatchPendingDecision: null,
            ActivePaceKey: null,
            ActiveActivityAssignments: nonSpatial.ActiveWatch?.ActivityAssignments
                .Select(ParticipantActivityAssignmentContract.From).ToArray() ?? [],
            ActiveNavigationAidKey: null,
            ActiveSuppressesNavigationCheck: false,
            ActiveResetsVeerAtBoundary: false,
            ActiveDeliberateDoubleBack: false,
            ActiveContinueAcrossBoundaries: false,
            ActiveEncounterKind: null,
            ActiveEncounterHour: null,
            ActiveEncounterHandled: null,
            PendingEncounter: PendingEncounterContract.From(nonSpatial.PendingEncounter)),
        _ => throw new ArgumentOutOfRangeException(nameof(runtime))
    };
    private static WorkbenchExpeditionStateContract FromCells(CellExpeditionState cells) => new(
        Id: cells.Id,
        IsSpatial: true,
        Position: cells.Traversal.Position,
        PositionPrecision: WorldPositionPrecision.Exact,
        IsLost: cells.IsLost,
        VeerDegrees: cells.ResolvedVeerDegrees,
        DistanceTraveled: cells.DistanceTraveled is { } distance ? DistanceContract.From(distance) : null,
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
    PeriodicWorldTiling? Tiling)
{
    public static CrawlContextContract From(CrawlSessionContext context, OverworldDefinition? world = null) => context switch
    {
        WorldBoundCrawlSessionContext worldContext => new(
            context.Kind, world?.Name ?? "World-bound crawl",
            worldContext.WorldId, world?.SpatialTiling),
        MaplessSpatialCrawlSessionContext mapless => new(
            context.Kind, mapless.DisplayName, null, mapless.Tiling),
        NonSpatialCrawlSessionContext nonSpatial => new(
            context.Kind, nonSpatial.DisplayName, null, null),
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
    double? PhysicalDistancePerWorldUnit = null,
    DistanceUnitContract? DistanceUnit = null)
{
    public NonSpatialCrawlSessionContext ToNonSpatial() =>
        Kind == CrawlSessionContextKind.NonSpatial
            ? new NonSpatialCrawlSessionContext(
                string.IsNullOrWhiteSpace(Name) ? "Non-spatial session" : Name.Trim())
            : throw new ArgumentException("This endpoint starts nonspatial sessions only.");

    public MaplessSpatialStartOptions ToMaplessOptions()
    {
        if (Kind != CrawlSessionContextKind.MaplessSpatial)
            throw new ArgumentException("This endpoint starts mapless spatial sessions only.");
        var name = string.IsNullOrWhiteSpace(Name) ? "Mapless exploration" : Name.Trim();
        if (PhysicalDistancePerWorldUnit is null && DistanceUnit is null)
            return new MaplessSpatialStartOptions(name);
        if (PhysicalDistancePerWorldUnit is not { } distance
            || !double.IsFinite(distance) || distance <= 0)
            throw new ArgumentException("Mapless physical distance per world unit must be finite and positive.");
        var units = (DistanceUnit ?? throw new ArgumentException(
            "Mapless spatial scale requires an explicit physical unit.")).ToDomain();
        return new MaplessSpatialStartOptions(name, new DistanceMeasure(distance, units));
    }
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
