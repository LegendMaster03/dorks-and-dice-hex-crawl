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

 
/// <summary>
/// Immutable connection between an explicitly recorded action and the shared
/// journey process. A pending event is resolved through the existing event
/// engine, never through an exploration-specific encounter subsystem.
/// </summary>
public sealed record ExplorationStageActionRecord(
    Guid OperationId,
    Guid ProcessId,
    string StageKey,
    string ActionKey,
    WorldCellId Cell,
    Guid? EncounterOccurrenceId)
{
    public void Validate()
    {
        if (OperationId == Guid.Empty || ProcessId == Guid.Empty || Cell.TilingId == Guid.Empty
            || string.IsNullOrWhiteSpace(StageKey) || string.IsNullOrWhiteSpace(ActionKey)
            || EncounterOccurrenceId == Guid.Empty)
            throw new InvalidOperationException("An exploration process action must have valid identifiers and keys.");
    }
}

/// <summary>
/// A retained exact submission allows safe idempotent replay even after the
/// active cell changes or later edits replace its current location details.
/// </summary>
public sealed record ExplorationOperationRecord(
    Guid OperationId,
    WorldCellId Cell,
    string? ActionKey,
    bool MarkVisited,
    bool MarkRestSite,
    string? LocationName,
    string? DmNote,
    bool? EncounterOccurs,
    Guid? ProcessId,
    ExpeditionConsequenceProvenance Provenance)
{
    public string? ConsequenceFingerprint { get; init; }

    public void Validate()
    {
        if (ConsequenceFingerprint is not null &&
            (ConsequenceFingerprint.Length != 64 ||
             ConsequenceFingerprint.Any(value => !char.IsAsciiHexDigit(value))))
            throw new InvalidOperationException("Exploration consequence fingerprint is malformed.");
        if (OperationId == Guid.Empty || Cell.TilingId == Guid.Empty
            || ProcessId == Guid.Empty)
            throw new InvalidOperationException("Exploration operation identifiers must be valid.");
        ExpeditionConsequenceProvenance.ValidateOptional(ActionKey, 200, "Exploration action key");
        ExpeditionConsequenceProvenance.ValidateOptional(LocationName, 300, "Exploration location name");
        ExpeditionConsequenceProvenance.ValidateOptional(DmNote, 4000, "Exploration DM note");
        Provenance.Validate();
    }
}
