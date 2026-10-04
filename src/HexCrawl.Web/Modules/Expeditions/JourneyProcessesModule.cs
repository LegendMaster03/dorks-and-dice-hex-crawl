using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Expeditions;

public sealed class JourneyProcessesModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "journey-processes",
        DisplayName: "Journey processes and challenges")
    {
        Dependencies = ["expeditions", "environment-context", "survival-resources"]
    };

    public void RegisterServices(IServiceCollection services) =>
        services.AddScoped<ExpeditionJourneyService>();

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/expeditions/{expeditionId:guid}/journeys", GetAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/journeys/processes", StartProcessAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/journeys/processes/{processId:guid}/resolve", ResolveProcessAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/journeys/processes/{processId:guid}/complete", CompleteProcessAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/journeys/processes/{processId:guid}/fail", FailProcessAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/journeys/processes/{processId:guid}/abandon", AbandonProcessAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/journeys/events", CreateEventOpportunityAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/journeys/events/{occurrenceId:guid}/resolve", ResolveEventAsync);
    }

    private static async Task<IResult> GetAsync(
        Guid expeditionId,
        HttpContext context,
        HexCrawlService core,
        CancellationToken cancellationToken)
    {
        var expedition = await core.GetExpeditionAsync(expeditionId, UserId(context), cancellationToken);
        return Results.Ok(ExpeditionJourneyStateContract.From(expedition));
    }

    private static async Task<IResult> StartProcessAsync(
        Guid expeditionId,
        StartJourneyProcessRequest request,
        HttpContext context,
        ExpeditionJourneyService journeys,
        CancellationToken cancellationToken)
    {
        var result = await journeys.StartProcessAsync(
            expeditionId,
            UserId(context),
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(JourneyOperationContract.From(result));
    }

    private static async Task<IResult> ResolveProcessAsync(
        Guid expeditionId,
        Guid processId,
        ResolveJourneyProcessRequest request,
        HttpContext context,
        ExpeditionJourneyService journeys,
        CancellationToken cancellationToken)
    {
        var result = await journeys.ResolveProcessAsync(
            expeditionId,
            UserId(context),
            request.ToCommand(processId),
            cancellationToken);
        return Results.Ok(JourneyOperationContract.From(result));
    }

    private static async Task<IResult> CompleteProcessAsync(
        Guid expeditionId,
        Guid processId,
        CloseJourneyProcessRequest request,
        HttpContext context,
        ExpeditionJourneyService journeys,
        CancellationToken cancellationToken)
    {
        var result = await journeys.CompleteProcessAsync(
            expeditionId,
            processId,
            UserId(context),
            request.ToCommand(JourneyProcessStatus.Completed),
            cancellationToken);
        return Results.Ok(JourneyOperationContract.From(result));
    }

    private static async Task<IResult> FailProcessAsync(
        Guid expeditionId,
        Guid processId,
        CloseJourneyProcessRequest request,
        HttpContext context,
        ExpeditionJourneyService journeys,
        CancellationToken cancellationToken)
    {
        var result = await journeys.FailProcessAsync(
            expeditionId,
            processId,
            UserId(context),
            request.ToCommand(JourneyProcessStatus.Failed),
            cancellationToken);
        return Results.Ok(JourneyOperationContract.From(result));
    }

    private static async Task<IResult> AbandonProcessAsync(
        Guid expeditionId,
        Guid processId,
        CloseJourneyProcessRequest request,
        HttpContext context,
        ExpeditionJourneyService journeys,
        CancellationToken cancellationToken)
    {
        var result = await journeys.AbandonProcessAsync(
            expeditionId,
            processId,
            UserId(context),
            request.ToCommand(JourneyProcessStatus.Abandoned),
            cancellationToken);
        return Results.Ok(JourneyOperationContract.From(result));
    }

    private static async Task<IResult> CreateEventOpportunityAsync(
        Guid expeditionId,
        CreateJourneyEventOpportunityRequest request,
        HttpContext context,
        ExpeditionJourneyService journeys,
        CancellationToken cancellationToken)
    {
        var result = await journeys.CreateEventOpportunityAsync(
            expeditionId,
            UserId(context),
            request.ToCommand(),
            cancellationToken);
        return Results.Ok(JourneyOperationContract.From(result));
    }

    private static async Task<IResult> ResolveEventAsync(
        Guid expeditionId,
        Guid occurrenceId,
        ResolveJourneyEventRequest request,
        HttpContext context,
        ExpeditionJourneyService journeys,
        CancellationToken cancellationToken)
    {
        var result = await journeys.ResolveEventAsync(
            expeditionId,
            UserId(context),
            request.ToCommand(occurrenceId),
            cancellationToken);
        return Results.Ok(JourneyOperationContract.From(result));
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}