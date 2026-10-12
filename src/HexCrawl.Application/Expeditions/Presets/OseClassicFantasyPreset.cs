using HexCrawl.Domain.Procedure;
using static HexCrawl.Application.CrawlProcedureCatalog;

namespace HexCrawl.Application;

internal static class OseClassicFantasyPreset
{
    internal static CrawlProcedurePresetDefinition Create(CrawlProcedurePresetDefinition bx) =>
        new CrawlProcedurePresetDefinition(
            OseClassicPresetKey,
            "Old-School Essentials Classic Fantasy",
            "Uses the same wilderness-travel behavior as B/X while preserving Old-School Essentials as its own source identity.",
            1,
            bx.Recipe,
            "Old-School Essentials is referenced only as preset metadata; the materialized procedure is the shared generic B/X recipe.");
}
