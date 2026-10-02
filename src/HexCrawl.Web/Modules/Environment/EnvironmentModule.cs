using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Web.Api;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Environment;

public sealed class EnvironmentModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "environment-context",
        DisplayName: "Environment context")
    {
        Dependencies = ["worlds", "expeditions"]
    };

    public void RegisterServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/overworlds/{overworldId:guid}/environment", GetWorldEnvironmentAsync);
        api.MapPut("/overworlds/{overworldId:guid}/environment", ReplaceWorldEnvironmentAsync);
        api.MapGet("/expeditions/{expeditionId:guid}/environment", GetExpeditionEnvironmentAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/environment", UpdateExpeditionEnvironmentAsync);
    }

    private static async Task<IResult> GetWorldEnvironmentAsync(
        Guid overworldId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var world = await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken);
        return Results.Ok(WorldEnvironmentContract.From(world));
    }

    private static async Task<IResult> ReplaceWorldEnvironmentAsync(
        Guid overworldId,
        ReplaceWorldEnvironmentRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var world = await service.ReplaceWorldEnvironmentAsync(
            overworldId, UserId(context), request.ToCommand(), cancellationToken);
        return Results.Ok(WorldEnvironmentContract.From(world));
    }

    private static async Task<IResult> GetExpeditionEnvironmentAsync(
        Guid expeditionId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.GetExpeditionAsync(expeditionId, owner, cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> UpdateExpeditionEnvironmentAsync(
        Guid expeditionId,
        UpdateExpeditionEnvironmentRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.UpdateExpeditionEnvironmentAsync(
            expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<EnvironmentWorkbenchContract> ContractAsync(
        StoredExpedition expedition,
        string ownerUserId,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        if (expedition.Context is WorldBoundCrawlSessionContext worldContext)
        {
            var world = await service.GetOverworldAsync(worldContext.WorldId, ownerUserId, cancellationToken);
            return EnvironmentWorkbenchContract.From(expedition, world.World);
        }
        return EnvironmentWorkbenchContract.From(expedition);
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
