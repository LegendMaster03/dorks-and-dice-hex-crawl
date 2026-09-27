using HexCrawl.Application;
using HexCrawl.Application.Assets;
using HexCrawl.Infrastructure.Wonderdraft;
using Microsoft.Extensions.Options;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    private static async Task<IResult> GetStoredWonderdraftAlignmentContextAsync(
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
        if (map.ImportProvenance?.SourceType.Equals("wonderdraft", StringComparison.OrdinalIgnoreCase) != true
            || map.SourceArchive is null)
        {
            return Results.NotFound(new { error = "This source map has no retained Wonderdraft project context." });
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

        var summary = document.Summary;
        var relationship = WonderdraftRasterRelationship.Analyze(
            summary.PixelWidth,
            summary.PixelHeight,
            map.PixelWidth,
            map.PixelHeight);
        return Results.Ok(new
        {
            summary = InspectionContract(summary),
            rasterRelationship = relationship.Kind.ToString(),
            relationship.CanMapProjectCoordinates,
            relationship.UniformScale,
            relationship.ScaleX,
            relationship.ScaleY,
            relationship.WidthResidualPixels,
            relationship.HeightResidualPixels,
            relationship.Explanation
        });
    }
}
