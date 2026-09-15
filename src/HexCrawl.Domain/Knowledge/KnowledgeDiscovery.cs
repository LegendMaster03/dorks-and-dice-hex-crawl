namespace HexCrawl.Domain.Knowledge;

public static class KnowledgeDiscovery
{
    public static PlayerKnowledgeState Discover(
        PlayerKnowledgeState knowledge,
        Guid subjectId,
        KnowledgeSubjectType subjectType,
        string source,
        DateTimeOffset? learnedAt = null)
    {
        var entries = new Dictionary<Guid, KnowledgeEntry>(knowledge.Entries)
        {
            [subjectId] = new KnowledgeEntry(
                subjectId,
                subjectType,
                KnowledgeState.Discovered,
                learnedAt,
                source)
        };

        return knowledge with { Entries = entries };
    }
}
