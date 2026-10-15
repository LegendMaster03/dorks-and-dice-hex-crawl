namespace HexCrawl.Domain.Runtime;

/// <summary>
/// User-supplied, geometry-neutral table rows. The application must never
/// substitute publisher content, missing ranges, or source attribution.
/// </summary>
public enum ExplorationTableScope { Location, Region }

public sealed record ExplorationTableRow(int From, int Through, string ResultText)
{
    public void Validate(int dieSides)
    {
        if (From < 1 || Through < From || Through > dieSides)
            throw new InvalidOperationException("Exploration table ranges must be within the die and ordered.");
        ExplorationTableResult.RequireKey(ResultText, "Exploration table row result", 2000);
    }
}

public sealed record ExplorationTableRollRequest(
    ExplorationTableScope Scope,
    string TableKey,
    int DieSides,
    string SourceReference,
    IReadOnlyList<ExplorationTableRow> Rows)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Scope))
            throw new InvalidOperationException("Unknown exploration table scope.");
        ExplorationTableResult.RequireKey(TableKey, "Exploration table key", 120);
        ExplorationTableResult.RequireKey(SourceReference, "Exploration table source", 500);
        if (DieSides < 2 || DieSides > 1000)
            throw new InvalidOperationException("Exploration table die must have between 2 and 1000 sides.");
        if (Rows is null || Rows.Count < 1 || Rows.Count > 1000)
            throw new InvalidOperationException("Exploration table requires 1 to 1000 supplied rows.");
        foreach (var row in Rows)
        {
            if (row is null)
                throw new InvalidOperationException("Exploration table rows cannot be null.");
            row.Validate(DieSides);
        }

        var next = 1;
        foreach (var row in Rows.OrderBy(value => value.From))
        {
            if (row.From != next)
                throw new InvalidOperationException("Exploration table ranges must cover every die face exactly once.");
            next = checked(row.Through + 1);
        }
        if (next != DieSides + 1)
            throw new InvalidOperationException("Exploration table has uncovered die faces.");
    }

    public static void ValidateCollection(IReadOnlyList<ExplorationTableRollRequest> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.Count > 16)
            throw new InvalidOperationException("An exploration operation supports at most 16 table rolls.");
        foreach (var table in tables)
        {
            if (table is null)
                throw new InvalidOperationException("Exploration table definitions cannot be null.");
            table.Validate();
        }
        if (tables.GroupBy(value => (value.Scope, value.TableKey.ToLowerInvariant()))
            .Any(group => group.Count() != 1))
            throw new InvalidOperationException("An exploration operation may roll a table once per scope.");
    }
}


/// <summary>
/// Reusable GM-defined table stored with one expedition's exploration ledger.
/// Subsequent edits never rewrite previously resolved roll results.
/// </summary>
public sealed record ExplorationSavedTable(
    ExplorationTableRollRequest Definition,
    Guid LastOperationId,
    ExpeditionConsequenceProvenance Provenance)
{
    public void Validate()
    {
        Definition.Validate();
        if (LastOperationId == Guid.Empty)
            throw new InvalidOperationException("A saved exploration table must reference its last mutation.");
        Provenance.Validate();
    }
}

/// <summary>
/// Immutable idempotency receipt for saving or removing a table definition.
/// Null fingerprints mean the table was removed.
/// </summary>
public sealed record ExplorationSavedTableMutation(
    Guid OperationId,
    ExplorationTableScope Scope,
    string TableKey,
    string? DefinitionFingerprint,
    ExpeditionConsequenceProvenance Provenance)
{
    public void Validate()
    {
        if (OperationId == Guid.Empty || !Enum.IsDefined(Scope))
            throw new InvalidOperationException("Saved table operation requires valid identifiers.");
        ExplorationTableResult.RequireKey(TableKey, "Saved table key", 120);
        if (DefinitionFingerprint is not null &&
            (DefinitionFingerprint.Length != 64
                || DefinitionFingerprint.Any(value => !char.IsAsciiHexDigit(value))))
            throw new InvalidOperationException("Saved table fingerprint is malformed.");
        Provenance.Validate();
    }
}


/// <summary>
/// Reference to an expedition-local table. The action takes its definition
/// from the saved catalog at the validated expedition version and retains the
/// resolved result, so later catalog edits cannot change historical rolls.
/// </summary>
public sealed record ExplorationSavedTableReference(
    ExplorationTableScope Scope,
    string TableKey)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Scope))
            throw new InvalidOperationException("Unknown exploration saved table scope.");
        ExplorationTableResult.RequireKey(TableKey, "Saved exploration table key", 120);
    }

    public static void ValidateCollection(IReadOnlyList<ExplorationSavedTableReference> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > 16)
            throw new InvalidOperationException("An exploration action may reference at most 16 saved tables.");
        foreach (var entry in entries)
        {
            if (entry is null) throw new InvalidOperationException("Saved table references cannot be null.");
            entry.Validate();
        }
        if (entries.GroupBy(value => (value.Scope, value.TableKey.ToLowerInvariant())).Any(group => group.Count() > 1))
            throw new InvalidOperationException("A saved table cannot be referenced twice by one action.");
    }
}
