using HexCrawl.Application;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

public sealed record StartProcedureSessionRequest(
    string Name,
    string? ProcedureKey,
    Guid? ProcedureId,
    int? ProcedureRevision,
    StandaloneCrawlContextRequest Context,
    HexCoordinate? StartHex = null)
{
    public ProcedureStartSelection ProcedureSelection() =>
        new(ProcedureKey, ProcedureId, ProcedureRevision);
}
