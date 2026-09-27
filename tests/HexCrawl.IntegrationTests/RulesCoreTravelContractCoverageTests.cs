using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HexCrawl.Application.Hosting;
using HexCrawl.Application.Rules;
using HexCrawl.Web.Authentication;
using HexCrawl.Web.Rules;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexCrawl.IntegrationTests;

public sealed class RulesCoreTravelContractCoverageTests
{
    [Fact]
    public async Task CampaignCatalogUsesCampaignEffectiveEndpoint()
    {
        var campaignId = Guid.NewGuid();
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                $"/tool-host/hex-crawl/api/delegate/rules-core/upstream/api/campaigns/{campaignId:D}/rules/travel-environment",
                request.RequestUri?.AbsolutePath);
            return Json(HttpStatusCode.OK, $$"""
                {"scope":"campaign","campaignId":"{{campaignId:D}}","revisionNumber":7,"publishedAt":"2026-09-26T20:00:00Z","mechanics":[]}
                """);
        });
        var gateway = await CreateGatewayAsync(handler, campaignId);

        var catalog = await gateway.GetCatalogAsync(campaignId);

        Assert.Equal("campaign", catalog.Scope);
        Assert.Equal(campaignId, catalog.CampaignId);
        Assert.Equal(7, catalog.RevisionNumber);
    }

    [Fact]
    public async Task ForcedMarchCheckConsumesRulesCoreDcWithoutLocalEditionFormula()
    {
        var handler = new RecordingHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith(
                "/api/rules/travel-environment/travel.overland.forced-march-check/resolve",
                request.RequestUri?.AbsolutePath,
                StringComparison.Ordinal);
            Assert.Contains("\"extra-hours\":2", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """
                {"mechanicKey":"travel.overland.forced-march-check","mechanicState":"resolved","evaluationState":"resolved","quantity":null,"factor":null,"check":{"dc":14,"abilityKey":"constitution","competencyConceptKey":null,"cadence":"per-extra-hour","failureConsequenceKey":"forced-march-failure"},"missingInputKeys":[],"sourceAttributions":[]}
                """);
        });
        var gateway = await CreateGatewayAsync(handler);

        var evaluation = await gateway.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.ForcedMarchCheck,
            new TravelEnvironmentResolutionRequest(
                IntegerInputs: new Dictionary<string, int> { ["extra-hours"] = 2 }));

        Assert.NotNull(evaluation);
        Assert.Equal(14, evaluation.Check?.Dc);
        Assert.Equal("constitution", evaluation.Check?.AbilityKey);
        Assert.Equal("per-extra-hour", evaluation.Check?.Cadence);
    }

    [Fact]
    public async Task MountVehicleQuantityPreservesUnitsAndTimeBase()
    {
        var handler = new RecordingHandler((_, body) =>
        {
            Assert.Contains("\"travel-mode\":\"keelboat\"", body, StringComparison.Ordinal);
            Assert.Contains("\"period\":\"day\"", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """
                {"mechanicKey":"travel.overland.mount-vehicle-distance","mechanicState":"resolved","evaluationState":"resolved","quantity":{"value":24,"unit":"miles","perUnit":"day"},"factor":null,"check":null,"missingInputKeys":[],"sourceAttributions":[]}
                """);
        });
        var gateway = await CreateGatewayAsync(handler);

        var evaluation = await gateway.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.MountVehicleDistance,
            new TravelEnvironmentResolutionRequest(
                StringInputs: new Dictionary<string, string>
                {
                    ["travel-mode"] = "keelboat",
                    ["period"] = "day"
                }));

        Assert.NotNull(evaluation?.Quantity);
        Assert.Equal(24m, evaluation.Quantity.Value);
        Assert.Equal("miles", evaluation.Quantity.Unit);
        Assert.Equal("day", evaluation.Quantity.PerUnit);
    }

    [Fact]
    public async Task RequiresAdjudicationStateIsPreservedForDmFacingHandling()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, """
            {"mechanicKey":"travel.environment.hampered-movement","mechanicState":"requires-adjudication","evaluationState":"not-applicable","quantity":null,"factor":null,"check":null,"missingInputKeys":[],"sourceAttributions":[]}
            """));
        var gateway = await CreateGatewayAsync(handler);

        var evaluation = await gateway.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.HamperedMovement,
            new TravelEnvironmentResolutionRequest());

        Assert.NotNull(evaluation);
        Assert.Equal(TravelEnvironmentMechanicStates.RequiresAdjudication, evaluation.MechanicState);
        Assert.Equal(TravelEnvironmentEvaluationStates.NotApplicable, evaluation.EvaluationState);
    }

    [Fact]
    public async Task ServiceFailureIsReportedAsRulesCoreGatewayFailure()
    {
        var handler = new RecordingHandler((_, _) => Json(
            HttpStatusCode.ServiceUnavailable,
            "{\"error\":\"temporarily unavailable\"}"));
        var gateway = await CreateGatewayAsync(handler);

        var exception = await Assert.ThrowsAsync<RulesCoreTravelGatewayException>(() =>
            gateway.GetCatalogAsync(null));

        Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<DelegatedRulesCoreTravelGateway> CreateGatewayAsync(
        HttpMessageHandler handler,
        Guid? campaignId = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://tool-host.internal/") };
        var context = await AuthenticatedContextAsync(campaignId);
        return new DelegatedRulesCoreTravelGateway(
            http,
            new StaticHttpContextAccessor(context),
            NullLogger<DelegatedRulesCoreTravelGateway>.Instance);
    }

    private static async Task<DefaultHttpContext> AuthenticatedContextAsync(Guid? campaignId)
    {
        IReadOnlyList<ToolHostCampaignContext> campaigns = campaignId.HasValue
            ? [new ToolHostCampaignContext(campaignId.Value, "Campaign", "dm")]
            : [];
        var auth = new ToolHostAuthenticationContext(
            1,
            "hex-crawl",
            "dorks",
            new ToolHostUserContext("user-1", "DM"),
            [],
            campaigns)
        {
            DelegationCapability = "ddtd_v1_test-capability",
            DelegationPath = "/tool-host/hex-crawl/api/delegate/{targetSlug}/upstream"
        };
        var context = new DefaultHttpContext();
        context.Request.Headers[ToolHostAuthenticationHeaders.Ticket] = "ticket";
        context.Request.Headers[ToolHostAuthenticationHeaders.IntrospectionPath] =
            "/tool-host/hex-crawl/api/introspect";
        var configuration = new ConfigurationBuilder().Build();
        var middleware = new HostedToolAuthenticationMiddleware(_ => Task.CompletedTask, configuration);
        await middleware.InvokeAsync(context, new StaticAuthenticationClient(auth));
        return context;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StaticAuthenticationClient(ToolHostAuthenticationContext context)
        : IToolHostAuthenticationClient
    {
        public Task<ToolHostAuthenticationContext?> RedeemAsync(
            string ticket,
            string introspectionPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolHostAuthenticationContext?>(context);
    }

    private sealed class StaticHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}
