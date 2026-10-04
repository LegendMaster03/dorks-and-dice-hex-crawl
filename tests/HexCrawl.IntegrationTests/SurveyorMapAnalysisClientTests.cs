using System.Net;
using System.Text;
using HexCrawl.Application;
using HexCrawl.Web.MapAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HexCrawl.IntegrationTests;

public sealed class SurveyorMapAnalysisClientTests
{
    private const string Token = "integration-surveyor-token";

    [Fact]
    public async Task ClientSendsRasterCredentialCorrelationAndSourcePixelOptionsAndMapsValidResponse()
    {
        HttpRequestMessage? observed = null;
        byte[]? observedBody = null;
        var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            observed = request;
            observedBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return Json(HttpStatusCode.OK, ValidDetectedJson());
        });
        var client = CreateClient(handler);
        var raster = new byte[] { 1, 2, 3, 4 };

        var result = await client.DetectHexGridAsync(
            new MemoryStream(raster),
            "image/png",
            new MapAnalysisOptions(12.5, 400.25, 90_000, 0.54),
            "hex-correlation-1");

        Assert.NotNull(observed);
        Assert.Equal(HttpMethod.Post, observed.Method);
        Assert.Equal("http://surveyor.internal/v1/hex-grid/detect", observed.RequestUri!.GetLeftPart(UriPartial.Path));
        var query = observed.RequestUri.Query;
        Assert.Contains("minimumSpacingPixels=12.5", query);
        Assert.Contains("maximumSpacingPixels=400.25", query);
        Assert.Contains("maximumEdgeSamples=90000", query);
        Assert.Contains("minimumConfidence=0.54", query);
        Assert.Equal("Bearer", observed.Headers.Authorization?.Scheme);
        Assert.Equal(Token, observed.Headers.Authorization?.Parameter);
        Assert.Equal("hex-correlation-1", Assert.Single(observed.Headers.GetValues("X-Correlation-ID")));
        Assert.Equal("image/png", observed.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(raster, observedBody);
        Assert.Equal("v1", result.ApiVersion);
        Assert.Equal("map.hex-grid.detect", result.Capability);
        Assert.Equal("detected", result.Status);
        Assert.Equal(2048, result.Source.Width);
        Assert.NotNull(result.Fit);
        Assert.Equal("FlatTop", result.Fit.Orientation);
        Assert.Equal(79.949, result.Fit.CenterSpacingPixels, 6);
        Assert.Equal(0.9819, result.Fit.Confidence, 6);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(MapAnalysisAuthenticationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(MapAnalysisAuthenticationException))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(MapAnalysisUnavailableException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(MapAnalysisUnavailableException))]
    [InlineData(HttpStatusCode.GatewayTimeout, typeof(MapAnalysisTimeoutException))]
    [InlineData(HttpStatusCode.RequestTimeout, typeof(MapAnalysisTimeoutException))]
    public async Task ClientPreservesTransportFailureCategories(HttpStatusCode status, Type exceptionType)
    {
        var client = CreateClient(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
        var exception = await Record.ExceptionAsync(() => client.DetectHexGridAsync(
            new MemoryStream([1]), "image/png", new MapAnalysisOptions()));
        Assert.NotNull(exception);
        Assert.IsType(exceptionType, exception);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"apiVersion\":\"v2\",\"capability\":\"map.hex-grid.detect\",\"status\":\"gridless\",\"reason\":\"x\",\"source\":{\"width\":1,\"height\":1,\"mediaType\":\"image/png\"},\"analysis\":{\"width\":1,\"height\":1,\"scale\":1,\"sourceResolutionVerified\":true},\"fit\":null}")]
    [InlineData("{\"apiVersion\":\"v1\",\"capability\":\"map.hex-grid.detect\",\"status\":\"detected\",\"reason\":\"x\",\"source\":{\"width\":1,\"height\":1,\"mediaType\":\"image/png\"},\"analysis\":{\"width\":1,\"height\":1,\"scale\":1,\"sourceResolutionVerified\":true},\"fit\":null}")]
    [InlineData("{\"apiVersion\":\"v1\",\"capability\":\"map.hex-grid.detect\",\"status\":\"detected\",\"reason\":\"x\",\"source\":{\"width\":1,\"height\":1,\"mediaType\":\"image/png\"},\"analysis\":{\"width\":1,\"height\":1,\"scale\":1,\"sourceResolutionVerified\":true},\"fit\":{\"orientation\":\"FlatTop\",\"rotationDegrees\":0,\"centerSpacingPixels\":80,\"anchorPixel\":{\"x\":0,\"y\":0},\"confidence\":2,\"residualPixels\":0,\"supportCoverage\":1,\"orientationSupport\":1,\"translationScore\":1,\"competingTranslationScore\":0,\"linePeriodicityScore\":1,\"phaseScore\":1}}")]
    public async Task ClientRejectsMalformedOrIncompatibleSurveyorResponses(string payload)
    {
        var client = CreateClient(new DelegateHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, payload))));
        await Assert.ThrowsAsync<MapAnalysisProtocolException>(() => client.DetectHexGridAsync(
            new MemoryStream([1]), "image/png", new MapAnalysisOptions()));
    }

    [Fact]
    public async Task ClientTranslatesItsOwnFiniteTimeoutButPreservesCallerCancellation()
    {
        var handler = new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        var client = CreateClient(handler, timeoutMilliseconds: 1_000);
        await Assert.ThrowsAsync<MapAnalysisTimeoutException>(() => client.DetectHexGridAsync(
            new MemoryStream([1]), "image/png", new MapAnalysisOptions()));

        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DetectHexGridAsync(
            new MemoryStream([1]), "image/png", new MapAnalysisOptions(), cancellationToken: callerCancellation.Token));
    }

    [Fact]
    public async Task ClientDoesNotRequireSurveyorConfigurationForHexCrawlStartupButAnalysisFailsExplicitly()
    {
        var options = Options.Create(new SurveyorOptions());
        var client = new SurveyorMapAnalysisClient(
            new HttpClient(new DelegateHandler((_, _) => throw new InvalidOperationException("transport must not be used"))),
            options,
            NullLogger<SurveyorMapAnalysisClient>.Instance);
        await Assert.ThrowsAsync<MapAnalysisUnavailableException>(() => client.DetectHexGridAsync(
            new MemoryStream([1]), "image/png", new MapAnalysisOptions()));
    }

    private static SurveyorMapAnalysisClient CreateClient(HttpMessageHandler handler, int timeoutMilliseconds = 35_000) =>
        new(
            new HttpClient(handler),
            Options.Create(new SurveyorOptions
            {
                BaseUrl = "http://surveyor.internal",
                ServiceToken = Token,
                RequestTimeoutMilliseconds = timeoutMilliseconds
            }),
            NullLogger<SurveyorMapAnalysisClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string ValidDetectedJson() =>
        """
        {
          "apiVersion":"v1",
          "capability":"map.hex-grid.detect",
          "status":"detected",
          "reason":"fixture",
          "source":{"width":2048,"height":1536,"mediaType":"image/png"},
          "analysis":{"width":2048,"height":1536,"scale":1,"sourceResolutionVerified":true},
          "fit":{
            "orientation":"FlatTop",
            "rotationDegrees":0.25,
            "centerSpacingPixels":79.949,
            "anchorPixel":{"x":10.5,"y":20.5},
            "confidence":0.9819,
            "residualPixels":0.506,
            "supportCoverage":0.8,
            "orientationSupport":0.9,
            "translationScore":0.91,
            "competingTranslationScore":0.12,
            "linePeriodicityScore":0.88,
            "phaseScore":0.76
          }
        }
        """;

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
