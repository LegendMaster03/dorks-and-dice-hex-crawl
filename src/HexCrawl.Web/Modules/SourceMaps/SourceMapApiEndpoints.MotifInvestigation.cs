using HexCrawl.Application;
using HexCrawl.Application.Assets;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    /// <summary>
    /// Explicitly requested read-only research analysis. No saving,
    /// registration, world conversion, or procedure compatibility decision.
    /// The existing grid-analysis endpoint continues to use Surveyor v2.
    /// </summary>
    private static async Task<IResult> InvestigateMotifAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService service,
        IMapAssetStore assetStore,
        IPeriodicMotifInvestigationService investigationService,
        CancellationToken cancellationToken)
    {
        var stored = await service.GetOverworldAsync(
            overworldId, UserId(context), cancellationToken);
        var sourceMap = stored.World.SourceMaps.SingleOrDefault(map => map.Id == sourceMapId);
        if (sourceMap is null)
            return Results.NotFound();

        context.Response.Headers.CacheControl = "no-store";
        await using var raster = await assetStore.OpenReadAsync(
            sourceMap.AssetKey, cancellationToken);
        PeriodicMotifInvestigation investigation;
        try
        {
            investigation = await investigationService.InvestigateAsync(
                raster, sourceMap.MediaType, context.TraceIdentifier, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MapAnalysisTimeoutException error)
        {
            return Results.Problem(
                title: "Experimental map investigation timed out",
                detail: error.Message,
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (MapAnalysisAuthenticationException)
        {
            return Results.Problem(
                title: "Experimental map investigation is unavailable",
                detail: "The internal Surveyor service rejected its authentication credential.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (MapAnalysisProtocolException error)
        {
            return Results.Problem(
                title: "Experimental map investigation returned an invalid response",
                detail: error.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (MapAnalysisUnavailableException error)
        {
            return Results.Problem(
                title: "Experimental map investigation is unavailable",
                detail: error.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (investigation.Authoritative || investigation.Maturity != "experimental")
            return Results.Problem(
                title: "Experimental map investigation returned an invalid response",
                detail: "Investigation may not accept or change world topology.",
                statusCode: StatusCodes.Status502BadGateway);

        if (investigation.Source is { } source
            && (source.Width != sourceMap.PixelWidth
                || source.Height != sourceMap.PixelHeight
                || !string.Equals(source.MediaType, sourceMap.MediaType,
                    StringComparison.OrdinalIgnoreCase)))
            return Results.Problem(
                title: "Experimental map investigation returned incompatible source metadata",
                detail: "The observed raster does not match this saved map. No world state was changed.",
                statusCode: StatusCodes.Status502BadGateway);
        if (investigation.Status != "unsupported" && investigation.Source is null)
            return Results.Problem(
                title: "Experimental map investigation returned incomplete source metadata",
                detail: "The observed raster dimensions were not provided.",
                statusCode: StatusCodes.Status502BadGateway);

        return Results.Ok(investigation);
    }
}
