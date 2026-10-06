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

public sealed record StartProcedureExpeditionRequest(
    string Name,
    string? ProcedureKey,
    Guid? ProcedureId,
    int? ProcedureRevision,
    HexCoordinate StartHex,
    string? PresentationKey = null)
{
    public ProcedureStartSelection ProcedureSelection() =>
        new(ProcedureKey, ProcedureId, ProcedureRevision);

    public string ResolvedPresentationKey =>
        string.IsNullOrWhiteSpace(PresentationKey) ? "exploration-map" : PresentationKey.Trim();
}
