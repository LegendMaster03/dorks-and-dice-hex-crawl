using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Expeditions;

public sealed class EncounterHandoffModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "encounter-handoff",
        DisplayName: "Encounter handoff")
    {
        Dependencies = ["expeditions"]
    };

    public void RegisterServices(IServiceCollection services) =>
        services.AddScoped<EncounterHandoffService>();

    public void MapEndpoints(RouteGroupBuilder api) =>
        api.MapPost("/expeditions/{expeditionId:guid}/encounter-handoff", CreateAsync);

    private static async Task<IResult> CreateAsync(
        Guid expeditionId,
        CreateEncounterHandoffCommand request,
        HttpContext context,
        EncounterHandoffService service,
        CancellationToken cancellationToken)
    {
        var handoff = await service.CreateAsync(
            expeditionId,
            UserId(context),
            request,
            cancellationToken);
        return Results.Ok(handoff);
    }

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException("Hex Crawl requires an authenticated user.");
}
