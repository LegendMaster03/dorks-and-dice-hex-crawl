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
    public string? RegionKey { get; init; }
    public IReadOnlyList<ExplorationTableResult> TableResults { get; init; } = [];
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
        if (RegionKey is not null) ExplorationTableResult.RequireKey(RegionKey, "Exploration region key", 120);
        ExplorationTableResult.ValidateCollection(TableResults);
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
    public string? RegionKey { get; init; }
    public string? RegionName { get; init; }
    public string? RegionDmNote { get; init; }
    public string? TableFingerprint { get; init; }

    public void Validate()
    {
        foreach (var fingerprint in new[] { ConsequenceFingerprint, TableFingerprint })
        {
            if (fingerprint is not null &&
                (fingerprint.Length != 64 || fingerprint.Any(value => !char.IsAsciiHexDigit(value))))
                throw new InvalidOperationException("Exploration submission fingerprint is malformed.");
        }
        if (RegionKey is not null) ExplorationTableResult.RequireKey(RegionKey, "Region key", 120);
        ExpeditionConsequenceProvenance.ValidateOptional(RegionName, 300, "Region name");
        ExpeditionConsequenceProvenance.ValidateOptional(RegionDmNote, 4000, "Region DM note");
        if (OperationId == Guid.Empty || Cell.TilingId == Guid.Empty
            || ProcessId == Guid.Empty)
            throw new InvalidOperationException("Exploration operation identifiers must be valid.");
        ExpeditionConsequenceProvenance.ValidateOptional(ActionKey, 200, "Exploration action key");
        ExpeditionConsequenceProvenance.ValidateOptional(LocationName, 300, "Exploration location name");
        ExpeditionConsequenceProvenance.ValidateOptional(DmNote, 4000, "Exploration DM note");
        Provenance.Validate();
    }
}


/// <summary>
/// A DM-confirmed, source-attributed table outcome. No dice are rolled and no
/// sourcebook table content is bundled or inferred by this record.
/// </summary>
public sealed record ExplorationTableResult(
    string TableKey,
    string ResultText,
    string? Roll = null,
    string? SourceReference = null)
{
    public void Validate()
    {
        RequireKey(TableKey, "Exploration table key", 120);
        RequireKey(ResultText, "Exploration table result", 2000);
        ExpeditionConsequenceProvenance.ValidateOptional(Roll, 120, "Resolved table roll");
        ExpeditionConsequenceProvenance.ValidateOptional(SourceReference, 500, "Table source reference");
    }

    public static void RequireKey(string? key, string label, int limit)
    {
        if (string.IsNullOrWhiteSpace(key) || key != key.Trim() || key.Length > limit)
            throw new InvalidOperationException($"{label} must be a nonempty, trimmed value within {limit} characters.");
    }

    public static void ValidateCollection(IReadOnlyList<ExplorationTableResult> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            if (entry is null) throw new InvalidOperationException("Exploration table results cannot be null.");
            entry.Validate();
        }
        if (entries.Select(value => value.TableKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidOperationException("Each exploration table can have only one current result per subject.");
    }
}

/// <summary>
/// Expedition-local GM grouping for region-generation results. Cell assignments
/// reference keys without assuming hex/square geometry or copying world regions.
/// </summary>
public sealed record ExplorationRegionRecord
{
    public required string Key { get; init; }
    public string? Name { get; init; }
    public string? DmNote { get; init; }
    public IReadOnlyList<ExplorationTableResult> TableResults { get; init; } = [];
    public required Guid LastOperationId { get; init; }
    public ExpeditionConsequenceProvenance? Provenance { get; init; }

    public void Validate()
    {
        ExplorationTableResult.RequireKey(Key, "Exploration region key", 120);
        if (LastOperationId == Guid.Empty)
            throw new InvalidOperationException("Region record requires a valid operation.");
        ExpeditionConsequenceProvenance.ValidateOptional(Name, 300, "Exploration region name");
        ExpeditionConsequenceProvenance.ValidateOptional(DmNote, 4000, "Exploration region DM note");
        ExplorationTableResult.ValidateCollection(TableResults);
        Provenance?.Validate();
    }
}
