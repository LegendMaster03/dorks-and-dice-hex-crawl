using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// User-supplied, geometry-neutral table rows. The application must never
/// substitute publisher content, missing ranges, or source attribution.
/// </summary>
public enum ExplorationTableScope { Location, Region }

public sealed record ExplorationTableRollRequest(
    ExplorationTableScope Scope,
    string TableKey,
    int DieSides,
    string SourceReference,
    IReadOnlyList<ProcedureRangeTableEntry> Rows)
{
    public ProcedureRangeTable ToProcedureRangeTable() =>
        new(TableKey, DieSides, SourceReference, Rows);

    public void Validate()
    {
        if (!Enum.IsDefined(Scope))
            throw new InvalidOperationException("Unknown exploration table scope.");
        ToProcedureRangeTable().Validate();
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
