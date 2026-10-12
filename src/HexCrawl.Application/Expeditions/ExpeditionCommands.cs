using HexCrawl.Domain.Knowledge;

namespace HexCrawl.Application;

/// <summary>
/// Semantic world discovery through a qualified-cell expedition.
/// </summary>
public sealed record DiscoverSubjectCommand(
    long ExpectedVersion,
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    string? Source);
