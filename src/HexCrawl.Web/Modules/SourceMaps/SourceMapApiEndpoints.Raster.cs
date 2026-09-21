using HexCrawl.Application;
using HexCrawl.Application.Assets;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Assets;
using Microsoft.Extensions.Options;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    private static async Task<IResult> ListAsync(
        Guid overworldId,
        HttpContext context,
        SourceMapApplicationService service,
        CancellationToken cancellationToken)
    {
        var world = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        return Results.Ok(new SourceMapListContract(
            world.Version,
            world.World.SourceMaps.Select(SourceMapDetailContract.From).ToArray()));
    }

    private static async Task<IResult> UploadAsync(
        Guid overworldId,
        HttpContext context,
        SourceMapApplicationService service,
        IMapAssetStore assetStore,
        IOptions<MapImportOptions> configuredOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var options = configuredOptions.Value;
        options.Validate();
        var current = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Source-map upload requires multipart/form-data." });
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "A non-empty raster file is required." });
        }
        if (file.Length > options.MaxFileBytes)
        {
            return Results.Problem(
                title: "Map file is too large",
                detail: $"The raster exceeds the configured {options.MaxFileBytes} byte Hex Crawl upload limit.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        if (!long.TryParse(form["expectedVersion"].ToString(), out var expectedVersion))
        {
            return Results.BadRequest(new { error = "expectedVersion is required." });
        }
        if (expectedVersion != current.Version)
        {
            throw new HexCrawlConcurrencyException($"Expected overworld version {expectedVersion}, but current version is {current.Version}.");
        }
        if (!Enum.TryParse<SourceMapRole>(form["role"].ToString(), ignoreCase: true, out var role))
        {
            return Results.BadRequest(new { error = "role must be GM, Player, Neutral, or Other." });
        }
        var geographyKey = form["geographyKey"].ToString();
        var name = form["name"].ToString();
        if (string.IsNullOrWhiteSpace(geographyKey) || string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest(new { error = "name and geographyKey are required." });
        }
        _ = bool.TryParse(form["containsBakedGrid"].ToString(), out var containsBakedGrid);

        RasterImageInfo raster;
        try
        {
            await using var headerStream = file.OpenReadStream();
            raster = await RasterImageInspector.InspectAsync(headerStream, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        if (raster.Width > options.MaxDimension || raster.Height > options.MaxDimension
            || ((long)raster.Width * raster.Height) > options.MaxPixelCount)
        {
            return Results.BadRequest(new
            {
                error = $"Raster dimensions {raster.Width}×{raster.Height} exceed the configured map-dimension safety limits."
            });
        }

        MapAssetWriteResult asset;
        await using (var assetStream = file.OpenReadStream())
        {
            asset = await assetStore.WriteAsync(assetStream, cancellationToken);
        }

        var logger = loggerFactory.CreateLogger("HexCrawl.SourceMapUpload");
        try
        {
            var updated = await service.CreateUploadedAsync(
                overworldId,
                UserId(context),
                new UploadSourceMapCommand(
                    geographyKey,
                    name,
                    role,
                    asset.AssetKey,
                    containsBakedGrid,
                    raster.Width,
                    raster.Height,
                    raster.MediaType,
                    DisplayFileName(file.FileName),
                    expectedVersion),
                cancellationToken);
            return Results.Created(
                $"/api/overworlds/{overworldId:D}/source-maps/{updated.World.SourceMaps[^1].Id:D}",
                OverworldContract.From(updated));
        }
        catch
        {
            try
            {
                await assetStore.DeleteAsync(asset.AssetKey, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogError(cleanupException, "Failed to compensate asset {AssetKey} after source-map metadata persistence failed.", asset.AssetKey);
            }
            throw;
        }
    }

    private static async Task<IResult> UpdateMetadataAsync(
        Guid overworldId,
        Guid sourceMapId,
        SourceMapMetadataRequest request,
        HttpContext context,
        SourceMapApplicationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateMetadataAsync(
            overworldId, sourceMapId, UserId(context), request.ToCommand(), cancellationToken)));

    private static async Task<IResult> RegisterAsync(
        Guid overworldId,
        Guid sourceMapId,
        SourceMapRegistrationRequest request,
        HttpContext context,
        SourceMapApplicationService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.RegisterAsync(
            overworldId, sourceMapId, UserId(context), request.ToCommand(), cancellationToken)));

    private static async Task<IResult> GetAssetAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService service,
        IMapAssetStore assetStore,
        CancellationToken cancellationToken)
    {
        var world = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        var map = world.World.SourceMaps.FirstOrDefault(item => item.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source-map representation was not found.");
        if (await assetStore.GetInfoAsync(map.AssetKey, cancellationToken) is null)
        {
            return Results.NotFound();
        }
        var stream = await assetStore.OpenReadAsync(map.AssetKey, cancellationToken);
        context.Response.Headers.CacheControl = "private, max-age=300";
        return Results.Stream(stream, map.MediaType, enableRangeProcessing: true);
    }

    private static async Task<IResult> DeleteAsync(
        Guid overworldId,
        Guid sourceMapId,
        long expectedVersion,
        HttpContext context,
        SourceMapApplicationService service,
        IMapAssetStore assetStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(
            overworldId, sourceMapId, UserId(context), expectedVersion, cancellationToken);
        var logger = loggerFactory.CreateLogger("HexCrawl.SourceMapDelete");
        try
        {
            if (!await assetStore.DeleteAsync(deleted.SourceMap.AssetKey, CancellationToken.None))
            {
                logger.LogError("Source-map metadata {SourceMapId} was deleted, but asset {AssetKey} was already missing.", sourceMapId, deleted.SourceMap.AssetKey);
                return Results.Problem(
                    title: "Source map deleted with asset cleanup problem",
                    detail: "The representation metadata was deleted, but its binary asset was missing during cleanup.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Source-map metadata {SourceMapId} was deleted, but asset {AssetKey} cleanup failed.", sourceMapId, deleted.SourceMap.AssetKey);
            return Results.Problem(
                title: "Source map deleted with asset cleanup problem",
                detail: "The representation metadata was deleted, but its binary asset could not be removed. Manual cleanup may be required.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
        return Results.Ok(OverworldContract.From(deleted.World));
    }

    private static string? DisplayFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..].Trim();
        return name.Length == 0 ? null : name[..Math.Min(name.Length, 255)];
    }
}
