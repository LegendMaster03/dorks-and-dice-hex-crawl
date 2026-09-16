using HexCrawl.Domain.Spatial;

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

    public static PlayerKnowledgeState KnowHex(PlayerKnowledgeState knowledge, HexCoordinate hex)
    {
        if (knowledge.KnownHexes.Contains(hex))
        {
            return knowledge;
        }

        return knowledge with { KnownHexes = [.. knowledge.KnownHexes, hex] };
    }
}
