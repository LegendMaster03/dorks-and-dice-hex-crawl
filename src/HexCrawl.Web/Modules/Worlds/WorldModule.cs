using System.Security.Claims;
using HexCrawl.Application;
using HexCrawl.Web.Api;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.Worlds;

public sealed class WorldModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "worlds",
        DisplayName: "Overworld authoring");

    public void RegisterServices(IServiceCollection services) =>
        services.AddScoped<HexCrawlService>();

    public void MapEndpoints(RouteGroupBuilder api)
    {
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
            OverworldContract.From(await service.CreateOverworldAsync(
                UserId(context),
                request.ToCommand(),
                cancellationToken)));

    private static async Task<IResult> GetOverworldAsync(
        Guid overworldId,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.GetOverworldAsync(
            overworldId,
            UserId(context),
            cancellationToken)));

    private static async Task<IResult> UpdateOverworldAsync(
        Guid overworldId,
        UpdateOverworldRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateOverworldAsync(
            overworldId,
            UserId(context),
            request.ToCommand(),
            cancellationToken)));

    private static async Task<IResult> CreateLocationAsync(
        Guid overworldId,
        LocationMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.CreateLocationAsync(
            overworldId,
            UserId(context),
            request.ToCreateCommand(),
            cancellationToken)));

    private static async Task<IResult> UpdateLocationAsync(
        Guid overworldId,
        Guid locationId,
        LocationMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateLocationAsync(
            overworldId,
            locationId,
            UserId(context),
            request.ToUpdateCommand(),
            cancellationToken)));

    private static async Task<IResult> DeleteLocationAsync(
        Guid overworldId,
        Guid locationId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.DeleteLocationAsync(
            overworldId,
            locationId,
            UserId(context),
            expectedVersion,
            cancellationToken)));

    private static async Task<IResult> CreateFeatureAsync(
        Guid overworldId,
        FeatureMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.CreateFeatureAsync(
            overworldId,
            UserId(context),
            request.ToCreateCommand(),
            cancellationToken)));

    private static async Task<IResult> UpdateFeatureAsync(
        Guid overworldId,
        Guid featureId,
        FeatureMutationRequest request,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.UpdateFeatureAsync(
            overworldId,
            featureId,
            UserId(context),
            request.ToUpdateCommand(),
            cancellationToken)));

    private static async Task<IResult> DeleteFeatureAsync(
        Guid overworldId,
        Guid featureId,
        long expectedVersion,
        HttpContext context,
        HexCrawlService service,
        CancellationToken cancellationToken) =>
        Results.Ok(OverworldContract.From(await service.DeleteFeatureAsync(
            overworldId,
            featureId,
            UserId(context),
            expectedVersion,
            cancellationToken)));

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
