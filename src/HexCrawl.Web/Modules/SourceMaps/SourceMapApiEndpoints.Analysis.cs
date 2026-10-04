using HexCrawl.Application;
using HexCrawl.Application.Assets;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    private static async Task<IResult> AnalyzeGridAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService service,
        IMapAssetStore assetStore,
        IMapAnalysisService mapAnalysis,
        CancellationToken cancellationToken)
    {
        var stored = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        var sourceMap = stored.World.SourceMaps.SingleOrDefault(map => map.Id == sourceMapId);
        if (sourceMap is null)
            return Results.NotFound();

        await using var raster = await assetStore.OpenReadAsync(sourceMap.AssetKey, cancellationToken);
        MapHexGridAnalysis analysis;
        try
        {
            analysis = await mapAnalysis.DetectHexGridAsync(
                raster,
                sourceMap.MediaType,
                new MapAnalysisOptions(
                    MinimumSpacingPixels: 12,
                    MinimumConfidence: 0.54),
                context.TraceIdentifier,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MapAnalysisTimeoutException exception)
        {
            return Results.Problem(
                title: "Automatic map analysis timed out",
                detail: $"{exception.Message} Manual registration remains available.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (MapAnalysisAuthenticationException)
        {
            return Results.Problem(
                title: "Automatic map analysis is unavailable",
                detail: "The internal Surveyor service rejected Hex Crawl authentication. Manual registration remains available.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (MapAnalysisProtocolException exception)
        {
            return Results.Problem(
                title: "Automatic map analysis returned an invalid response",
                detail: $"{exception.Message} Manual registration remains available.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (MapAnalysisUnavailableException exception)
        {
            return Results.Problem(
                title: "Automatic map analysis is unavailable",
                detail: $"{exception.Message} Manual registration remains available.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (analysis.Source.Width != sourceMap.PixelWidth || analysis.Source.Height != sourceMap.PixelHeight)
        {
            return Results.Problem(
                title: "Automatic map analysis returned incompatible dimensions",
                detail: $"Surveyor decoded {analysis.Source.Width}×{analysis.Source.Height}, but the authoritative source-map metadata is {sourceMap.PixelWidth}×{sourceMap.PixelHeight}. No world state was changed.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        if (!string.Equals(analysis.Source.MediaType, sourceMap.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                title: "Automatic map analysis returned incompatible media metadata",
                detail: "Surveyor decoded a media type that does not match the authoritative source map. No world state was changed.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(analysis);
    }
}
