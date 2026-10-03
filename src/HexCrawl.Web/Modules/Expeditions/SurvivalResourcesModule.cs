using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.World;
using HexCrawl.Web.Api;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Expeditions;

public sealed class SurvivalResourcesModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "survival-resources",
        DisplayName: "Expedition survival and resources")
    {
        Dependencies = ["expeditions", "environment"]
    };

    public void RegisterServices(IServiceCollection services) => services.AddScoped<ExpeditionSurvivalService>();

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/expeditions/{expeditionId:guid}/survival", GetAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/survival/resources/{resourceId:guid}", UpsertResourceAsync);
        api.MapDelete("/expeditions/{expeditionId:guid}/survival/resources/{resourceId:guid}", RemoveResourceAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/resources/pending/{consequenceId:guid}/apply", ApplyPendingResourceAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/consumption", ResolveConsumptionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/foraging", ResolveForagingAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/forced-travel/usage", RecordForcedTravelUsageAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/forced-travel/check", ResolveForcedTravelCheckAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/forced-travel/reset", ResetForcedTravelAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/exposure", ResolveExposureAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/camp", ResolveCampAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/survival/rest-recovery", RecoverFromRestAsync);
    }

    private static async Task<IResult> GetAsync(
        Guid expeditionId,
        HttpContext context,
        HexCrawlService core,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await core.GetExpeditionAsync(expeditionId, owner, cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, core, cancellationToken));
    }

    private static async Task<IResult> UpsertResourceAsync(
        Guid expeditionId,
        Guid resourceId,
        UpsertResourceRequest request,
        HttpContext context,
        ExpeditionSurvivalService survival,
        HexCrawlService core,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.UpsertResourceAsync(expeditionId, owner, request.ToCommand(resourceId), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> RemoveResourceAsync(
        Guid expeditionId,
        Guid resourceId,
        [Microsoft.AspNetCore.Mvc.FromBody] RemoveResourceRequest request,
        HttpContext context,
        ExpeditionSurvivalService survival,
        HexCrawlService core,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.RemoveResourceAsync(expeditionId, resourceId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> ApplyPendingResourceAsync(
        Guid expeditionId,
        Guid consequenceId,
        ApplyPendingResourceRequest request,
        HttpContext context,
        ExpeditionSurvivalService survival,
        HexCrawlService core,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.ApplyPendingResourceConsequenceAsync(
            expeditionId, consequenceId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> ResolveConsumptionAsync(
        Guid expeditionId, ResolveConsumptionRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.ResolveConsumptionAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> ResolveForagingAsync(
        Guid expeditionId, ResolveForagingRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.ResolveForagingAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> RecordForcedTravelUsageAsync(
        Guid expeditionId, RecordForcedTravelUsageRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.RecordForcedTravelUsageAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> ResolveForcedTravelCheckAsync(
        Guid expeditionId, ResolveForcedTravelCheckRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.ResolveForcedTravelCheckAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> ResetForcedTravelAsync(
        Guid expeditionId, ResetForcedTravelRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.ResetForcedTravelAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> ResolveExposureAsync(
        Guid expeditionId, ResolveExposureRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.ResolveExposureAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> ResolveCampAsync(
        Guid expeditionId, ResolveCampRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.ResolveCampAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<IResult> RecoverFromRestAsync(
        Guid expeditionId, RecoverFromRestRequest request, HttpContext context,
        ExpeditionSurvivalService survival, HexCrawlService core, CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await survival.RecoverFromResolvedRestAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await OperationAsync(result, owner, core, cancellationToken));
    }

    private static async Task<SurvivalOperationContract> OperationAsync(
        SurvivalOperationResult result,
        string owner,
        HexCrawlService core,
        CancellationToken cancellationToken) => new(
            result.Expedition.Version,
            result.Status,
            result.Detail,
            result.ConsequenceId,
            await ContractAsync(result.Expedition, owner, core, cancellationToken));

    private static async Task<SurvivalResourcesContract> ContractAsync(
        StoredExpedition expedition,
        string owner,
        HexCrawlService core,
        CancellationToken cancellationToken)
    {
        OverworldDefinition? world = null;
        if (expedition.Context is WorldBoundCrawlSessionContext worldContext)
        {
            world = (await core.GetOverworldAsync(worldContext.WorldId, owner, cancellationToken)).World;
        }
        var environment = EnvironmentContextResolver.Resolve(expedition, world);
        return SurvivalResourcesContract.From(expedition, environment);
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
