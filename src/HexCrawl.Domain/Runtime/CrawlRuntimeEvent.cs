using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public enum CrawlRuntimeEventKind
{
    WatchStarted,
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
    ResolutionProvenanceRecorded
}

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
    KnowledgeSubjectType? SubjectType = null);

public sealed record WatchAdvanceResult(
    ExpeditionState Expedition,
    RuntimePauseReason? PauseReason,
    TimeSpan RemainingWatchTime,
    IReadOnlyList<CrawlRuntimeEvent> Events);
