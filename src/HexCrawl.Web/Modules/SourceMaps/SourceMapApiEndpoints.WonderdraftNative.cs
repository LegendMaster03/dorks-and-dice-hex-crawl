using System.Security.Cryptography;
using HexCrawl.Application;
using HexCrawl.Application.Assets;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Wonderdraft;
using Microsoft.Extensions.Options;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    private static async Task<IResult> ImportWonderdraftSourceAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService service,
        IMapAssetStore assetStore,
        IOptions<MapImportOptions> configuredOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var options = configuredOptions.Value;
        options.Validate();
        var owner = UserId(context);
        var world = await service.GetOverworldAsync(overworldId, owner, cancellationToken);
        var map = world.World.SourceMaps.FirstOrDefault(item => item.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source-map representation was not found.");

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Wonderdraft source import requires multipart/form-data." });
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
        if (map.PixelWidth <= 0 || map.PixelHeight <= 0)
        {
            return Results.BadRequest(new { error = "The selected raster map has no usable pixel dimensions." });
        }

        var sourceScaleX = (double)map.PixelWidth / summary.PixelWidth;
        var sourceScaleY = (double)map.PixelHeight / summary.PixelHeight;
        WorldPoint RasterPoint(WonderdraftPixelPoint point) =>
            new(point.X * sourceScaleX, point.Y * sourceScaleY);

        var content = document.Candidates.Select(candidate =>
        {
            var properties = new Dictionary<string, string>(
                candidate.Properties ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            if (candidate.Problem is not null) properties["import.problem"] = candidate.Problem;
            return new SourceMapContentElement(
                candidate.Key,
                candidate.Kind switch
                {
                    WonderdraftCandidateKind.Label => SourceMapContentKind.Label,
                    WonderdraftCandidateKind.Symbol => SourceMapContentKind.Symbol,
                    WonderdraftCandidateKind.Path => SourceMapContentKind.Line,
                    WonderdraftCandidateKind.Territory => SourceMapContentKind.Region,
                    _ => throw new ArgumentOutOfRangeException(nameof(candidate.Kind))
                },
                candidate.DisplayName,
                candidate.Descriptor,
                candidate.Position is null ? null : RasterPoint(candidate.Position),
                candidate.Points.Select(RasterPoint).ToArray(),
                properties);
        }).ToArray();

        string fingerprint;
        await using (var fingerprintStream = file.OpenReadStream())
        {
            fingerprint = Convert.ToHexString(
                await SHA256.HashDataAsync(fingerprintStream, cancellationToken))
                .ToLowerInvariant();
        }

        MapAssetWriteResult? newArchiveAsset = null;
        SourceMapSourceArchive sourceArchive;
        if (map.ImportProvenance?.SourceFingerprint == fingerprint
            && map.SourceArchive is { } existingArchive
            && await assetStore.GetInfoAsync(existingArchive.AssetKey, cancellationToken) is not null)
        {
            sourceArchive = existingArchive;
        }
        else
        {
            await using var archiveStream = file.OpenReadStream();
            newArchiveAsset = await assetStore.WriteAsync(archiveStream, cancellationToken);
            sourceArchive = new SourceMapSourceArchive(
                newArchiveAsset.AssetKey,
                newArchiveAsset.Length,
                "application/octet-stream",
                DisplayFileName(file.FileName));
        }

        var alignment = map.Alignment;
        var registrationMode = alignment is null ? "SourceOnly" : "Existing";
        string? registrationNote = alignment is null
            ? "Source records were imported, but the raster still needs whole-map placement because the project did not provide enough compatible physical scale information."
            : "The existing raster registration was preserved.";

        var hasExistingWorldAnchors =
            world.World.Locations.Count > 0
            || world.World.Features.Count > 0
            || world.World.SourceMaps.Any(item => item.Id != map.Id && item.Alignment is not null);

        if (alignment is null
            && !hasExistingWorldAnchors
            && TryCreateWonderdraftScaleAlignment(
                world.World.Grid,
                map,
                summary,
                sourceScaleX,
                sourceScaleY,
                out var derived,
                out var note))
        {
            alignment = derived;
            registrationMode = "PhysicalScale";
            registrationNote = note;
        }
        else if (alignment is null
            && hasExistingWorldAnchors
            && summary.PhysicalScale is not null)
        {
            registrationNote =
                "Wonderdraft physical scale was recovered, but translation and rotation relative to existing world content are ambiguous. The source records were imported without guessing; place the whole raster once with advanced registration.";
        }

        StoredOverworld updated;
        try
        {
            updated = await service.ImportContentAsync(
                overworldId,
                sourceMapId,
                owner,
                new ImportSourceMapContentCommand(
                    content,
                    new SourceMapImportProvenance(
                        "wonderdraft",
                        fingerprint,
                        DateTimeOffset.UtcNow,
                        content.Length),
                    sourceArchive,
                    alignment,
                    expectedVersion),
                cancellationToken);
        }
        catch
        {
            if (newArchiveAsset is not null)
            {
                try
                {
                    await assetStore.DeleteAsync(newArchiveAsset.AssetKey, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    loggerFactory.CreateLogger("HexCrawl.SourceArchiveImport").LogError(
                        cleanupException,
                        "Failed to compensate source archive {AssetKey} after Wonderdraft metadata persistence failed.",
                        newArchiveAsset.AssetKey);
                }
            }
            throw;
        }

        if (newArchiveAsset is not null
            && map.SourceArchive is { } previousArchive
            && !string.Equals(previousArchive.AssetKey, newArchiveAsset.AssetKey, StringComparison.Ordinal))
        {
            try
            {
                if (!await assetStore.DeleteAsync(previousArchive.AssetKey, CancellationToken.None))
                {
                    loggerFactory.CreateLogger("HexCrawl.SourceArchiveImport").LogWarning(
                        "Replaced source archive {AssetKey}, but the previous asset was already missing.",
                        previousArchive.AssetKey);
                }
            }
            catch (Exception cleanupException)
            {
                loggerFactory.CreateLogger("HexCrawl.SourceArchiveImport").LogError(
                    cleanupException,
                    "Replaced source archive metadata, but cleanup of previous asset {AssetKey} failed.",
                    previousArchive.AssetKey);
            }
        }

        return Results.Ok(new WonderdraftSourceImportContract(
            OverworldContract.From(updated),
            InspectionContract(summary),
            sourceMapId,
            content.Length,
            registrationMode,
            registrationNote));
    }

    private static bool TryCreateWonderdraftScaleAlignment(
        HexGridDefinition grid,
        SourceMapRepresentation map,
        WonderdraftProjectSummary summary,
        double sourceScaleX,
        double sourceScaleY,
        out MapRegistrationTransform alignment,
        out string note)
    {
        alignment = null!;
        note = string.Empty;
        if (summary.PhysicalScale is not { } scale
            || scale.UnitsPerPixel <= 0
            || !double.IsFinite(scale.UnitsPerPixel))
        {
            return false;
        }
        if (!TryMetersPerUnit(scale.UnitLabel, out var sourceMetersPerUnit)
            || grid.NeighborCenterDistance.Unit.MetersPerUnit is not { } worldMetersPerUnit
            || worldMetersPerUnit <= 0
            || grid.NeighborCenterDistance.Value <= 0)
        {
            return false;
        }
        if (sourceScaleX <= 0 || sourceScaleY <= 0
            || !double.IsFinite(sourceScaleX) || !double.IsFinite(sourceScaleY)
            || Math.Abs(sourceScaleX - sourceScaleY) > Math.Max(sourceScaleX, sourceScaleY) * 1e-6)
        {
            return false;
        }

        var neighborWorldUnits = Math.Sqrt(3d) * grid.HexRadiusWorldUnits;
        var metersPerWorldUnit =
            (grid.NeighborCenterDistance.Value * worldMetersPerUnit) / neighborWorldUnits;
        var metersPerRasterPixel =
            (scale.UnitsPerPixel * sourceMetersPerUnit) / sourceScaleX;
        var worldUnitsPerRasterPixel = metersPerRasterPixel / metersPerWorldUnit;
        if (worldUnitsPerRasterPixel <= 0 || !double.IsFinite(worldUnitsPerRasterPixel))
        {
            return false;
        }

        alignment = MapRegistrationTransform.Affine(
            worldUnitsPerRasterPixel,
            0,
            0,
            worldUnitsPerRasterPixel,
            grid.Origin.X - ((map.PixelWidth * worldUnitsPerRasterPixel) / 2d),
            grid.Origin.Y - ((map.PixelHeight * worldUnitsPerRasterPixel) / 2d));
        note =
            $"The raster was centered on the Hex Crawl grid origin and scaled from Wonderdraft's {scale.DistancePerSegment:g} {scale.UnitLabel} × {scale.SegmentCount} scale over {scale.PixelLength:g} project pixels.";
        return true;
    }

    private static bool TryMetersPerUnit(string label, out double metersPerUnit)
    {
        var normalized = label.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "mile":
            case "miles":
            case "mi":
                metersPerUnit = 1609.344;
                return true;
            case "kilometer":
            case "kilometers":
            case "kilometre":
            case "kilometres":
            case "km":
                metersPerUnit = 1000;
                return true;
            default:
                metersPerUnit = 0;
                return false;
        }
    }
}
