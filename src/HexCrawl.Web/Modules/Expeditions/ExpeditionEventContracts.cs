using HexCrawl.Application;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

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
    KnowledgeSubjectType? SubjectType,
    EncounterOutcomeKind? EncounterOutcome,
    string? EncounterNote,
    ResolutionProvenance? EncounterProvenance)
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
        runtimeEvent.SubjectType,
        runtimeEvent.EncounterOutcome,
        runtimeEvent.EncounterNote,
        runtimeEvent.EncounterProvenance);
}

public sealed record DiscoverSubjectRequest(
    long ExpectedVersion,
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    string? Source)
{
    public DiscoverSubjectCommand ToCommand() => new(ExpectedVersion, SubjectId, SubjectType, Source);
}
