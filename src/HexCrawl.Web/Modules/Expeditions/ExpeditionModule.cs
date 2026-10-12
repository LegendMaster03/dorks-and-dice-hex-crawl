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
        services.AddScoped<ExpeditionStartService>();
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
        // Spatial sessions use authoritative cell geometry.
        api.MapPost("/expeditions/cells", StartStandaloneCellSessionAsync);
        api.MapGet("/overworlds/{overworldId:guid}/expeditions", ListExpeditionsAsync);
        api.MapPost("/overworlds/{overworldId:guid}/cell-expeditions", StartCellExpeditionAsync);
        api.MapGet("/expeditions/{expeditionId:guid}", GetExpeditionAsync);
        api.MapGet("/expeditions/{expeditionId:guid}/cell-topology", GetCellTopologyAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/cells/advance", AdvanceCellWatchAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/cells/resolution-helper", ResolveCellProcedureInputsAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/cells/course", ResolveCellCourseAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/cells/reposition", RepositionCellExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/cells/encounters/{occurrenceId:guid}/resolve", ResolveCellEncounterAsync);
        api.MapDelete("/expeditions/{expeditionId:guid}", DeleteExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/discover", DiscoverAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/party", UpdatePartyAsync);
        api.MapGet("/expeditions/{expeditionId:guid}/effects", GetEffectsAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/consequences", ApplyConsequenceAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/effects/{effectId:guid}", UpsertEffectAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/effects/{effectId:guid}/recover", RecoverEffectAsync);
        api.MapDelete("/expeditions/{expeditionId:guid}/effects/{effectId:guid}", ClearEffectAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/consequences/{consequenceId:guid}/resolve", ResolvePendingConsequenceAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/assistants/watch", RecordNonSpatialWatchAssistantAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/nonspatial/encounters", RecordNonSpatialEncounterAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/nonspatial/encounters/{occurrenceId:guid}/resolve", ResolveNonSpatialEncounterAsync);
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
        if (request.Context.ToDomain() is not NonSpatialCrawlSessionContext nonSpatial)
            throw new ArgumentException("This endpoint starts nonspatial sessions only.");
        var owner = UserId(context);
        var expedition = await starter.StartNonSpatialAsync(
            owner, request.Name, request.ProcedureSelection(), nonSpatial, cancellationToken);
        return Results.Created(
            $"/api/expeditions/{expedition.Id:D}",
            await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> StartStandaloneCellSessionAsync(
        StartProcedureSessionRequest request,
        HttpContext context,
        ExpeditionStartService starter,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        if (request.Context.ToDomain() is not AbstractHexCrawlSessionContext mapless)
            throw new ArgumentException("Generalized mapless starts require an abstract hex context.");
        if (request.StartHex is not { } startHex)
            throw new ArgumentException("Generalized mapless starts require a starting hex.");
        var owner = UserId(context);
        var expedition = await starter.StartStandaloneCellsAsync(
            owner, request.Name, request.ProcedureSelection(), mapless,
            startHex, cancellationToken);
        return Results.Created(
            $"/api/expeditions/{expedition.Id:D}",
            await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> StartCellExpeditionAsync(
        Guid overworldId,
        StartCellExpeditionRequest request,
        HttpContext context,
        ExpeditionStartService starter,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var selection = request.Selection();
        var owner = UserId(context);
        var expedition = await starter.StartWorldBoundCellsAsync(
            overworldId, owner, request.Name, selection,
            request.StartCell, request.PresentationKey, cancellationToken);
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
        if (expedition.Runtime is not CellExpeditionState cell)
            throw new NotSupportedException("This expedition does not have a generalized cell traversal.");
        var geometry = await service.ResolveCellGeometryAsync(expedition, owner, cancellationToken);
        return Results.Ok(CellTraversalContextContract.From(geometry.Tiling, cell));
    }

    private static async Task<IResult> ResolveCellProcedureInputsAsync(
        Guid expeditionId,
        ResolveCellProcedureInputsRequest request,
        HttpContext context,
        ProcedureResolutionHelperService helper,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var result = await helper.ResolveCellsAsync(
            expeditionId, UserId(context), request.ToCommand(), cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> AdvanceCellWatchAsync(
        Guid expeditionId,
        AdvanceCellWatchRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var owner = UserId(context);
        var updated = await service.AdvanceCellExpeditionAsync(
            expeditionId, owner, request.ExpectedVersion,
            request.Plan, request.Inputs, cancellationToken,
            request.GeneratedProcedureResolutionId);
        return Results.Ok(await ContractAsync(updated, owner, service, cancellationToken));
    }

    private static async Task<IResult> ResolveCellCourseAsync(
        Guid expeditionId,
        ResolveCellCourseRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var owner = UserId(context);
        var updated = await service.ResolveCellExpeditionCourseAsync(
            expeditionId, owner, request.ExpectedVersion,
            request.Decision, cancellationToken);
        return Results.Ok(await ContractAsync(updated, owner, service, cancellationToken));
    }

    private static async Task<IResult> RepositionCellExpeditionAsync(
        Guid expeditionId,
        RepositionCellExpeditionRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var owner = UserId(context);
        var updated = await service.RepositionCellExpeditionAsync(
            expeditionId, owner, request.ExpectedVersion,
            request.TargetCell, request.Position!.Value, request.Note,
            cancellationToken);
        return Results.Ok(await ContractAsync(updated, owner, service, cancellationToken));
    }

    private static async Task<IResult> ResolveCellEncounterAsync(
        Guid expeditionId,
        Guid occurrenceId,
        ResolveCellEncounterRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var owner = UserId(context);
        var updated = await service.ResolveCellExpeditionEncounterAsync(
            expeditionId, owner, request.ExpectedVersion,
            occurrenceId, request.ResultNote, cancellationToken);
        return Results.Ok(await ContractAsync(updated, owner, service, cancellationToken));
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

    private static async Task<IResult> RecordNonSpatialEncounterAsync(
        Guid expeditionId,
        RecordNonSpatialEncounterRequest request,
        HttpContext context,
        ExpeditionAssistantService assistants,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var owner = UserId(context);
        var loaded = await service.GetExpeditionAsync(expeditionId, owner, cancellationToken);
        if (loaded.Runtime is not NonSpatialSessionState)
            throw new InvalidOperationException("Nonspatial encounter recording requires a nonspatial session.");
        var expedition = await assistants.RecordEncounterCadenceAsync(
            expeditionId, owner, request.ToCommand(), cancellationToken);
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> ResolveNonSpatialEncounterAsync(
        Guid expeditionId,
        Guid occurrenceId,
        ResolveNonSpatialEncounterRequest request,
        HttpContext context,
        ExpeditionAssistantService assistants,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        request.Validate();
        var owner = UserId(context);
        var loaded = await service.GetExpeditionAsync(expeditionId, owner, cancellationToken);
        if (loaded.Runtime is not NonSpatialSessionState)
            throw new InvalidOperationException("Nonspatial encounter resolution requires a nonspatial session.");
        var expedition = await assistants.ResolveEncounterAsync(
            expeditionId, owner, request.ToCommand(occurrenceId), cancellationToken);
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
