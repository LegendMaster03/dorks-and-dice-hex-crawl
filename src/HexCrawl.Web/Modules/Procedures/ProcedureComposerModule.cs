using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Procedures;

public sealed class ProcedureComposerModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "procedure-composer",
        DisplayName: "Campaign procedure composer");

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ProcedureComposerService>();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapPost("/procedures/composer/draft", ComposeDraftAsync);
        api.MapPost("/procedures", CreateProcedureAsync);
        api.MapGet("/procedures/{procedureId:guid}", GetLatestProcedureAsync);
        api.MapGet("/procedures/{procedureId:guid}/revisions", ListProcedureRevisionsAsync);
        api.MapGet("/procedures/{procedureId:guid}/revisions/{revision:int}", GetProcedureRevisionAsync);
        api.MapPost("/procedures/{procedureId:guid}/revisions", CreateProcedureRevisionAsync);
    }

    private static async Task<IResult> ComposeDraftAsync(
        ProcedureComposerDraftRequest request,
        HttpContext context,
        ProcedureComposerService service,
        CancellationToken cancellationToken)
    {
        var draft = await service.CreateDraftAsync(
            UserId(context),
            request.PresetKey,
            request.ProcedureId,
            request.Revision,
            Overrides(request.Overrides),
            cancellationToken);
        return Results.Ok(ProcedureComposerContract.From(draft));
    }

    private static async Task<IResult> CreateProcedureAsync(
        ProcedureComposerCreateRequest request,
        HttpContext context,
        ProcedureComposerService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var stored = await service.CreateAsync(
            owner,
            request.PresetKey,
            Overrides(request.Overrides),
            request.CampaignId,
            cancellationToken);
        var draft = await service.CreateDraftAsync(
            owner,
            null,
            stored.ProcedureId,
            stored.Revision,
            [],
            cancellationToken);
        return Results.Created(
            $"/api/procedures/{stored.ProcedureId:D}/revisions/{stored.Revision}",
            ProcedureComposerContract.From(draft));
    }

    private static async Task<IResult> GetLatestProcedureAsync(
        Guid procedureId,
        HttpContext context,
        ProcedureComposerService service,
        CancellationToken cancellationToken)
    {
        var draft = await service.CreateDraftAsync(
            UserId(context),
            null,
            procedureId,
            null,
            [],
            cancellationToken);
        return Results.Ok(ProcedureComposerContract.From(draft));
    }

    private static async Task<IResult> GetProcedureRevisionAsync(
        Guid procedureId,
        int revision,
        HttpContext context,
        ProcedureComposerService service,
        CancellationToken cancellationToken)
    {
        var draft = await service.CreateDraftAsync(
            UserId(context),
            null,
            procedureId,
            revision,
            [],
            cancellationToken);
        return Results.Ok(ProcedureComposerContract.From(draft));
    }

    private static async Task<IResult> ListProcedureRevisionsAsync(
        Guid procedureId,
        HttpContext context,
        ProcedureComposerService service,
        CancellationToken cancellationToken)
    {
        var revisions = await service.ListRevisionsAsync(UserId(context), procedureId, cancellationToken);
        return Results.Ok(revisions.Select(ProcedureRevisionSummaryContract.From).ToArray());
    }

    private static async Task<IResult> CreateProcedureRevisionAsync(
        Guid procedureId,
        ProcedureComposerRevisionRequest request,
        HttpContext context,
        ProcedureComposerService service,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var stored = await service.CreateRevisionAsync(
            owner,
            procedureId,
            request.ExpectedRevision,
            Overrides(request.Overrides),
            cancellationToken);
        var draft = await service.CreateDraftAsync(
            owner,
            null,
            stored.ProcedureId,
            stored.Revision,
            [],
            cancellationToken);
        return Results.Ok(ProcedureComposerContract.From(draft));
    }

    private static IReadOnlyList<HexCrawl.Domain.Procedure.CampaignProcedureOverride> Overrides(
        IReadOnlyList<ProcedureComposerOverrideRequest>? values) =>
        values?.Select(value => value.ToDomain()).ToArray()
        ?? [];

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
