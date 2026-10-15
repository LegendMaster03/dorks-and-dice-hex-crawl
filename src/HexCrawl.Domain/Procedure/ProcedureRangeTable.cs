namespace HexCrawl.Domain.Procedure;

/// <summary>
/// Source-attributed ranged random table, independent of ruleset, map
/// geometry, journey progression, and the subject receiving its outcome.
/// </summary>
public sealed record ProcedureRangeTableEntry(int From, int Through, string ResultText);

public sealed record ProcedureRangeTable(
    string TableKey,
    int DieSides,
    string SourceReference,
    IReadOnlyList<ProcedureRangeTableEntry> Rows)
{
    public void Validate()
    {
        RequireText(TableKey, 120, "Table key");
        RequireText(SourceReference, 500, "Table source");
        if (DieSides < 2 || DieSides > 1000)
            throw new InvalidOperationException("A range table die must have between 2 and 1000 sides.");
        if (Rows is null || Rows.Count < 1 || Rows.Count > 1000)
            throw new InvalidOperationException("A range table needs 1 to 1000 supplied rows.");

        var next = 1;
        foreach (var row in Rows.OrderBy(value => value?.From))
        {
            if (row is null || row.From != next || row.Through < row.From
                || row.Through > DieSides)
                throw new InvalidOperationException(
                    "A range table must cover every possible die result exactly once.");
            RequireText(row.ResultText, 2000, "Table result");
            next = checked(row.Through + 1);
        }
        if (next != DieSides + 1)
            throw new InvalidOperationException(
                "A range table must cover every possible die result exactly once.");
    }

    private static void RequireText(string? value, int limit, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > limit)
            throw new InvalidOperationException(
                $"{label} must be a nonempty, trimmed string of at most {limit} characters.");
    }
}

public sealed record ProcedureRangeTableOutcome(
    string TableKey,
    string ResultText,
    string SourceReference,
    int DieSides,
    int RolledFace);
