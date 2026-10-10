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

    public static PlayerKnowledgeState KnowCell(PlayerKnowledgeState knowledge, WorldCellId cell)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        if (cell.TilingId == Guid.Empty)
            throw new InvalidOperationException("Known cell requires a real tiling identity.");
        var known = knowledge.KnownCells ?? [];
        return known.Contains(cell)
            ? knowledge
            : knowledge with { KnownCells = [.. known, cell] };
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
