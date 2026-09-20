using System.Security.Claims;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Assets;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Assets;
using HexCrawl.Infrastructure.Wonderdraft;
using Microsoft.Extensions.Options;

namespace HexCrawl.Web.Api;

public static class SourceMapApiEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/overworlds/{overworldId:guid}/source-maps");
        api.MapGet("", ListAsync);
        api.MapPost("", UploadAsync);
        api.MapPost("/wonderdraft/inspect", InspectWonderdraftAsync);
        api.MapPost("/{sourceMapId:guid}/wonderdraft/candidates", PreviewWonderdraftCandidatesAsync);
        api.MapPost("/{sourceMapId:guid}/wonderdraft/import", ImportWonderdraftCandidatesAsync);
        api.MapPut("/{sourceMapId:guid}", UpdateMetadataAsync);
        api.MapPut("/{sourceMapId:guid}/registration", RegisterAsync);
        api.MapGet("/{sourceMapId:guid}/asset", GetAssetAsync);
        api.MapDelete("/{sourceMapId:guid}", DeleteAsync);
    }

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

    private static async Task<IResult> InspectWonderdraftAsync(
        Guid overworldId,
        HttpContext context,
        SourceMapApplicationService service,
        IOptions<MapImportOptions> configuredOptions,
        CancellationToken cancellationToken)
    {
        var options = configuredOptions.Value;
        options.Validate();
        _ = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Wonderdraft inspection requires multipart/form-data." });
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "A non-empty .wonderdraft_map project file is required." });
        }
        if (file.Length > options.MaxFileBytes)
        {
            return Results.Problem(
                title: "Wonderdraft project is too large",
                detail: $"The project exceeds the configured {options.MaxFileBytes} byte Hex Crawl upload limit.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        WonderdraftProjectSummary summary;
        try
        {
            await using var projectStream = file.OpenReadStream();
            summary = await WonderdraftProjectInspector.InspectAsync(
                projectStream,
                options.MaxWonderdraftDecodedBytes,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        if (summary.PixelWidth > options.MaxDimension || summary.PixelHeight > options.MaxDimension
            || ((long)summary.PixelWidth * summary.PixelHeight) > options.MaxPixelCount)
        {
            return Results.BadRequest(new
            {
                error = $"Wonderdraft canvas dimensions {summary.PixelWidth}×{summary.PixelHeight} exceed the configured map-dimension safety limits."
            });
        }

        return Results.Ok(InspectionContract(summary));
    }

    private static async Task<IResult> PreviewWonderdraftCandidatesAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService service,
        IOptions<MapImportOptions> configuredOptions,
        CancellationToken cancellationToken)
    {
        var options = configuredOptions.Value;
        options.Validate();
        var world = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        var map = world.World.SourceMaps.FirstOrDefault(item => item.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source-map representation was not found.");
        if (map.Alignment is null)
        {
            return Results.BadRequest(new { error = "Register the source-map representation before reviewing Wonderdraft import candidates." });
        }
        if (map.PixelWidth <= 0 || map.PixelHeight <= 0)
        {
            return Results.BadRequest(new { error = "The registered source map has no usable raster dimensions." });
        }
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Wonderdraft candidate review requires multipart/form-data." });
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "A non-empty .wonderdraft_map project file is required." });
        }
        if (file.Length > options.MaxFileBytes)
        {
            return Results.Problem(
                title: "Wonderdraft project is too large",
                detail: $"The project exceeds the configured {options.MaxFileBytes} byte Hex Crawl upload limit.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        WonderdraftProjectDocument document;
        try
        {
            await using var projectStream = file.OpenReadStream();
            document = await WonderdraftProjectInspector.ReadAsync(
                projectStream,
                options.MaxWonderdraftDecodedBytes,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        var summary = document.Summary;
        if (summary.PixelWidth > options.MaxDimension || summary.PixelHeight > options.MaxDimension
            || ((long)summary.PixelWidth * summary.PixelHeight) > options.MaxPixelCount)
        {
            return Results.BadRequest(new
            {
                error = $"Wonderdraft canvas dimensions {summary.PixelWidth}×{summary.PixelHeight} exceed the configured map-dimension safety limits."
            });
        }

        var scaleX = (double)map.PixelWidth / summary.PixelWidth;
        var scaleY = (double)map.PixelHeight / summary.PixelHeight;
        WorldPoint SourcePoint(WonderdraftPixelPoint point) => new(point.X * scaleX, point.Y * scaleY);
        WorldPoint WorldPointFrom(WonderdraftPixelPoint point) => map.Alignment.ToWorld(SourcePoint(point));

        var candidates = document.Candidates.Select(candidate =>
        {
            WorldPoint? sourcePosition = candidate.Position is null ? null : SourcePoint(candidate.Position);
            var sourcePoints = candidate.Points.Select(SourcePoint).ToArray();
            WorldPoint? worldPosition = candidate.Position is null ? null : WorldPointFrom(candidate.Position);
            var worldPoints = candidate.Points.Select(WorldPointFrom).ToArray();
            var geometryKind = candidate.Kind switch
            {
                WonderdraftCandidateKind.Label or WonderdraftCandidateKind.Symbol => "Point",
                WonderdraftCandidateKind.Path => "Line",
                WonderdraftCandidateKind.Territory => "Region",
                _ => throw new ArgumentOutOfRangeException(nameof(candidate.Kind))
            };
            return new WonderdraftCandidateContract(
                candidate.Key,
                candidate.Kind.ToString(),
                geometryKind,
                candidate.DisplayName,
                candidate.Descriptor,
                candidate.Problem,
                sourcePosition,
                sourcePoints,
                worldPosition,
                worldPoints);
        }).ToArray();

        return Results.Ok(new WonderdraftCandidatePreviewContract(
            map.Id,
            scaleX,
            scaleY,
            InspectionContract(summary),
            candidates));
    }

    private static async Task<IResult> ImportWonderdraftCandidatesAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService sourceMaps,
        HexCrawlService semanticWorld,
        IOptions<MapImportOptions> configuredOptions,
        CancellationToken cancellationToken)
    {
        const int maxSelections = 5_000;
        var options = configuredOptions.Value;
        options.Validate();
        var owner = UserId(context);
        var world = await sourceMaps.GetOverworldAsync(overworldId, owner, cancellationToken);
        var map = world.World.SourceMaps.FirstOrDefault(item => item.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source-map representation was not found.");
        if (map.Alignment is null)
        {
            return Results.BadRequest(new { error = "Register the source-map representation before importing Wonderdraft candidates." });
        }
        if (map.PixelWidth <= 0 || map.PixelHeight <= 0)
        {
            return Results.BadRequest(new { error = "The registered source map has no usable raster dimensions." });
        }
        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Wonderdraft import requires multipart/form-data." });
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "A non-empty .wonderdraft_map project file is required." });
        }
        if (file.Length > options.MaxFileBytes)
        {
            return Results.Problem(
                title: "Wonderdraft project is too large",
                detail: $"The project exceeds the configured {options.MaxFileBytes} byte Hex Crawl upload limit.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        if (!long.TryParse(form["expectedVersion"].ToString(), out var expectedVersion))
        {
            return Results.BadRequest(new { error = "expectedVersion is required." });
        }
        if (expectedVersion != world.Version)
        {
            throw new HexCrawlConcurrencyException(
                $"Expected overworld version {expectedVersion}, but current version is {world.Version}.");
        }

        WonderdraftImportSelectionContract[]? selections;
        try
        {
            selections = JsonSerializer.Deserialize<WonderdraftImportSelectionContract[]>(
                form["selections"].ToString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "selections must be a valid JSON array." });
        }

        if (selections is null || selections.Length == 0)
        {
            return Results.BadRequest(new { error = "Select at least one Wonderdraft candidate to import." });
        }
        if (selections.Length > maxSelections)
        {
            return Results.BadRequest(new { error = $"At most {maxSelections} Wonderdraft candidates may be imported in one request." });
        }
        if (selections.Any(selection => string.IsNullOrWhiteSpace(selection.CandidateKey)))
        {
            return Results.BadRequest(new { error = "Every import selection requires a candidateKey." });
        }
        if (selections.Select(selection => selection.CandidateKey).Distinct(StringComparer.Ordinal).Count() != selections.Length)
        {
            return Results.BadRequest(new { error = "A Wonderdraft candidate may be selected only once per import." });
        }

        WonderdraftProjectDocument document;
        try
        {
            await using var projectStream = file.OpenReadStream();
            document = await WonderdraftProjectInspector.ReadAsync(
                projectStream,
                options.MaxWonderdraftDecodedBytes,
                cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        var summary = document.Summary;
        if (summary.PixelWidth > options.MaxDimension || summary.PixelHeight > options.MaxDimension
            || ((long)summary.PixelWidth * summary.PixelHeight) > options.MaxPixelCount)
        {
            return Results.BadRequest(new
            {
                error = $"Wonderdraft canvas dimensions {summary.PixelWidth}×{summary.PixelHeight} exceed the configured map-dimension safety limits."
            });
        }

        var candidates = document.Candidates.ToDictionary(candidate => candidate.Key, StringComparer.Ordinal);
        var scaleX = (double)map.PixelWidth / summary.PixelWidth;
        var scaleY = (double)map.PixelHeight / summary.PixelHeight;
        WorldPoint WorldPointFrom(WonderdraftPixelPoint point) =>
            map.Alignment.ToWorld(new WorldPoint(point.X * scaleX, point.Y * scaleY));

        var locations = new List<ImportedLocationDefinition>();
        var features = new List<ImportedFeatureDefinition>();
        foreach (var selection in selections)
        {
            if (!candidates.TryGetValue(selection.CandidateKey, out var candidate))
            {
                return Results.BadRequest(new { error = $"Wonderdraft candidate '{selection.CandidateKey}' was not found in the uploaded project." });
            }
            if (candidate.Problem is not null)
            {
                return Results.BadRequest(new { error = $"Wonderdraft candidate '{selection.CandidateKey}' can not be imported: {candidate.Problem}" });
            }
            if (string.IsNullOrWhiteSpace(selection.Name) || string.IsNullOrWhiteSpace(selection.Category))
            {
                return Results.BadRequest(new { error = $"Wonderdraft candidate '{selection.CandidateKey}' requires an explicit name and category." });
            }

            switch (selection.Target)
            {
                case "Location" when candidate.Kind is WonderdraftCandidateKind.Label or WonderdraftCandidateKind.Symbol
                    && candidate.Position is not null:
                    if (!Enum.TryParse<LocationDiscoverability>(
                            selection.Discoverability,
                            ignoreCase: true,
                            out var discoverability))
                    {
                        return Results.BadRequest(new
                        {
                            error = $"Wonderdraft candidate '{selection.CandidateKey}' requires discoverability Obvious, Hidden, or Conditional."
                        });
                    }
                    locations.Add(new ImportedLocationDefinition(
                        selection.Name,
                        selection.Category,
                        WorldPointFrom(candidate.Position),
                        discoverability));
                    break;

                case "PointFeature" when candidate.Kind is WonderdraftCandidateKind.Label or WonderdraftCandidateKind.Symbol
                    && candidate.Position is not null:
                    features.Add(new ImportedFeatureDefinition(
                        selection.Name,
                        selection.Category,
                        SpatialFeatureKind.Point,
                        WorldPointFrom(candidate.Position),
                        null,
                        null));
                    break;

                case "LineFeature" when candidate.Kind == WonderdraftCandidateKind.Path:
                    features.Add(new ImportedFeatureDefinition(
                        selection.Name,
                        selection.Category,
                        SpatialFeatureKind.Line,
                        null,
                        candidate.Points.Select(WorldPointFrom).ToArray(),
                        null));
                    break;

                case "RegionFeature" when candidate.Kind == WonderdraftCandidateKind.Territory:
                    features.Add(new ImportedFeatureDefinition(
                        selection.Name,
                        selection.Category,
                        SpatialFeatureKind.Region,
                        null,
                        null,
                        candidate.Points.Select(WorldPointFrom).ToArray()));
                    break;

                case "Location":
                case "PointFeature":
                case "LineFeature":
                case "RegionFeature":
                    return Results.BadRequest(new
                    {
                        error = $"Target '{selection.Target}' is incompatible with Wonderdraft candidate '{selection.CandidateKey}' ({candidate.Kind})."
                    });

                default:
                    return Results.BadRequest(new
                    {
                        error = $"Wonderdraft import target '{selection.Target}' is not supported."
                    });
            }
        }

        var updated = await semanticWorld.ImportWorldObjectsAsync(
            overworldId,
            owner,
            new ImportWorldObjectsCommand(locations, features, expectedVersion),
            cancellationToken);
        return Results.Ok(OverworldContract.From(updated));
    }

    private static WonderdraftInspectionContract InspectionContract(WonderdraftProjectSummary summary) => new(
        summary.FormatVersion,
        summary.PixelWidth,
        summary.PixelHeight,
        summary.SymbolCount,
        summary.LabelCount,
        summary.PathCount,
        summary.TerritoryCount,
        summary.HasGrid,
        summary.IncludedPacks,
        summary.IncludedDefaultPacks);

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

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException("An authenticated Tool Host or explicitly configured standalone development identity is required.");

    private static string? DisplayFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..].Trim();
        return name.Length == 0 ? null : name[..Math.Min(name.Length, 255)];
    }
}
