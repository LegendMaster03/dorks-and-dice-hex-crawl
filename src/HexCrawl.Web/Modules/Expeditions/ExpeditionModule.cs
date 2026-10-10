using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.World;
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
        Dependencies = ["worlds", "procedure-composer"]
    };

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<CrawlSessionContextResolver>();
        services.AddScoped<CrawlSessionService>();
        services.AddScoped<ExpeditionStartService>();
        services.AddScoped<ExpeditionWorkbenchService>();
        services.AddScoped<ExpeditionAssistantService>();
        services.AddScoped<ExpeditionPartyService>();
        services.AddScoped<ExpeditionEffectService>();
        services.AddScoped<ProcedureResolutionResolver>();
        services.AddScoped<ProcedureResolutionHelperService>();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/expeditions", ListAllExpeditionsAsync);
        api.MapPost("/expeditions", StartStandaloneSessionAsync);
        api.MapGet("/overworlds/{overworldId:guid}/expeditions", ListExpeditionsAsync);
        api.MapPost("/overworlds/{overworldId:guid}/expeditions", StartExpeditionAsync);
        api.MapGet("/expeditions/{expeditionId:guid}", GetExpeditionAsync);
        api.MapGet("/expeditions/{expeditionId:guid}/cell-topology", GetCellTopologyAsync);
        api.MapDelete("/expeditions/{expeditionId:guid}", DeleteExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/advance", AdvanceExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/encounters/{occurrenceId:guid}/resolve", ResolveEncounterAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/boundary-decision", ResolveBoundaryDecisionAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/course-intent", SetCourseIntentAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/reposition", RepositionExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/resolution-helper", ResolveProcedureInputsAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/discover", DiscoverAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/party", UpdatePartyAsync);
        api.MapGet("/expeditions/{expeditionId:guid}/effects", GetEffectsAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/consequences", ApplyConsequenceAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/effects/{effectId:guid}", UpsertEffectAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/effects/{effectId:guid}/recover", RecoverEffectAsync);
        api.MapDelete("/expeditions/{expeditionId:guid}/effects/{effectId:guid}", ClearEffectAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/consequences/{consequenceId:guid}/resolve", ResolvePendingConsequenceAsync);
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
        StartProcedureSessionRequest request,
        HttpContext context,
        ExpeditionStartService starter,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await starter.StartStandaloneAsync(
            owner,
            request.Name,
            request.ProcedureSelection(),
            request.Context.ToDomain(),
            request.StartHex,
            cancellationToken);
        return Results.Created(
            $"/api/expeditions/{expedition.Id:D}",
            await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> StartExpeditionAsync(
        Guid overworldId,
        StartProcedureExpeditionRequest request,
        HttpContext context,
        ExpeditionStartService starter,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await starter.StartWorldBoundAsync(
            overworldId,
            owner,
            request.Name,
            request.ProcedureSelection(),
            request.ResolvedPresentationKey,
            request.StartHex,
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

    private static async Task<IResult> GetCellTopologyAsync(
        Guid expeditionId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.GetExpeditionAsync(expeditionId, owner, cancellationToken);
        if (expedition.Runtime is not CellExpeditionState cell
            || expedition.Context is not WorldBoundCrawlSessionContext worldContext)
            throw new NotSupportedException("This expedition does not have a generalized cell traversal.");
        var world = await service.GetOverworldAsync(worldContext.WorldId, owner, cancellationToken);
        return Results.Ok(CellTraversalContextContract.From(world.World.SpatialTiling, cell));
    }

    private static async Task<IResult> DeleteExpeditionAsync(
        Guid expeditionId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        await service.DeleteExpeditionAsync(expeditionId, UserId(context), expectedVersion, cancellationToken);
        return Results.NoContent();
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
        var expedition = await workbench.AdvanceAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> ResolveEncounterAsync(
        Guid expeditionId,
        Guid occurrenceId,
        ResolveEncounterRequest request,
        HttpContext context,
        ExpeditionAssistantService assistants,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await assistants.ResolveEncounterAsync(
            expeditionId, owner, request.ToCommand(occurrenceId), cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> ResolveBoundaryDecisionAsync(
        Guid expeditionId,
        ResolveBoundaryDecisionRequest request,
        HttpContext context,
        ExpeditionWorkbenchService workbench,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await workbench.ResolveBoundaryDecisionAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> SetCourseIntentAsync(
        Guid expeditionId,
        SetExpeditionCourseIntentRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.SetExpeditionCourseIntentAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> RepositionExpeditionAsync(
        Guid expeditionId,
        RepositionExpeditionRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.RepositionExpeditionAsync(
            expeditionId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> ResolveProcedureInputsAsync(
        Guid expeditionId,
        ResolveProcedureInputsRequest request,
        HttpContext context,
        ProcedureResolutionHelperService helper,
        CancellationToken cancellationToken)
    {
        var result = await helper.ResolveAsync(expeditionId, UserId(context), request.ToCommand(), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> DiscoverAsync(
        Guid expeditionId,
        DiscoverSubjectRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await service.DiscoverAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
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
        var expedition = await parties.UpdateAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> GetEffectsAsync(
        Guid expeditionId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var expedition = await service.GetExpeditionAsync(expeditionId, UserId(context), cancellationToken);
        return Results.Ok(ExpeditionEffectStateContract.From(expedition));
    }

    private static async Task<IResult> ApplyConsequenceAsync(
        Guid expeditionId,
        ApplyExpeditionConsequenceRequest request,
        HttpContext context,
        ExpeditionEffectService effects,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await effects.ApplyConsequenceAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(new ExpeditionEffectOperationContract(
            await ContractAsync(result.Expedition, owner, service, cancellationToken),
            ExpeditionEffectStateContract.From(result.Expedition),
            result.Processing.Status,
            result.Processing.Detail));
    }

    private static async Task<IResult> UpsertEffectAsync(
        Guid expeditionId,
        Guid effectId,
        UpsertExpeditionEffectRequest request,
        HttpContext context,
        ExpeditionEffectService effects,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await effects.UpsertEffectAsync(
            expeditionId,
            owner,
            request.ToCommand(effectId),
            cancellationToken);
        return Results.Ok(new ExpeditionEffectOperationContract(
            await ContractAsync(expedition, owner, service, cancellationToken),
            ExpeditionEffectStateContract.From(expedition),
            ExpeditionConsequenceStatus.Applied,
            "Manual expedition effect state saved."));
    }

    private static async Task<IResult> RecoverEffectAsync(
        Guid expeditionId,
        Guid effectId,
        RecoverExpeditionEffectRequest request,
        HttpContext context,
        ExpeditionEffectService effects,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var result = await effects.RecoverAsync(
            expeditionId,
            effectId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(new ExpeditionEffectOperationContract(
            await ContractAsync(result.Expedition, owner, service, cancellationToken),
            ExpeditionEffectStateContract.From(result.Expedition),
            result.Recovery.Status,
            result.Recovery.Detail));
    }

    private static async Task<IResult> ClearEffectAsync(
        Guid expeditionId,
        Guid effectId,
        [Microsoft.AspNetCore.Mvc.FromBody] ClearExpeditionEffectRequest request,
        HttpContext context,
        ExpeditionEffectService effects,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await effects.ClearEffectAsync(
            expeditionId,
            effectId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(new ExpeditionEffectOperationContract(
            await ContractAsync(expedition, owner, service, cancellationToken),
            ExpeditionEffectStateContract.From(expedition),
            ExpeditionConsequenceStatus.Applied,
            "Manual expedition effect cleared."));
    }

    private static async Task<IResult> ResolvePendingConsequenceAsync(
        Guid expeditionId,
        Guid consequenceId,
        ResolvePendingConsequenceRequest request,
        HttpContext context,
        ExpeditionEffectService effects,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var expedition = await effects.ResolvePendingAsync(
            expeditionId,
            consequenceId,
            owner,
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(new ExpeditionEffectOperationContract(
            await ContractAsync(expedition, owner, service, cancellationToken),
            ExpeditionEffectStateContract.From(expedition),
            ExpeditionConsequenceStatus.Recorded,
            "Deferred consequence resolution recorded."));
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
        var expedition = await assistants.RecordTravelWatchAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
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
        var expedition = await assistants.RecordNonSpatialWatchAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
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
        var expedition = await assistants.RecordNavigationAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
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
        var expedition = await assistants.RecordEncounterCadenceAsync(expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<ExpeditionWorkbenchContract> ContractAsync(
        StoredExpedition expedition,
        string ownerUserId,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        OverworldDefinition? world = null;
        if (expedition.Context is WorldBoundCrawlSessionContext worldContext)
        {
            world = (await service.GetOverworldAsync(worldContext.WorldId, ownerUserId, cancellationToken)).World;
        }

        var context = EnvironmentContextResolver.Resolve(expedition, world);
        var evaluation = EnvironmentProcedureEvaluator.Evaluate(expedition, context);
        var movement = ExpeditionEffectMovementProjection.Compose(expedition, evaluation.MovementInput);
        return ExpeditionWorkbenchContract.From(expedition, world) with
        {
            MovementComposition = MovementCapabilityCompositionContract.From(movement)
        };
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
