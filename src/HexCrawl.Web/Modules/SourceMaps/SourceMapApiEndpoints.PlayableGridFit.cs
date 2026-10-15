using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    public sealed record ConfirmPlayableGridFitRequest(long ExpectedVersion);

    /// <summary>
    /// A computed suggestion, not a detected tile count: only the owner may
    /// preview it and the client must ask for explicit confirmation.
    /// </summary>
    private static async Task<IResult> PreviewPlayableGridFitAsync(
        Guid overworldId,
        Guid sourceMapId,
        HttpContext context,
        SourceMapApplicationService service,
        CancellationToken cancellationToken)
    {
        var world = await service.GetOverworldAsync(
            overworldId, UserId(context), cancellationToken);
        var map = world.World.SourceMaps.SingleOrDefault(value => value.Id == sourceMapId)
            ?? throw new HexCrawlNotFoundException("Source map was not found.");
        var fit = PlayableGridRasterFit.Calculate(
            world.World.SpatialTiling, map.PixelWidth, map.PixelHeight);
        return Results.Ok(new
        {
            alignment = fit,
            worldVersion = world.Version,
            playableBounds = world.World.SpatialTiling.PlayableBounds,
            note = "This fit assumes the source image contains precisely the stated grid from edge to edge, " +
                "without margins, crops, rotation or additional neighboring cells. Preview all four edges before accepting."
        });
    }

    /// <summary>
    /// Recompute from the authoritative current world and source dimensions;
    /// do not accept an arbitrary client-supplied transform.
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
        var fit = PlayableGridRasterFit.Calculate(
            world.World.SpatialTiling, map.PixelWidth, map.PixelHeight);
        var saved = await service.RegisterComputedAffineAsync(
            overworldId, sourceMapId, owner, fit, request.ExpectedVersion, cancellationToken);
        return Results.Ok(OverworldContract.From(saved));
    }
}
