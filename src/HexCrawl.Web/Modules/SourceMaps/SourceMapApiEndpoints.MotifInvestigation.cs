using HexCrawl.Application;
using HexCrawl.Application.Persistence;
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
        var blockers = CorrectionBlockers(stored, expeditions);
        var assessment = Phase19TilingCompatibility.Assess(
            stored.World.SpatialTiling, analyzed.Value, procedure, blockers, stored.Version);
        return Results.Ok(assessment);
    }

    private static IReadOnlyList<string> CorrectionBlockers(
        StoredOverworld world, IReadOnlyList<ExpeditionSummary> expeditions)
    {
        var blockers = new List<string>();
        if (expeditions.Count > 0)
            blockers.Add($"{expeditions.Count} saved expeditions, including "
                + string.Join(", ", expeditions.Take(3).Select(item => item.Name)) + ".");
        var registrations = world.World.SourceMaps.Count(map => map.Alignment is not null);
        if (registrations > 0)
            blockers.Add($"{registrations} existing source-map registrations.");
        if (world.World.Features.Count > 0 || world.World.Locations.Count > 0)
            blockers.Add($"{world.World.Features.Count} features and "
                + $"{world.World.Locations.Count} locations with world-space positions.");
        if (world.World.EnvironmentAnnotations.Count > 0)
            blockers.Add($"{world.World.EnvironmentAnnotations.Count} environment annotations.");
        if (world.World.SpatialTiling.PhysicalDistancePerWorldUnit is not null)
            blockers.Add("an existing physical-distance calibration.");
        return blockers;
    }

    private sealed record ApplyMotifCorrectionRequest(
        int CandidateIndex, HexCrawl.Domain.World.MapRegistrationTransform Alignment,
        long ExpectedVersion, Guid? ProcedureId = null, int? ProcedureRevision = null);

    /// <summary>
    /// An explicit opt-in correction for an unused uncalibrated world only.
    /// The candidate is reconstructed independently of Surveyor's measured
    /// polygons; exact image correspondence is rechecked at this boundary.
    /// A single guarded database transaction changes the world and image
    /// alignment, with no procedure or expedition mutation.
    /// </summary>
    private static async Task<IResult> ApplyMotifCorrectionAsync(
        Guid overworldId, Guid sourceMapId,
        ApplyMotifCorrectionRequest request,
        HttpContext context,
        SourceMapApplicationService sourceMaps,
        IMapAssetStore assets,
        IPeriodicMotifInvestigationService surveyor,
        HexCrawlService crawls,
        CampaignProcedureService procedures,
        CancellationToken cancellationToken)
    {
        if (request.ProcedureId.HasValue != request.ProcedureRevision.HasValue
            || request.ProcedureRevision is <= 0 || request.ProcedureId == Guid.Empty)
            throw new ArgumentException("Choose a saved procedure and revision together, or neither.");
        var owner = UserId(context);
        var world = await crawls.GetOverworldAsync(overworldId, owner, cancellationToken);
        if (world.Version != request.ExpectedVersion)
            throw new HexCrawlConcurrencyException(
                "The world changed while its replacement cells were being reviewed. Reload and check the image again.");
        var observed = await InvestigateMotifAsync(overworldId, sourceMapId,
            context, sourceMaps, assets, surveyor, cancellationToken);
        if (observed is not Ok<PeriodicMotifInvestigation> analyzed
            || analyzed.Value is null) return observed;

        var applicable = request.ProcedureId is { } id && request.ProcedureRevision is { } rev
            ? (await procedures.GetAsync(owner, id, rev, cancellationToken)).Procedure
            : null;
        var expeditions = await crawls.ListExpeditionsAsync(
            overworldId, owner, cancellationToken);
        var assessment = Phase19TilingCompatibility.Assess(
            world.World.SpatialTiling, analyzed.Value, applicable,
            CorrectionBlockers(world, expeditions), world.Version);
        if (!assessment.CanCorrectInPlace || assessment.Correction is not
            { Status: "candidates" } options
            || !options.Candidates.Any(candidate => candidate.Index == request.CandidateIndex
                && Phase19RasterRegistration.Equivalent(candidate.Alignment, request.Alignment)))
            throw new HexCrawlConflictException(
                "The observed pattern can not safely replace this world's current cells. No changes were made.");
        var replacement = Phase19RasterRegistration.ConstructReplacement(
            world.World.SpatialTiling, analyzed.Value)
            ?? throw new HexCrawlConflictException(
                "The observed cell pattern no longer has a verified world geometry.");
        var updated = await sourceMaps.ReplaceUnusedPeriodicWorldAsync(
            overworldId, sourceMapId, owner, replacement,
            request.Alignment, request.ExpectedVersion, cancellationToken);
        return Results.Ok(OverworldContract.From(updated));
    }

    private sealed record ApplyMotifRegistrationRequest(
        int CandidateIndex, HexCrawl.Domain.World.MapRegistrationTransform Alignment,
        long ExpectedVersion);

    /// <summary>
    /// Re-run the same independent image checks at the save boundary. A client
    /// can select a preview, but can not submit an arbitrary affine transform.
    /// Only source-map alignment changes; world topology and procedures do not.
    /// </summary>
    private static async Task<IResult> ApplyMotifRegistrationAsync(
        Guid overworldId, Guid sourceMapId,
        ApplyMotifRegistrationRequest request,
        HttpContext context,
        SourceMapApplicationService sourceMaps,
        IMapAssetStore assets,
        IPeriodicMotifInvestigationService surveyor,
        HexCrawlService crawls,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var world = await crawls.GetOverworldAsync(overworldId, owner, cancellationToken);
        if (request.ExpectedVersion != world.Version)
            throw new HexCrawlConcurrencyException(
                "The world changed while the detected map placement was being reviewed. Reload and check it again.");
        if (request.Alignment is null)
            throw new ArgumentException("A selected verified map placement is required.");

        var analyzed = await InvestigateMotifAsync(overworldId, sourceMapId,
            context, sourceMaps, assets, surveyor, cancellationToken);
        if (analyzed is not Ok<PeriodicMotifInvestigation> observation
            || observation.Value is null)
            return analyzed;
        var options = Phase19RasterRegistration.Assess(
            world.World.SpatialTiling, observation.Value);
        if (options.Status != "candidates"
            || !options.Candidates.Any(candidate => candidate.Index == request.CandidateIndex
                && Phase19RasterRegistration.Equivalent(candidate.Alignment, request.Alignment)))
            throw new HexCrawlConflictException(
                "The detected placement could not be verified against the saved world and current image. Check the map again before saving.");

        var result = await sourceMaps.RegisterComputedAffineAsync(
            overworldId, sourceMapId, owner, request.Alignment, request.ExpectedVersion,
            cancellationToken);
        return Results.Ok(OverworldContract.From(result));
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
