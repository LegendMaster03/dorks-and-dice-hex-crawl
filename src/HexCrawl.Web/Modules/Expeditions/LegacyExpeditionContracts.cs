using HexCrawl.Application;
using HexCrawl.Domain.Knowledge;

namespace HexCrawl.Web.Api;

public sealed record DiscoverSubjectRequest(
    long ExpectedVersion,
    Guid SubjectId,
    KnowledgeSubjectType SubjectType,
    string? Source)
{
    public DiscoverSubjectCommand ToCommand() => new(ExpectedVersion, SubjectId, SubjectType, Source);
}
