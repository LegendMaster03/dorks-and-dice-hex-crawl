using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Assets;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Wonderdraft;
using Microsoft.Extensions.Options;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
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

        return Results.Ok(BuildWonderdraftCandidatePreview(map, document));
    }

    private static async Task<IResult> PreviewStoredWonderdraftCandidatesAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService service,
        IMapAssetStore assetStore,
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
            return Results.BadRequest(new { error = "Register the source-map representation before reviewing retained Wonderdraft source." });
        }
        if (map.PixelWidth <= 0 || map.PixelHeight <= 0)
        {
            return Results.BadRequest(new { error = "The registered source map has no usable raster dimensions." });
        }

        WonderdraftProjectDocument document;
        try
        {
            document = await ReadStoredWonderdraftProjectAsync(map, assetStore, options, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        return Results.Ok(BuildWonderdraftCandidatePreview(map, document));
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

        return await PromoteWonderdraftCandidatesAsync(
            overworldId,
            owner,
            map,
            document,
            selections,
            expectedVersion,
            semanticWorld,
            cancellationToken);
    }

    private static async Task<IResult> ImportStoredWonderdraftCandidatesAsync(
        Guid overworldId,
        Guid sourceMapId,
        WonderdraftStoredImportRequest request,
        HttpContext context,
        SourceMapApplicationService sourceMaps,
        HexCrawlService semanticWorld,
        IMapAssetStore assetStore,
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
            return Results.BadRequest(new { error = "Register the source-map representation before promoting retained Wonderdraft source." });
        }
        if (request.ExpectedVersion != world.Version)
        {
            throw new HexCrawlConcurrencyException(
                $"Expected overworld version {request.ExpectedVersion}, but current version is {world.Version}.");
        }

        var selections = request.Selections?.ToArray() ?? [];
        if (selections.Length == 0)
        {
            return Results.BadRequest(new { error = "Select at least one Wonderdraft candidate to promote." });
        }
        if (selections.Length > maxSelections)
        {
            return Results.BadRequest(new { error = $"At most {maxSelections} Wonderdraft candidates may be promoted in one request." });
        }
        if (selections.Any(selection => string.IsNullOrWhiteSpace(selection.CandidateKey)))
        {
            return Results.BadRequest(new { error = "Every promotion selection requires a candidateKey." });
        }
        if (selections.Select(selection => selection.CandidateKey).Distinct(StringComparer.Ordinal).Count() != selections.Length)
        {
            return Results.BadRequest(new { error = "A Wonderdraft candidate may be selected only once per promotion." });
        }

        WonderdraftProjectDocument document;
        try
        {
            document = await ReadStoredWonderdraftProjectAsync(map, assetStore, options, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }

        return await PromoteWonderdraftCandidatesAsync(
            overworldId,
            owner,
            map,
            document,
            selections,
            request.ExpectedVersion,
            semanticWorld,
            cancellationToken);
    }

    private static async Task<IResult> PromoteWonderdraftCandidatesAsync(
        Guid overworldId,
        string owner,
        SourceMapRepresentation map,
        WonderdraftProjectDocument document,
        IReadOnlyList<WonderdraftImportSelectionContract> selections,
        long expectedVersion,
        HexCrawlService semanticWorld,
        CancellationToken cancellationToken)
    {
        var summary = document.Summary;
        var candidates = document.Candidates.ToDictionary(candidate => candidate.Key, StringComparer.Ordinal);
        var scaleX = (double)map.PixelWidth / summary.PixelWidth;
        var scaleY = (double)map.PixelHeight / summary.PixelHeight;
        WorldPoint WorldPointFrom(WonderdraftPixelPoint point) =>
            map.Alignment!.ToWorld(new WorldPoint(point.X * scaleX, point.Y * scaleY));

        var locations = new List<ImportedLocationDefinition>();
        var features = new List<ImportedFeatureDefinition>();
        foreach (var selection in selections)
        {
            if (!candidates.TryGetValue(selection.CandidateKey, out var candidate))
            {
                return Results.BadRequest(new { error = $"Wonderdraft candidate '{selection.CandidateKey}' was not found in the retained project." });
            }
            if (candidate.Problem is not null)
            {
                return Results.BadRequest(new { error = $"Wonderdraft candidate '{selection.CandidateKey}' can not be promoted: {candidate.Problem}" });
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

    private static async Task<WonderdraftProjectDocument> ReadStoredWonderdraftProjectAsync(
        SourceMapRepresentation map,
        IMapAssetStore assetStore,
        MapImportOptions options,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(map.ImportProvenance?.SourceType, "wonderdraft", StringComparison.OrdinalIgnoreCase)
            || map.SourceArchive is not { } archive)
        {
            throw new HexCrawlNotFoundException("This source map has no retained Wonderdraft project.");
        }

        var info = await assetStore.GetInfoAsync(archive.AssetKey, cancellationToken)
            ?? throw new HexCrawlNotFoundException("The retained Wonderdraft project archive is missing.");
        if (info.Length > options.MaxFileBytes)
        {
            throw new InvalidDataException(
                $"The retained Wonderdraft project exceeds the configured {options.MaxFileBytes} byte Hex Crawl upload limit.");
        }

        await using var projectStream = await assetStore.OpenReadAsync(archive.AssetKey, cancellationToken);
        var document = await WonderdraftProjectInspector.ReadAsync(
            projectStream,
            options.MaxWonderdraftDecodedBytes,
            cancellationToken);
        var summary = document.Summary;
        if (summary.PixelWidth > options.MaxDimension || summary.PixelHeight > options.MaxDimension
            || ((long)summary.PixelWidth * summary.PixelHeight) > options.MaxPixelCount)
        {
            throw new InvalidDataException(
                $"Wonderdraft canvas dimensions {summary.PixelWidth}×{summary.PixelHeight} exceed the configured map-dimension safety limits.");
        }

        return document;
    }

    private static WonderdraftCandidatePreviewContract BuildWonderdraftCandidatePreview(
        SourceMapRepresentation map,
        WonderdraftProjectDocument document)
    {
        var summary = document.Summary;
        var scaleX = (double)map.PixelWidth / summary.PixelWidth;
        var scaleY = (double)map.PixelHeight / summary.PixelHeight;
        WorldPoint SourcePoint(WonderdraftPixelPoint point) => new(point.X * scaleX, point.Y * scaleY);
        WorldPoint WorldPointFrom(WonderdraftPixelPoint point) => map.Alignment!.ToWorld(SourcePoint(point));

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
                candidate.Properties ?? new Dictionary<string, string>(),
                sourcePosition,
                sourcePoints,
                worldPosition,
                worldPoints);
        }).ToArray();

        return new WonderdraftCandidatePreviewContract(
            map.Id,
            scaleX,
            scaleY,
            InspectionContract(summary),
            candidates);
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
        summary.IncludedDefaultPacks,
        summary.GridMetadata ?? new Dictionary<string, string>(),
        summary.ScaleMetadata ?? new Dictionary<string, string>(),
        summary.PhysicalScale is null
            ? null
            : new WonderdraftPhysicalScaleContract(
                summary.PhysicalScale.UnitLabel,
                summary.PhysicalScale.DistancePerSegment,
                summary.PhysicalScale.SegmentCount,
                summary.PhysicalScale.PixelLength,
                summary.PhysicalScale.UnitsPerPixel));


}
