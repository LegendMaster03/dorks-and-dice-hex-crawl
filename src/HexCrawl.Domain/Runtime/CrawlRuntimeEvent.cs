using System.Text.Json.Serialization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public enum CrawlRuntimeEventKind
{
    WatchStarted,
    WatchTimeAdvanced,
    WatchCompleted,
    NavigationCheckResolved,
    ExpeditionBecameLost,
    VeerChanged,
    VeerReset,
    ExpeditionReoriented,
    DirectionChanged,
    TravelResolved,
    DistanceTraveled,
    HexExited,
    HexEntered,
    EncounterCheckPerformed,
    EncounterTriggered,
    KeyedLocationEncountered,
    LocationDiscovered,
    FeatureDiscovered,
    NavigationDecisionRequired,
    ConditionsReviewRequired,
    DmOverrideApplied,
    ProcedureResolutionHelperGenerated,
    ProcedureResolutionHelperConsumed,
    ResolutionProvenanceRecorded,
    EncounterResolved,
    CellExited,
    CellEntered,
    CellCourseAdjudicationRequired
}

public sealed record CrawlRuntimeLinkedSceneSnapshot(
    Guid Id,
    string Kind,
    string ReferenceKey);

public sealed record CrawlRuntimeLocationSnapshot(
    Guid Id,
    string Name,
    string Category,
    IReadOnlyList<CrawlRuntimeLinkedSceneSnapshot> LinkedScenes);

public sealed record CrawlRuntimeEvent(
    long Sequence,
    int WatchNumber,
    CrawlRuntimeEventKind Kind,
    TimeSpan ExpeditionElapsedTime,
    HexCoordinate? Hex,
    string Message,
    double? DistanceValue = null,
    string? DistanceUnit = null,
    Guid? SubjectId = null,
    KnowledgeSubjectType? SubjectType = null,
    EncounterOutcomeKind? EncounterOutcome = null,
    string? EncounterNote = null,
    ResolutionProvenance? EncounterProvenance = null,
    CrawlRuntimeLocationSnapshot? EncounterLocation = null,
    Guid? EncounterOccurrenceId = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorldCellId? Cell { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BoundaryInterfaceIndex { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReciprocalInterfaceIndex { get; init; }
}

public sealed record WatchAdvanceResult(
    ExpeditionState Expedition,
    RuntimePauseReason? PauseReason,
    TimeSpan RemainingWatchTime,
    IReadOnlyList<CrawlRuntimeEvent> Events);
