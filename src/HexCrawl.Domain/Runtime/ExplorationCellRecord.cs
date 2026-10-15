using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// DM-owned, expedition-local knowledge about one authoritative periodic cell.
/// A visit is not a generated location, and neither is automatically exposed
/// through the independent player map knowledge projection.
/// </summary>
public sealed record ExplorationCellRecord
{
    public required WorldCellId Cell { get; init; }
    public bool Visited { get; init; }
    public bool RestSite { get; init; }
    public string? LocationName { get; init; }
    public string? DmNote { get; init; }
    public Guid LastOperationId { get; init; }
    public ExpeditionConsequenceProvenance? Provenance { get; init; }

    public void Validate()
    {
        if (Cell.TilingId == Guid.Empty)
            throw new InvalidOperationException("Exploration cell requires a qualified tiling identity.");
        if (LastOperationId == Guid.Empty)
            throw new InvalidOperationException("Exploration cell requires a nonempty operation id.");
        ExpeditionConsequenceProvenance.ValidateOptional(LocationName, 300, "Exploration location name");
        ExpeditionConsequenceProvenance.ValidateOptional(DmNote, 4000, "Exploration DM note");
        Provenance?.Validate();
    }
}
