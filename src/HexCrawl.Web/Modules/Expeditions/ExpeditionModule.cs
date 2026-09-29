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
        api.MapDelete("/expeditions/{expeditionId:guid}", DeleteExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/advance", AdvanceExpeditionAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/resolution-helper", ResolveProcedureInputsAsync);
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
        if (LegacyCreationConflict(request.ProcedureKey) is { } conflict)
        {
            return conflict;
        }

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
        if (LegacyCreationConflict(request.ProcedureKey) is { } conflict)
        {
            return conflict;
        }

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
        if (LegacyRepresentationConflict(expedition) is { } conflict)
        {
            return conflict;
        }
        return Results.Ok(await ContractAsync(expedition, owner, service, cancellationToken));
    }

    private static async Task<IResult> DeleteExpeditionAsync(
        Guid expeditionId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        await service.DeleteExpeditionAsync(
            expeditionId,
            UserId(context),
            expectedVersion,
            cancellationToken);
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
        if (await LegacyMutationConflictAsync(expeditionId, owner, service, cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var expedition = await workbench.AdvanceAsync(
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
        var result = await helper.ResolveAsync(
            expeditionId,
            UserId(context),
            request.ToCommand(),
            cancellationToken);
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
        if (await LegacyMutationConflictAsync(expeditionId, owner, service, cancellationToken) is { } conflict)
        {
            return conflict;
        }

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
        if (await LegacyMutationConflictAsync(expeditionId, owner, service, cancellationToken) is { } conflict)
        {
            return conflict;
        }

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
        if (await LegacyMutationConflictAsync(expeditionId, owner, service, cancellationToken) is { } conflict)
        {
            return conflict;
        }

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
        if (await LegacyMutationConflictAsync(expeditionId, owner, service, cancellationToken) is { } conflict)
        {
            return conflict;
        }

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
        if (await LegacyMutationConflictAsync(expeditionId, owner, service, cancellationToken) is { } conflict)
        {
            return conflict;
        }

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
        if (await LegacyMutationConflictAsync(expeditionId, owner, service, cancellationToken) is { } conflict)
        {
            return conflict;
        }

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

    private static IResult? LegacyCreationConflict(string procedureKey)
    {
        var materialized = CrawlProcedureCatalog.Resolve(procedureKey).MaterializeGeneric();
        return materialized.CompatibilityProfile is null
            ? Results.Conflict(new
            {
                error = $"Procedure preset '{procedureKey}' is generic-only and can not be represented by the current legacy HTTP workbench contract. Use an application-level generic procedure workflow until the Phase 4 procedure surface is available."
            })
            : null;
    }

    private static async Task<IResult?> LegacyMutationConflictAsync(
        Guid expeditionId,
        string ownerUserId,
        HexCrawlService service,
        CancellationToken cancellationToken)
    {
        var expedition = await service.GetExpeditionAsync(expeditionId, ownerUserId, cancellationToken);
        return LegacyRepresentationConflict(expedition);
    }

    private static IResult? LegacyRepresentationConflict(StoredExpedition expedition) =>
        expedition.CompatibilityProfile is null
            ? Results.Conflict(new
            {
                error = $"Expedition '{expedition.Id:D}' uses a generic-only CampaignProcedure and can not be represented by the current legacy HTTP workbench contract. The persisted generic snapshot remains authoritative."
            })
            : null;

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
