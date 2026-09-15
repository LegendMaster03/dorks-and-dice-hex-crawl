using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Knowledge;

public enum KnowledgeSubjectType
{
    Feature,
    Location,
    Terrain,
    Route
}

public enum KnowledgeState
{
    Observed,
    Discovered,
    Revealed
}

public sealed record KnowledgeEntry(
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    KnowledgeState State,
    DateTimeOffset? LearnedAt,
    string? Source);

public sealed record PlayerAnnotation(Guid Id, WorldPoint Position, string Text, DateTimeOffset CreatedAt);

public sealed record PlayerKnowledgeState
{
    public required Guid ScopeId { get; init; }
    public required Guid OverworldId { get; init; }
    public IReadOnlyDictionary<Guid, KnowledgeEntry> Entries { get; init; } = new Dictionary<Guid, KnowledgeEntry>();
    public IReadOnlyList<PlayerAnnotation> Annotations { get; init; } = [];
}
