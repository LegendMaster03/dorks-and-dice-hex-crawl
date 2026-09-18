using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Api;

public static class PersistentApiEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/runtime/profiles", () => Results.Ok(
            CrawlProcedureCatalog.All.Select(RuntimeProfileContract.From).ToArray()));
        api.MapGet("/presentation/presets", () => Results.Ok(
            MapPresentationPolicyCatalog.All.Select(PresentationProfileContract.From).ToArray()));

        api.MapGet("/overworlds", ListOverworldsAsync);
        api.MapPost("/overworlds", CreateOverworldAsync);
        api.MapGet("/overworlds/{overworldId:guid}", GetOverworldAsync);
        api.MapPut("/overworlds/{overworldId:guid}", UpdateOverworldAsync);

        api.MapPost("/overworlds/{overworldId:guid}/locations", CreateLocationAsync);
        api.MapPut("/overworlds/{overworldId:guid}/locations/{locationId:guid}", UpdateLocationAsync);
        api.MapDelete("/overworlds/{overworldId:guid}/locations/{locationId:guid}", DeleteLocationAsync);

        api.MapPost("/overworlds/{overworldId:guid}/features", CreateFeatureAsync);
        api.MapPut("/overworlds/{overworldId:guid}/features/{featureId:guid}", UpdateFeatureAsync);
        api.MapDelete("/overworlds/{overworldId:guid}/features/{featureId:guid}", DeleteFeatureAsync);

        api.MapGet("/expeditions", ListAllExpeditionsAsync);
        api.MapPost("/expeditions", StartStandaloneSessionAsync);
        api.MapGet("/overworlds/{overworldId:guid}/expeditions", ListExpeditionsAsync);
        api.MapPost("/overworlds/{overworldId:guid}/expeditions", StartExpeditionAsync);
        api.MapGet("/expeditions/{expeditionId:guid}", GetExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/advance", AdvanceExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/discover", DiscoverAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/travel", RecordTravelAssistantAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/watch", RecordNonSpatialWatchAssistantAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/navigation", RecordNavigationAssistantAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/encounters", RecordEncounterAssistantAsync);
    }

    private static async Task<IResult> ListOverworldsAsync(
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListOverworldsAsync(UserId(context), cancellationToken));

    private static async Task<IResult> CreateOverworldAsync(
        CreateOverworldRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Created(
            "/api/overworlds",
            OverworldContract.From(await service.CreateOverworldAsync(UserId(context), request.ToCommand(), cancellationToken)));

    private static async Task<IResult> GetOverworldAsync(
        Guid overworldId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.GetOverworldAsync(overworldId, UserId(context), cancellationToken)));

    private static async Task<IResult> UpdateOverworldAsync(
        Guid overworldId,
        UpdateOverworldRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateOverworldAsync(
            overworldId, UserId(context), request.ToCommand(), cancellationToken)));

    private static async Task<IResult> CreateLocationAsync(
        Guid overworldId,
        LocationMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.CreateLocationAsync(
            overworldId, UserId(context), request.ToCreateCommand(), cancellationToken)));

    private static async Task<IResult> UpdateLocationAsync(
        Guid overworldId,
        Guid locationId,
        LocationMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateLocationAsync(
            overworldId, locationId, UserId(context), request.ToUpdateCommand(), cancellationToken)));

    private static async Task<IResult> DeleteLocationAsync(
        Guid overworldId,
        Guid locationId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.DeleteLocationAsync(
            overworldId, locationId, UserId(context), expectedVersion, cancellationToken)));

    private static async Task<IResult> CreateFeatureAsync(
        Guid overworldId,
        FeatureMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.CreateFeatureAsync(
            overworldId, UserId(context), request.ToCreateCommand(), cancellationToken)));

    private static async Task<IResult> UpdateFeatureAsync(
        Guid overworldId,
        Guid featureId,
        FeatureMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateFeatureAsync(
            overworldId, featureId, UserId(context), request.ToUpdateCommand(), cancellationToken)));

    private static async Task<IResult> DeleteFeatureAsync(
        Guid overworldId,
        Guid featureId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.DeleteFeatureAsync(
            overworldId, featureId, UserId(context), expectedVersion, cancellationToken)));

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
            overworldId, owner, request.ToCommand(), cancellationToken);
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
            expeditionId, owner, request.ToCommand(), cancellationToken);
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
            expeditionId, owner, request.ToCommand(), cancellationToken);
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
            expeditionId, owner, request.ToCommand(), cancellationToken);
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
            expeditionId, owner, request.ToCommand(), cancellationToken);
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
            expeditionId, owner, request.ToCommand(), cancellationToken);
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
            expeditionId, owner, request.ToCommand(), cancellationToken);
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
            var world = await service.GetOverworldAsync(worldContext.WorldId, ownerUserId, cancellationToken);
            return ExpeditionWorkbenchContract.From(expedition, world.World);
        }

        return ExpeditionWorkbenchContract.From(expedition);
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException("An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
