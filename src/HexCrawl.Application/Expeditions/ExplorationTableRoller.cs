using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

/// <summary>
/// Rolls only externally supplied, fully covered tables through the existing
/// procedure random source. Outputs include the actual roll and supplied
/// table-source identity for durable GM audit.
/// </summary>
public sealed class ExplorationTableRoller(IProcedureResolutionRandomSource random)
{
    public IReadOnlyList<(ExplorationTableScope Scope, ExplorationTableResult Result)> Roll(
        IReadOnlyList<ExplorationTableRollRequest> tables)
    {
        ExplorationTableRollRequest.ValidateCollection(tables);
        var results = new List<(ExplorationTableScope, ExplorationTableResult)>(tables.Count);
        foreach (var table in tables)
        {
            var die = random.NextInt32(1, table.DieSides + 1);
            if (die < 1 || die > table.DieSides)
                throw new InvalidOperationException("The configured random source returned an invalid die result.");
            var row = table.Rows.Single(value => die >= value.From && die <= value.Through);
            results.Add((table.Scope, new ExplorationTableResult(
                table.TableKey, row.ResultText, $"1d{table.DieSides}={die}", table.SourceReference)));
        }
        return results;
    }
}
