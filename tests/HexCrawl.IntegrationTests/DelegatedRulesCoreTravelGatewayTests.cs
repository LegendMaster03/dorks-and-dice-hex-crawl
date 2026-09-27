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

public sealed class DelegatedRulesCoreTravelGatewayTests
{
    [Fact]
    public async Task ReadsGlobalCatalogThroughToolHostDelegation()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "/tool-host/hex-crawl/api/delegate/rules-core/upstream/api/rules/travel-environment",
                request.RequestUri?.AbsolutePath);
            return Json(HttpStatusCode.OK, """
                {"scope":"global","campaignId":null,"revisionNumber":null,"publishedAt":null,"mechanics":[{"mechanicKey":"travel.overland.walk-distance","state":"resolved","canResolve":true,"definition":{"mechanicKey":"travel.overland.walk-distance","kind":"distance-rate","displayName":"Overland walking distance","resolutionKind":"lookup-quantity","inputs":[{"key":"base-speed-feet","valueKind":"integer","required":true,"allowedValues":null},{"key":"period","valueKind":"string","required":true,"allowedValues":["hour","day"]}],"quantityRows":null,"factorRows":null,"constantFactor":null,"linearCheck":null,"maximumCheck":null,"thresholdFactor":null,"factorSemantic":null,"scale":"overland"},"sourceAttributions":[]}]}
                """);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://tool-host.internal/") };
        var context = await AuthenticatedContextAsync(campaignId: null);
        var gateway = new DelegatedRulesCoreTravelGateway(
            http,
            new HttpContextAccessor { HttpContext = context },
            NullLogger<DelegatedRulesCoreTravelGateway>.Instance);

        var catalog = await gateway.GetCatalogAsync(null);

        Assert.Equal("global", catalog.Scope);
        Assert.Single(catalog.Mechanics);
        Assert.Equal(TravelEnvironmentMechanicKeys.WalkDistance, catalog.Mechanics[0].MechanicKey);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("ddtd_v1_test-capability", handler.Authorization?.Parameter);
    }

    [Fact]
    public async Task ResolvesCampaignMechanicWithoutLosingUnitsOrCheckMetadata()
    {
        var campaignId = Guid.NewGuid();
        var handler = new RecordingHandler((request, body) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                $"/tool-host/hex-crawl/api/delegate/rules-core/upstream/api/campaigns/{campaignId:D}/rules/travel-environment/travel.navigation.avoid-getting-lost/resolve",
                request.RequestUri?.AbsolutePath);
            Assert.Contains("risk-factors", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """
                {"mechanicKey":"travel.navigation.avoid-getting-lost","mechanicState":"resolved","evaluationState":"resolved","quantity":null,"factor":null,"check":{"dc":15,"abilityKey":null,"competencyConceptKey":"skill.survival","cadence":"once-per-hour-or-portion","failureConsequenceKey":"become-lost"},"missingInputKeys":[],"sourceAttributions":[]}
                """);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://tool-host.internal/") };
        var context = await AuthenticatedContextAsync(campaignId);
        var gateway = new DelegatedRulesCoreTravelGateway(
            http,
            new HttpContextAccessor { HttpContext = context },
            NullLogger<DelegatedRulesCoreTravelGateway>.Instance);

        var evaluation = await gateway.ResolveAsync(
            campaignId,
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            new TravelEnvironmentResolutionRequest(
                StringListInputs: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["risk-factors"] = ["forest"]
                }));

        Assert.NotNull(evaluation);
        Assert.Equal(15, evaluation.Check?.Dc);
        Assert.Equal("skill.survival", evaluation.Check?.CompetencyConceptKey);
        Assert.Equal("once-per-hour-or-portion", evaluation.Check?.Cadence);
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

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}
