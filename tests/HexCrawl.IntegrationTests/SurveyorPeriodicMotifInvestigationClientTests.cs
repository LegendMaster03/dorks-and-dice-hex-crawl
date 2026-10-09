using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Web.MapAnalysis;
using Microsoft.Extensions.Options;

namespace HexCrawl.IntegrationTests;

public sealed class SurveyorPeriodicMotifInvestigationClientTests
{
    private const string Token = "survey-test-token";
    private const string SquareTranslationSymbol = "<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>";

    [Fact]
    public async Task OldSurveyorDiscoveryReturnsExplicitUnsupportedWithoutSendingAnInvestigation()
    {
        int calls = 0;
        var client = Client(new DelegateHandler((request, _) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json("{}"));
        }));
        var result = await client.InvestigateAsync(new MemoryStream([1, 2, 3]), "image/png");
        Assert.Equal("unsupported", result.Status);
        Assert.False(result.Authoritative);
        Assert.Null(result.Candidate);
        Assert.Null(result.Evidence);
        Assert.Null(result.Source);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NegotiatedCandidateIsCheckedAgainstIndependentTopologyAndNeverAuthoritative()
    {
        int calls = 0;
        var client = Client(new DelegateHandler(async (request, ct) =>
        {
            calls++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Token, request.Headers.Authorization?.Parameter);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/", request.RequestUri?.AbsolutePath);
                return Json(Discovery());
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v3/periodic-tiling/investigate", request.RequestUri?.AbsolutePath);
            Assert.Equal("", request.RequestUri?.Query);
            Assert.Equal("image/png", request.Content?.Headers.ContentType?.MediaType);
            Assert.Equal("phase16-probe", Assert.Single(request.Headers.GetValues("X-Correlation-ID")));
            Assert.Equal(new byte[] { 1, 2, 3 }, await request.Content!.ReadAsByteArrayAsync(ct));
            return Json(Candidate());
        }));
        var result = await client.InvestigateAsync(
            new MemoryStream([1, 2, 3]), "image/png", "phase16-probe");
        Assert.Equal(2, calls);
        Assert.Equal("consistent-candidate", result.Status);
        Assert.False(result.Authoritative);
        Assert.Equal("experimental", result.Maturity);
        Assert.Equal("v3", result.SurveyorApiVersion);
        Assert.Equal(SquareTranslationSymbol, result.Candidate?.DsSymbol);
        Assert.Single(result.Candidate!.MotifCells);
        Assert.Equal(4, result.Candidate.MotifCells[0].Boundaries.Count);
        Assert.Equal(1, result.Evidence!.MatchedHypotheses);
    }

    [Fact]
    public async Task NonAuthoritativeUncertaintyHasNoPatternIdentityOrGeometry()
    {
        var result = await Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get
                ? Discovery() : JsonSerializer.Serialize(new
                {
                    apiVersion = "v3", capability = "map.periodic-tiling.investigate",
                    maturity = "experimental", authoritative = false,
                    status = "ambiguous", reason = "inconsistent distant regions",
                    candidate = (object?)null, evidence = (object?)null,
                    source = new { width = 128, height = 128, mediaType = "image/png" },
                    analysis = new { width = 128, height = 128, scale = 1,
                        sourceResolutionVerified = true }
                }))))).InvestigateAsync(new MemoryStream([1]), "image/png");
        Assert.Equal("ambiguous", result.Status);
        Assert.Null(result.Candidate);
        Assert.Null(result.Evidence);
    }

    [Fact]
    public async Task ForgedIdentityOrMismatchedIncidenceNeverBecomesCandidate()
    {
        foreach (var payload in new[]
        {
            Candidate().Replace(SquareTranslationSymbol, "<1:1,1,1:4,4>", StringComparison.Ordinal),
            Candidate().Replace("\"authoritative\":false", "\"authoritative\":true", StringComparison.Ordinal),
            Candidate().Replace("\"targetSideIndex\":2", "\"targetSideIndex\":1", StringComparison.Ordinal),
            Candidate().Replace("\"sourceResolutionVerified\":true", "\"sourceResolutionVerified\":false", StringComparison.Ordinal)
                .Replace("\"status\":\"consistent-candidate\"", "\"status\":\"ambiguous\"", StringComparison.Ordinal)
        })
        {
            var client = Client(new DelegateHandler((request, _) =>
                Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
            await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
                client.InvestigateAsync(new MemoryStream([1]), "image/png"));
        }
    }

    [Fact]
    public async Task MismatchedDiscoveryCannotPostEvenWhenItMentionsV3()
    {
        string invalid = Discovery().Replace("\"authoritative\":false",
            "\"authoritative\":true", StringComparison.Ordinal);
        int posts = 0;
        var client = Client(new DelegateHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) posts++;
            return Task.FromResult(Json(invalid));
        }));
        var result = await client.InvestigateAsync(new MemoryStream([1]), "image/png");
        Assert.Equal("unsupported", result.Status);
        Assert.Equal(0, posts);
    }

    [Fact]
    public async Task PayloadSizeBoundAndFailedProviderStatusAreDistinct()
    {
        var oversized = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get
                ? Discovery() : new string('x', 1_500_001)))));
        await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
            oversized.InvestigateAsync(new MemoryStream([1]), "image/png"));

        var unavailable = Client(new DelegateHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        await Assert.ThrowsAsync<MapAnalysisUnavailableException>(() =>
            unavailable.InvestigateAsync(new MemoryStream([1]), "image/png"));
    }

    private static SurveyorPeriodicMotifInvestigationClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new SurveyorOptions
        {
            BaseUrl = "http://surveyor.internal",
            ServiceToken = Token,
            RequestTimeoutMilliseconds = 10000
        }));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Discovery() => JsonSerializer.Serialize(new
    {
        apiVersion = "v2",
        capabilities = new object[]
        {
            new
            {
                id = "map.periodic-tiling.detect",
                path = "/v2/periodic-tiling/detect",
                apiVersion = "v2"
            },
            new
            {
                id = "map.periodic-tiling.investigate",
                path = "/v3/periodic-tiling/investigate",
                apiVersion = "v3", maturity = "experimental",
                authoritative = false, acceptedWorldTopology = false,
                supportsExpectedSymbol = false
            }
        }
    });

    private static object Edge(int index, int targetSide, int u, int v) => new
    {
        sideIndex = index, targetProvisionalId = "candidate-square",
        targetSideIndex = targetSide, translation = new { u, v },
        supportingObservations = 5
    };

    private static string Candidate() => JsonSerializer.Serialize(new
    {
        apiVersion = "v3", capability = "map.periodic-tiling.investigate",
        maturity = "experimental", authoritative = false,
        status = "consistent-candidate", reason = "provisional",
        candidate = new
        {
            dsSymbol = SquareTranslationSymbol,
            translationBasisSourcePixels = new[]
            {
                new { x = 64, y = 0 }, new { x = 0, y = 64 }
            },
            motifCells = new[]
            {
                new
                {
                    provisionalId = "candidate-square",
                    polygonSourcePixels = new[]
                    {
                        new { x = 0, y = 0 }, new { x = 64, y = 0 },
                        new { x = 64, y = 64 }, new { x = 0, y = 64 }
                    },
                    boundaries = new[]
                    {
                        Edge(0, 2, 0, -1), Edge(1, 3, 1, 0),
                        Edge(2, 0, 0, 1), Edge(3, 1, -1, 0)
                    }
                }
            }
        },
        evidence = new
        {
            matchedHypotheses = 1, checkedHypotheses = 2, rejectedHypotheses = 1,
            minimumEdgeObservations = 5, originalRasterEdgeSupport = 0.95,
            maximumRigidVertexResidualSourcePixels = 1.2,
            translationRefinementResidualSourcePixels = (double?)null
        },
        source = new { width = 128, height = 128, mediaType = "image/png" },
        analysis = new { width = 128, height = 128, scale = 1,
            sourceResolutionVerified = true }
    });

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => action(request, cancellationToken);
    }
}
