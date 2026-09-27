using System.Security.Claims;
using HexCrawl.Application.Hosting;
using HexCrawl.Application.Rules;
using HexCrawl.Web.Authentication;
using HexCrawl.Web.Framework;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HexCrawl.Web.Modules.TravelRules;

public sealed record UpdateExpeditionRulesScopeRequest(long ExpectedVersion, Guid? CampaignId);

public sealed class TravelRulesModule : IHexCrawlModule
{
    public HexCrawlModuleManifest Manifest { get; } = new(
        Id: "travel-rules",
        DisplayName: "Rules Core travel and environment integration")
    {
        Dependencies = ["expeditions"]
    };

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ExpeditionTravelRulesService>();
        services.AddScoped<ProcedureResolutionRulesCoreAdapter>();
    }

    public void MapEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/expeditions/{expeditionId:guid}/travel-environment", GetCatalogAsync);
        api.MapPost("/expeditions/{expeditionId:guid}/travel-environment/{mechanicKey}/resolve", ResolveAsync);
        api.MapPut("/expeditions/{expeditionId:guid}/rules-scope", UpdateScopeAsync);
    }

    private static async Task<IResult> GetCatalogAsync(
        Guid expeditionId,
        HttpContext context,
        ExpeditionTravelRulesService travel,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await travel.GetCatalogAsync(
                expeditionId,
                UserId(context),
                cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(result);
        }
        catch (RulesCoreTravelGatewayException exception)
        {
            return RulesCoreUnavailable(exception);
        }
    }

    private static async Task<IResult> ResolveAsync(
        Guid expeditionId,
        string mechanicKey,
        TravelEnvironmentResolutionRequest request,
        HttpContext context,
        ExpeditionTravelRulesService travel,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await travel.ResolveAsync(
                expeditionId,
                UserId(context),
                mechanicKey,
                request,
                cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (RulesCoreTravelGatewayException exception)
        {
            return RulesCoreUnavailable(exception);
        }
    }

    private static async Task<IResult> UpdateScopeAsync(
        Guid expeditionId,
        UpdateExpeditionRulesScopeRequest request,
        HttpContext context,
        ExpeditionTravelRulesService travel,
        CancellationToken cancellationToken)
    {
        if (request.CampaignId is { } campaignId)
        {
            var authentication = HostedToolAuthenticationMiddleware.GetAuthenticationContext(context);
            if (authentication is null
                || !authentication.Campaigns.Any(campaign => campaign.Id == campaignId))
            {
                return Results.NotFound();
            }
        }

        var result = await travel.UpdateCampaignScopeAsync(
            expeditionId,
            UserId(context),
            request.ExpectedVersion,
            request.CampaignId,
            cancellationToken);
        return Results.Ok(result);
    }

    private static IResult RulesCoreUnavailable(RulesCoreTravelGatewayException exception) =>
        Results.Problem(
            title: "Rules Core travel mechanics unavailable",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string UserId(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException(
                "An authenticated Tool Host or explicitly configured standalone development identity is required.");
}
