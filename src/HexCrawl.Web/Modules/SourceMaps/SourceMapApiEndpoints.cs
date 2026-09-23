using System.Security.Claims;

namespace HexCrawl.Web.Api;

public static partial class SourceMapApiEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("", ListAsync);
        api.MapPost("", UploadAsync);
        api.MapPost("/wonderdraft/inspect", InspectWonderdraftAsync);
        api.MapPost("/{sourceMapId:guid}/wonderdraft/source", ImportWonderdraftSourceAsync);
        api.MapPost("/{sourceMapId:guid}/wonderdraft/candidates", PreviewWonderdraftCandidatesAsync);
        api.MapPost("/{sourceMapId:guid}/wonderdraft/import", ImportWonderdraftCandidatesAsync);
        api.MapPut("/{sourceMapId:guid}", UpdateMetadataAsync);
        api.MapPut("/{sourceMapId:guid}/registration", RegisterAsync);
        api.MapGet("/{sourceMapId:guid}/asset", GetAssetAsync);
        api.MapGet("/{sourceMapId:guid}/source-archive", GetSourceArchiveAsync);
        api.MapDelete("/{sourceMapId:guid}", DeleteAsync);
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException("An authenticated Tool Host or explicitly configured standalone development identity is required.");


}
