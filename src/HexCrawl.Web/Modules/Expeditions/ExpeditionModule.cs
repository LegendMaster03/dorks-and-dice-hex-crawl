using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Web.Api;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Expeditions;

public sealed class ExpeditionModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "expeditions",
        DisplayName: "Expedition runtime and assistants")
    {
        Dependencies = ["worlds"]
    };

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<CrawlSessionContextResolver>();
        services.AddScoped<CrawlSessionService>();
        services.AddScoped<ExpeditionWorkbenchService>();
        services.AddScoped<ExpeditionAssistantService>();
        services.AddScoped<ExpeditionPartyService>();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/expeditions", ListAllExpeditionsAsync);
        api.MapPost("/expeditions", StartStandaloneSessionAsync);
        api.MapGet("/overworlds/{overworldId:guid}/expeditions", ListExpeditionsAsync);
        api.MapPost("/overworlds/{overworldId:guid}/expeditions", StartExpeditionAsync);
        api.MapGet("/expeditions/{expeditionId:guid}", GetExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/advance", AdvanceExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/discover", DiscoverAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/party", UpdatePartyAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/travel", RecordTravelAssistantAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/watch", RecordNonSpatialWatchAssistantAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/navigation", RecordNavigationAssistantAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/encounters", RecordEncounterAssistantAsync);
    }

    private static async Task<IResult> ListAllExpeditionsAsync(
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var expeditions = await service.ListExpeditionsAsync(UserId(context), cancellationToken);
        return Results.Ok(expeditions.Select(ExpeditionSummaryContract.From).ToArray());
    }

    private static async Task<IResult> ListExpeditionsAsync(
        Guid overworldId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var expeditions = await service.ListExpeditionsAsync(overworldId, UserId(context), cancellationToken);
        return Results.Ok(expeditions.Select(ExpeditionSummaryContract.From).ToArray());
    }

    private static async Task<IResult> StartStandaloneSessionAsync(
        StartStandaloneCrawlSessionRequest request,
        HttpContext context,
        CrawlSessionService sessions,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await sessions.StartAsync(owner, request.ToCommand(), cancellationToken);
        return Results.Created(
            $"/api/expeditions/{expedition.Id:D}",
            await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> StartExpeditionAsync(
        Guid overworldId,
        StartExpeditionWorkbenchRequest request,
        HttpContext context,
        ExpeditionWorkbenchService workbench,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await workbench.StartAsync(
            overworldId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Created(
            $"/api/expeditions/{expedition.Id:D}",
            await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> GetExpeditionAsync(
        Guid expeditionId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.GetExpeditionAsync(expeditionId, owner, cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> AdvanceExpeditionAsync(
        Guid expeditionId,
        AdvanceExpeditionWorkbenchRequest request,
        HttpContext context,
        ExpeditionWorkbenchService workbench,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await workbench.AdvanceAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> DiscoverAsync(
        Guid expeditionId,
        DiscoverSubjectRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.DiscoverAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> UpdatePartyAsync(
        Guid expeditionId,
        UpdateExpeditionPartyRequest request,
        HttpContext context,
        ExpeditionPartyService parties,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await parties.UpdateAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> RecordTravelAssistantAsync(
        Guid expeditionId,
        TravelWatchAssistantRequest request,
        HttpContext context,
        ExpeditionAssistantService assistants,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await assistants.RecordTravelWatchAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> RecordNonSpatialWatchAssistantAsync(
        Guid expeditionId,
        NonSpatialWatchAssistantRequest request,
        HttpContext context,
        ExpeditionAssistantService assistants,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await assistants.RecordNonSpatialWatchAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> RecordNavigationAssistantAsync(
        Guid expeditionId,
        NavigationAssistantRequest request,
        HttpContext context,
        ExpeditionAssistantService assistants,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await assistants.RecordNavigationAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> RecordEncounterAssistantAsync(
        Guid expeditionId,
        EncounterCadenceAssistantRequest request,
        HttpContext context,
        ExpeditionAssistantService assistants,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await assistants.RecordEncounterCadenceAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<ExpeditionWorkbenchContract> ContractAsync(
        StoredExpedition expedition,
        string ownerUserId,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        if (expedition.Context is WorldBoundCrawlSessionContext worldContext)
        {
            var world = await service.GetOverworldAsync(
                worldContext.WorldId,
                ownerUserId,
                cancellationToken);
            return ExpeditionWorkbenchContract.From(expedition, world.World);
        }

        return ExpeditionWorkbenchContract.From(expedition);
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
