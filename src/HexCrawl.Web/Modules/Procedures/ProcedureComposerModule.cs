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
        services.AddScoped<CampaignProcedureService>();
        services.AddScoped<ProcedureComposerService>();
        services.AddScoped<ProcedureCanonicalJsonService>();
        services.AddScoped<ProcedureReferenceService>();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapPost("/procedures/composer/draft", ComposeDraftAsync);
        api.MapPost("/procedures/composer/canonical/draft", ComposeCanonicalDraftAsync);
        api.MapPost("/procedures/composer/canonical/validate", ValidateCanonicalAsync);
        api.MapPost("/procedures", CreateProcedureAsync);
        api.MapPost("/procedures/canonical", CreateCanonicalProcedureAsync);
        api.MapGet("/procedures/{procedureId:guid}", GetLatestProcedureAsync);
        api.MapGet("/procedures/{procedureId:guid}/reference", GetLatestProcedureReferenceAsync);
        api.MapGet("/procedures/{procedureId:guid}/revisions", ListProcedureRevisionsAsync);
        api.MapGet("/procedures/{procedureId:guid}/revisions/{revision:int}", GetProcedureRevisionAsync);
        api.MapGet("/procedures/{procedureId:guid}/revisions/{revision:int}/reference", GetProcedureRevisionReferenceAsync);
        api.MapPost("/procedures/{procedureId:guid}/revisions", CreateProcedureRevisionAsync);
        api.MapPost("/procedures/{procedureId:guid}/canonical/revisions", CreateCanonicalProcedureRevisionAsync);
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
            ModuleSelections(request.ModuleSelections),
            Overrides(request.Overrides),
            cancellationToken);
        return Results.Ok(ProcedureComposerContract.From(draft));
    }

    private static async Task<IResult> ComposeCanonicalDraftAsync(
        ProcedureCanonicalDraftRequest request,
        HttpContext context,
        ProcedureCanonicalJsonService service,
        CancellationToken cancellationToken)
    {
        var canonicalJson = await service.CreateDraftJsonAsync(
            UserId(context),
            request.PresetKey,
            request.ProcedureId,
            request.Revision,
            ModuleSelections(request.ModuleSelections),
            Overrides(request.Overrides),
            cancellationToken);
        var validation = service.Validate(canonicalJson);
        if (!validation.IsValid || validation.Procedure is null)
        {
            throw new InvalidOperationException(validation.Error ?? "Canonical procedure draft could not be validated.");
        }

        return Results.Ok(new ProcedureCanonicalJsonContract(
            validation.Procedure.ProcedureId,
            validation.Procedure.Revision,
            canonicalJson));
    }

    private static IResult ValidateCanonicalAsync(
        ProcedureCanonicalValidationRequest request,
        ProcedureCanonicalJsonService service)
    {
        var validation = service.Validate(request.CanonicalJson);
        return Results.Ok(new ProcedureCanonicalValidationContract(
            validation.IsValid,
            validation.Procedure?.ProcedureId,
            validation.Procedure?.Revision,
            validation.Error,
            validation.LineNumber,
            validation.BytePositionInLine));
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
            ModuleSelections(request.ModuleSelections),
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

    private static async Task<IResult> CreateCanonicalProcedureAsync(
        ProcedureCanonicalCreateRequest request,
        HttpContext context,
        ProcedureCanonicalJsonService canonical,
        ProcedureComposerService composer,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var stored = await canonical.CreateAsync(
            owner,
            request.CanonicalJson,
            request.PresetKey,
            request.CampaignId,
            cancellationToken);
        var draft = await composer.CreateDraftAsync(
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

    private static async Task<IResult> GetLatestProcedureReferenceAsync(
        Guid procedureId,
        HttpContext context,
        ProcedureReferenceService service,
        CancellationToken cancellationToken)
    {
        var reference = await service.GetAsync(
            UserId(context),
            procedureId,
            null,
            cancellationToken);
        return Results.Ok(ProcedureReferenceContract.From(reference));
    }

    private static async Task<IResult> GetProcedureRevisionReferenceAsync(
        Guid procedureId,
        int revision,
        HttpContext context,
        ProcedureReferenceService service,
        CancellationToken cancellationToken)
    {
        var reference = await service.GetAsync(
            UserId(context),
            procedureId,
            revision,
            cancellationToken);
        return Results.Ok(ProcedureReferenceContract.From(reference));
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
            ModuleSelections(request.ModuleSelections),
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

    private static async Task<IResult> CreateCanonicalProcedureRevisionAsync(
        Guid procedureId,
        ProcedureCanonicalRevisionRequest request,
        HttpContext context,
        ProcedureCanonicalJsonService canonical,
        ProcedureComposerService composer,
        CancellationToken cancellationToken)
    {
        var owner = UserId(context);
        var stored = await canonical.CreateRevisionAsync(
            owner,
            procedureId,
            request.ExpectedRevision,
            request.CanonicalJson,
            cancellationToken);
        var draft = await composer.CreateDraftAsync(
            owner,
            null,
            stored.ProcedureId,
            stored.Revision,
            [],
            cancellationToken);
        return Results.Ok(ProcedureComposerContract.From(draft));
    }

    private static IReadOnlyList<ProcedureModuleSelection> ModuleSelections(
        IReadOnlyList<ProcedureComposerModuleSelectionRequest>? values) =>
        values?.Select(value => value.ToDomain()).ToArray()
        ?? [];

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