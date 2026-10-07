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

public sealed class RulesCoreTravelEnvironmentProviderTests
{
    [Fact]
    public async Task ReadsGlobalCatalogThroughToolHostDelegationAndMapsProviderMetadata()
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
        var provider = await CreateProviderAsync(handler);

        var result = await provider.GetCatalogAsync(null);

        Assert.Equal(TravelEnvironmentProviderAvailabilityStates.Available, result.Availability);
        Assert.Equal("rules-core", result.Provider?.ProviderKey);
        Assert.Equal("Rules Core", result.Provider?.DisplayName);
        var catalog = Assert.IsType<TravelEnvironmentCatalogView>(result.Catalog);
        Assert.Equal("global", catalog.Scope);
        Assert.Single(catalog.Mechanics);
        Assert.Equal(TravelEnvironmentMechanicKeys.WalkDistance, catalog.Mechanics[0].MechanicKey);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("ddtd_v1_test-capability", handler.Authorization?.Parameter);
    }

    [Fact]
    public async Task ResolvesCampaignMechanicWithoutLosingCheckMetadata()
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
        var provider = await CreateProviderAsync(handler, campaignId);

        var result = await provider.ResolveAsync(
            campaignId,
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            new TravelEnvironmentResolutionRequest(
                StringListInputs: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["risk-factors"] = ["forest"]
                }));

        Assert.Equal(TravelEnvironmentProviderResolutionStates.Resolved, result.Status);
        Assert.Equal(15, result.Evaluation?.Check?.Dc);
        Assert.Equal("skill.survival", result.Evaluation?.Check?.CompetencyConceptKey);
        Assert.Equal("once-per-hour-or-portion", result.Evaluation?.Check?.Cadence);
    }

    [Fact]
    public async Task CampaignCatalogUsesCampaignEffectiveEndpoint()
    {
        var campaignId = Guid.NewGuid();
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(
                $"/tool-host/hex-crawl/api/delegate/rules-core/upstream/api/campaigns/{campaignId:D}/rules/travel-environment",
                request.RequestUri?.AbsolutePath);
            return Json(HttpStatusCode.OK, $$"""
                {"scope":"campaign","campaignId":"{{campaignId:D}}","revisionNumber":7,"publishedAt":"2026-09-26T20:00:00Z","mechanics":[]}
                """);
        });
        var provider = await CreateProviderAsync(handler, campaignId);

        var result = await provider.GetCatalogAsync(campaignId);

        Assert.Equal(TravelEnvironmentProviderAvailabilityStates.Available, result.Availability);
        Assert.Equal("campaign", result.Catalog?.Scope);
        Assert.Equal(campaignId, result.Catalog?.CampaignId);
        Assert.Equal(7, result.Catalog?.RevisionNumber);
    }

    [Fact]
    public async Task QuantityResolutionPreservesUnitsAndTimeBase()
    {
        var handler = new RecordingHandler((_, body) =>
        {
            Assert.Contains("\"travel-mode\":\"keelboat\"", body, StringComparison.Ordinal);
            Assert.Contains("\"period\":\"day\"", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """
                {"mechanicKey":"travel.overland.mount-vehicle-distance","mechanicState":"resolved","evaluationState":"resolved","quantity":{"value":24,"unit":"miles","perUnit":"day"},"factor":null,"check":null,"missingInputKeys":[],"sourceAttributions":[]}
                """);
        });
        var provider = await CreateProviderAsync(handler);

        var result = await provider.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.MountVehicleDistance,
            new TravelEnvironmentResolutionRequest(
                StringInputs: new Dictionary<string, string>
                {
                    ["travel-mode"] = "keelboat",
                    ["period"] = "day"
                }));

        Assert.Equal(TravelEnvironmentProviderResolutionStates.Resolved, result.Status);
        var quantity = Assert.IsType<TravelEnvironmentQuantity>(result.Evaluation?.Quantity);
        Assert.Equal(24m, quantity.Value);
        Assert.Equal("miles", quantity.Unit);
        Assert.Equal("day", quantity.PerUnit);
    }

    [Fact]
    public async Task InputRequiredPreservesMissingInputKeys()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, """
            {"mechanicKey":"travel.navigation.avoid-getting-lost","mechanicState":"resolved","evaluationState":"input-required","quantity":null,"factor":null,"check":null,"missingInputKeys":["risk-factors"],"sourceAttributions":[]}
            """));
        var provider = await CreateProviderAsync(handler);

        var result = await provider.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.AvoidGettingLost,
            new TravelEnvironmentResolutionRequest());

        Assert.Equal(TravelEnvironmentProviderResolutionStates.InputRequired, result.Status);
        Assert.Collection(result.MissingInputKeys, key => Assert.Equal("risk-factors", key));
    }

    [Fact]
    public async Task NotApplicableAndAdjudicationRemainDistinct()
    {
        var notApplicableProvider = await CreateProviderAsync(new RecordingHandler((_, _) => Json(HttpStatusCode.OK, """
            {"mechanicKey":"travel.environment.hampered-movement","mechanicState":"resolved","evaluationState":"not-applicable","quantity":null,"factor":null,"check":null,"missingInputKeys":[],"sourceAttributions":[]}
            """)));
        var adjudicationProvider = await CreateProviderAsync(new RecordingHandler((_, _) => Json(HttpStatusCode.OK, """
            {"mechanicKey":"travel.environment.hampered-movement","mechanicState":"requires-adjudication","evaluationState":"not-applicable","quantity":null,"factor":null,"check":null,"missingInputKeys":[],"sourceAttributions":[]}
            """)));

        var notApplicable = await notApplicableProvider.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.HamperedMovement,
            new TravelEnvironmentResolutionRequest());
        var adjudication = await adjudicationProvider.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.HamperedMovement,
            new TravelEnvironmentResolutionRequest());

        Assert.Equal(TravelEnvironmentProviderResolutionStates.NotApplicable, notApplicable.Status);
        Assert.Equal(TravelEnvironmentProviderResolutionStates.RequiresAdjudication, adjudication.Status);
    }

    [Fact]
    public async Task MissingCapabilityIsUnsupportedRatherThanTransportFailure()
    {
        var provider = await CreateProviderAsync(new RecordingHandler((_, _) =>
            Json(HttpStatusCode.NotFound, "{\"error\":\"missing\"}")));

        var result = await provider.ResolveAsync(
            null,
            "travel.example.missing",
            new TravelEnvironmentResolutionRequest());

        Assert.Equal(TravelEnvironmentProviderResolutionStates.Unsupported, result.Status);
        Assert.Null(result.Evaluation);
    }

    [Fact]
    public async Task ServiceFailureBecomesProviderFailedStatusWithoutRulesCoreException()
    {
        var provider = await CreateProviderAsync(new RecordingHandler((_, _) => Json(
            HttpStatusCode.ServiceUnavailable,
            "{\"error\":\"temporarily unavailable\"}")));

        var result = await provider.GetCatalogAsync(null);

        Assert.Equal(TravelEnvironmentProviderAvailabilityStates.Failed, result.Availability);
        Assert.Null(result.Catalog);
        Assert.Contains("503", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutBecomesProviderFailedStatusWithoutEscapingTransportException()
    {
        var provider = await CreateProviderAsync(new RecordingHandler((_, _) =>
            throw new TaskCanceledException("simulated timeout")));

        var result = await provider.GetCatalogAsync(null);

        Assert.Equal(TravelEnvironmentProviderAvailabilityStates.Failed, result.Availability);
        Assert.Null(result.Catalog);
        Assert.Contains("timed out", result.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedResponseBecomesProviderFailedStatus()
    {
        var provider = await CreateProviderAsync(new RecordingHandler((_, _) =>
            Json(HttpStatusCode.OK, "{not-valid-json")));

        var result = await provider.GetCatalogAsync(null);

        Assert.Equal(TravelEnvironmentProviderAvailabilityStates.Failed, result.Availability);
        Assert.Null(result.Catalog);
        Assert.Contains("invalid", result.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyResponseBecomesProviderFailedStatus()
    {
        var provider = await CreateProviderAsync(new RecordingHandler((_, _) =>
            Json(HttpStatusCode.OK, "null")));

        var result = await provider.GetCatalogAsync(null);

        Assert.Equal(TravelEnvironmentProviderAvailabilityStates.Failed, result.Availability);
        Assert.Null(result.Catalog);
        Assert.Contains("empty", result.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingToolHostConfigurationReportsProviderUnavailableWithoutHttpCall()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("HTTP should not be called."));
        using var http = new HttpClient(handler);
        var provider = new RulesCoreTravelEnvironmentProvider(
            http,
            new StaticHttpContextAccessor(new DefaultHttpContext()),
            NullLogger<RulesCoreTravelEnvironmentProvider>.Instance);

        var catalog = await provider.GetCatalogAsync(null);
        var resolution = await provider.ResolveAsync(
            null,
            TravelEnvironmentMechanicKeys.WalkDistance,
            new TravelEnvironmentResolutionRequest());

        Assert.Equal(TravelEnvironmentProviderAvailabilityStates.Unavailable, catalog.Availability);
        Assert.Equal(TravelEnvironmentProviderResolutionStates.Unavailable, resolution.Status);
        Assert.Equal(0, handler.CallCount);
    }

    private static async Task<RulesCoreTravelEnvironmentProvider> CreateProviderAsync(
        HttpMessageHandler handler,
        Guid? campaignId = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://tool-host.internal/") };
        var context = await AuthenticatedContextAsync(campaignId);
        return new RulesCoreTravelEnvironmentProvider(
            http,
            new StaticHttpContextAccessor(context),
            NullLogger<RulesCoreTravelEnvironmentProvider>.Instance);
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
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}
