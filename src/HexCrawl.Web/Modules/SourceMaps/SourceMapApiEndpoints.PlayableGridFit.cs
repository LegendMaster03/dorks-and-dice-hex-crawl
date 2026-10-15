using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    public sealed record ConfirmPlayableGridFitRequest(
        long ExpectedVersion,
        RasterGridMargins? Margins = null,
        int? ObservedColumns = null,
        int? ObservedRows = null);

    private sealed record ReviewedGridFit(
        MapRegistrationTransform? Alignment,
        string Status,
        string Note,
        int ExpectedColumns,
        int ExpectedRows);

    private static ReviewedGridFit ReviewGridFit(
        PeriodicWorldTiling tiling,
        SourceMapRepresentation map,
        RasterGridMargins margins,
        int? columns,
        int? rows)
    {
        if (columns.HasValue != rows.HasValue)
            throw new InvalidOperationException(
                "Provide both the printed grid column count and row count.");
        if (columns is <= 0 or > 100000 || rows is <= 0 or > 100000)
            throw new InvalidOperationException(
                "The printed grid must have positive row and column counts.");
        if (tiling.PlayableBounds is not { } bounds)
            throw new InvalidOperationException(
                "This world has no finite map dimensions. Set playable bounds or use manual placement.");
        // Validate margins even for incompatible reported counts: invalid
        // rectangles should never be treated as an ordinary grid mismatch.
        margins.InnerRectangle(map.PixelWidth, map.PixelHeight);
        if (columns is { } observedColumns && rows is { } observedRows
            && (observedColumns != bounds.Width || observedRows != bounds.Height))
        {
            return new(null, "incompatible",
                $"The printed grid is reported as {observedColumns} columns × {observedRows} rows, " +
                $"but this map expects {bounds.Width} columns × {bounds.Height} rows. " +
                "A full-grid fit would incorrectly stretch the map cells. Check the grid count, " +
                "change the world map dimensions or use manual placement for a larger reference image.",
                bounds.Width, bounds.Height);
        }
        var alignment = PlayableGridRasterFit.Calculate(
            tiling, map.PixelWidth, map.PixelHeight, margins);
        if (!columns.HasValue)
        {
            return new(alignment, "unverified",
                $"The image is being fitted to the {bounds.Width}×{bounds.Height} playable grid, " +
                "but the printed row and column count has not been verified. " +
                "Margins are allowed. Enter the actual number of printed grid columns and rows " +
                "before saving; check that the overlay matches the artwork.",
                bounds.Width, bounds.Height);
        }
        return new(alignment, "reported-match",
            $"The reported {bounds.Width}×{bounds.Height} grid matches the playable size. " +
            "This count was supplied by the GM, not independently detected from the pixels. " +
            "Margins are excluded from the grid alignment but the complete raster is preserved. " +
            "Verify the preview before saving.",
            bounds.Width, bounds.Height);
    }

    /// <summary>
    /// Read-only comparison. The existing Surveyor motif detector checks
    /// structural pattern, not reliable printed rows/columns or image borders.
    /// Never advertise a row-count match as independently detected.
    /// </summary>
    private static async Task<IResult> PreviewPlayableGridFitAsync(
        Guid overworldId,
        Guid sourceMapId,
        int? observedColumns,
        int? observedRows,
        double? leftPercent,
        double? topPercent,
        double? rightPercent,
        double? bottomPercent,
        HttpContext context,
        SourceMapApplicationService service,
        CancellationToken cancellationToken)
    {
        var world = await service.GetOverworldAsync(
            overworldId, UserId(context), cancellationToken);
        var map = world.World.SourceMaps.SingleOrDefault(value => value.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source map was not found.");
        var margins = new RasterGridMargins(
            leftPercent ?? 0, topPercent ?? 0, rightPercent ?? 0, bottomPercent ?? 0);
        var fit = ReviewGridFit(world.World.SpatialTiling, map, margins,
            observedColumns, observedRows);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            alignment = fit.Alignment,
            status = fit.Status,
            note = fit.Note,
            expectedColumns = fit.ExpectedColumns,
            expectedRows = fit.ExpectedRows,
            worldVersion = world.Version,
            playableBounds = world.World.SpatialTiling.PlayableBounds
        });
    }

    /// <summary>
    /// Recompute using current authoritative geometry, source dimensions and
    /// explicitly reported printed cell counts at the version-checked boundary.
    /// Arbitrary client-supplied matrices are never accepted.
    /// </summary>
    private static async Task<IResult> SavePlayableGridFitAsync(
        Guid overworldId,
        Guid sourceMapId,
        ConfirmPlayableGridFitRequest request,
        HttpContext context,
        SourceMapApplicationService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var world = await service.GetOverworldAsync(
            overworldId, owner, cancellationToken);
        if (world.Version != request.ExpectedVersion)
            throw new HexCrawlConcurrencyException(
                "The world changed since the grid-fit preview. Reload and preview it again.");
        var map = world.World.SourceMaps.SingleOrDefault(value => value.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source map was not found.");
        var fit = ReviewGridFit(world.World.SpatialTiling, map,
            request.Margins ?? new RasterGridMargins(),
            request.ObservedColumns, request.ObservedRows);
        if (fit.Status != "reported-match" || fit.Alignment is null)
            throw new InvalidOperationException(fit.Note);
        var saved = await service.RegisterComputedAffineAsync(
            overworldId, sourceMapId, owner, fit.Alignment,
            request.ExpectedVersion, cancellationToken);
        return Results.Ok(OverworldContract.From(saved));
    }
}
