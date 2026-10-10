using HexCrawl.Application;
using HexCrawl.Application.Assets;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    /// <summary>
    /// Explicitly requested read-only research analysis. No saving,
    /// registration, world conversion, or procedure compatibility decision.
    /// The existing grid-analysis endpoint continues to use Surveyor v2.
    /// </summary>
    private static async Task<IResult> AssessMotifCompatibilityAsync(
        Guid overworldId, Guid sourceMapId,
        Guid? procedureId, int? procedureRevision,
        HttpContext context,
        SourceMapApplicationService sourceMaps,
        IMapAssetStore assets,
        IPeriodicMotifInvestigationService surveyor,
        HexCrawlService crawls,
        CampaignProcedureService procedures,
        CancellationToken cancellationToken)
    {
        if (procedureId.HasValue != procedureRevision.HasValue
            || procedureRevision is <= 0 || procedureId == Guid.Empty)
            throw new ArgumentException("Choose both a saved procedure and a revision, or neither.");
        var result = await InvestigateMotifAsync(overworldId, sourceMapId, context,
            sourceMaps, assets, surveyor, cancellationToken);
        if (result is not Ok<PeriodicMotifInvestigation> analyzed || analyzed.Value is null)
            return result;

        var owner = UserId(context);
        var stored = await crawls.GetOverworldAsync(overworldId, owner, cancellationToken);
        var procedure = procedureId is { } selected && procedureRevision is { } revision
            ? (await procedures.GetAsync(owner, selected, revision, cancellationToken)).Procedure
            : null;
        var expeditions = await crawls.ListExpeditionsAsync(overworldId, owner, cancellationToken);
        var blockers = new List<string>();
        if (expeditions.Count > 0)
            blockers.Add($"{expeditions.Count} saved expeditions, including "
                + string.Join(", ", expeditions.Take(3).Select(item => item.Name)) + ".");
        var registrations = stored.World.SourceMaps.Count(map => map.Alignment is not null);
        if (registrations > 0)
            blockers.Add($"{registrations} existing source-map registrations.");
        if (stored.World.Features.Count > 0 || stored.World.Locations.Count > 0)
            blockers.Add($"{stored.World.Features.Count} features and "
                + $"{stored.World.Locations.Count} locations with world-space positions.");
        if (stored.World.EnvironmentAnnotations.Count > 0)
            blockers.Add($"{stored.World.EnvironmentAnnotations.Count} environment annotations.");
        return Results.Ok(Phase19TilingCompatibility.Assess(
            stored.World.SpatialTiling, analyzed.Value, procedure, blockers, stored.Version));
    }

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
