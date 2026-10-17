using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Web.MapAnalysis;
using Microsoft.Extensions.Options;

namespace HexCrawl.IntegrationTests;

public sealed class SurveyorPeriodicMotifInvestigationClientTests
{
    private const string Token = "survey-test-token";
    private const string SquareTranslationSymbol = "<8:2 7 6 8,3 5 7 8,4 6 5 8:4,4>";

    [Theory]
    [InlineData("qa-regular-hex-grid", "<1:1,1,1:6,3>")]
    [InlineData("qa-regular-hex-grid-before", "<1:1,1,1:6,3>")]
    [InlineData("fallout-warfare2-9x9-blank", "<1:1,1,1:4,4>")]
    [InlineData("fallout-warfare2-9x9-blank-before", "<1:1,1,1:4,4>")]
    [InlineData("fallout-warfare2-9x9-populated", "<1:1,1,1:4,4>")]
    [InlineData("fallout-wanderer-5x5-blank", "<1:1,1,1:4,4>")]
    [InlineData("fallout-wanderer-5x5-populated", "<1:1,1,1:4,4>")]
    public async Task OriginalRasterResponsesRetainTheirDistantPeriodAlignment(string fixture, string symbol)
    {
        // Frozen responses generated from the exact acceptance PNGs. The
        // -before cases retain 42a46ae output; the rest use the corrected
        // detector. Exercise parsing AND final placement for both.
        string payload = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "Surveyor", fixture + ".json"));
        var client = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
        var observed = await client.InvestigateAsync(new MemoryStream([1]), "image/png");
        var (world, _) = Phase19RasterRegistrationTests.Fixture(symbol);
        var result = HexCrawl.Web.Api.Phase19RasterRegistration.Assess(world, observed);
        Assert.Equal("candidates", result.Status);
        Assert.NotEmpty(result.Candidates);
        foreach (var option in result.Candidates)
        {
            var t = option.Alignment;
            // Independently compare several distant repetitions to the world
            // lattice. A locally tolerable per-period error must not accumulate.
            var origin = t.ToWorld(new(0, 0));
            var a = observed.Candidate!.TranslationBasisSourcePixels[0];
            var b = observed.Candidate.TranslationBasisSourcePixels[1];
            var u = world.Realization.TranslationU;
            var v = world.Realization.TranslationV;
            double det = u.X * v.Y - u.Y * v.X;
            foreach (var (i, j) in new[] { (0, 9), (9, 0), (9, 9), (-9, 9) })
            {
                var point = t.ToWorld(new(i * a.X + j * b.X, i * a.Y + j * b.Y));
                double dx = point.X - origin.X, dy = point.Y - origin.Y;
                double fu = (dx * v.Y - dy * v.X) / det;
                double fv = (u.X * dy - u.Y * dx) / det;
                Assert.InRange(Math.Abs(fu - Math.Round(fu)), 0, 1e-8);
                Assert.InRange(Math.Abs(fv - Math.Round(fv)), 0, 1e-8);
            }
            // Independent fixture geometry, not returned motif coordinates:
            // inspect printed vertices in distant rows/columns, measuring
            // error back in source pixels under the EXACT saved transform.
            foreach (var (center, vertices) in PrintedCells(fixture))
            {
                var lookup = world.Containing(t.ToWorld(center), 256);
                Assert.Equal(WorldCellLookupStatus.Unique, lookup.Status);
                var target = world.Resolve(lookup.Candidates[0].Address).Polygon;
                foreach (var vertex in vertices)
                {
                    var actual = t.ToWorld(vertex);
                    double inverseDet = t.M11 * t.M22 - t.M12 * t.M21;
                    double error = target.Min(p =>
                    {
                        double dx = p.X - actual.X, dy = p.Y - actual.Y;
                        return Math.Sqrt(Math.Pow((t.M22 * dx - t.M12 * dy) / inverseDet, 2)
                            + Math.Pow((t.M11 * dy - t.M21 * dx) / inverseDet, 2));
                    });
                    Assert.InRange(error, 0, 2);
                }
            }
        }
    }

    private static IEnumerable<(WorldPoint Center, WorldPoint[] Vertices)> PrintedCells(string fixture)
    {
        if (fixture.StartsWith("qa-regular-hex-grid", StringComparison.Ordinal))
        {
            foreach (int u in new[] { 1, 5, 9 })
                foreach (int v in new[] { 0, 2, 5 })
                {
                    var center = new WorldPoint(90 * u, Math.Sqrt(3) * 60 * (v + u / 2d));
                    if (center.Y > 660) continue;
                    yield return (center, Enumerable.Range(0, 6).Select(i => new WorldPoint(
                        center.X + 60 * Math.Cos(i * Math.PI / 3),
                        center.Y + 60 * Math.Sin(i * Math.PI / 3))).ToArray());
                }
        }
        else
        {
            bool five = fixture.StartsWith("fallout-wanderer", StringComparison.Ordinal);
            double pitch = five ? 280 : 176;
            int[] indices = five ? [0, 2, 4] : [0, 4, 8];
            foreach (int column in indices)
                foreach (int row in indices)
                {
                    double x = pitch * column, y = pitch * row;
                    yield return (new(x + pitch / 2, y + pitch / 2),
                        [new(x, y), new(x + pitch, y), new(x + pitch, y + pitch), new(x, y + pitch)]);
                }
        }
    }

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
        Assert.Equal("registered", result.Evidence.MetricRegistration?.Status);
        Assert.Equal("supported", result.Evidence.MetricRegistration?.SourceProjection.Status);
    }

    [Fact]
    public async Task VerifiedPeriodicMotifMayStraddleItsNormalizedOrigin()
    {
        // The Surveyor metric cover is normalized in periodic coordinates:
        // its first cell is not necessarily an observed contour at x>=0/y>=0.
        // This is a genuine gauge shift of an otherwise unchanged periodic
        // polygon and reciprocal chamber graph, not a request to infer shape.
        var shifted = JsonNode.Parse(Candidate())!;
        var polygon = (JsonArray)shifted["candidate"]!["motifCells"]![0]!["polygonSourcePixels"]!;
        foreach (var vertex in polygon)
        {
            vertex!["x"] = vertex["x"]!.GetValue<int>() - 32;
            vertex["y"] = vertex["y"]!.GetValue<int>() - 32;
        }
        var payload = shifted.ToJsonString();
        var client = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
        var result = await client.InvestigateAsync(new MemoryStream([1]), "image/png");
        Assert.Equal("consistent-candidate", result.Status);
        Assert.False(result.Authoritative);
        Assert.Equal(-32d, result.Candidate!.MotifCells[0].PolygonSourcePixels[0].X);
        Assert.Equal(-32d, result.Candidate.MotifCells[0].PolygonSourcePixels[0].Y);
        Assert.Equal(SquareTranslationSymbol, result.Candidate.DsSymbol);
        Assert.Equal("supported", result.Evidence!.MetricRegistration!.SourceProjection.Status);
    }

    [Fact]
    public async Task NormalizedPeriodicOriginDoesNotPermitUnboundedCoordinates()
    {
        // The normalized-gauge allowance depends only on the two observed
        // period vectors; it never licenses arbitrary off-image polygons.
        var corrupted = JsonNode.Parse(Candidate())!;
        corrupted["candidate"]!["motifCells"]![0]!["polygonSourcePixels"]![0]!["x"] = -1000;
        var payload = corrupted.ToJsonString();
        var client = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
        await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
            client.InvestigateAsync(new MemoryStream([1]), "image/png"));
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
    public async Task ForgedAnalysisScaleCannotRelaxSourcePixelGeometryChecks()
    {
        var forged = JsonNode.Parse(Candidate())!;
        forged["analysis"]!["sourceResolutionVerified"] = false;
        forged["analysis"]!["scale"] = 1e-9;
        forged["candidate"]!["translationBasisSourcePixels"]![0]!["x"] = 128;
        forged["candidate"]!["translationBasisSourcePixels"]![1]!["y"] = 128;
        var client = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get
                ? Discovery() : forged.ToJsonString()))));
        await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
            client.InvestigateAsync(new MemoryStream([1]), "image/png"));

        // Both dimensions *round* to one pixel at the forged scale, so
        // dimension consistency alone is insufficient. A fully verified
        // source projection cannot exist on a 1x1 analysis raster.
        var tiny = JsonNode.Parse(Candidate())!;
        tiny["analysis"]!["sourceResolutionVerified"] = false;
        tiny["analysis"]!["width"] = 1;
        tiny["analysis"]!["height"] = 1;
        tiny["analysis"]!["scale"] = 0.009;
        var tinyClient = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get
                ? Discovery() : tiny.ToJsonString()))));
        await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
            tinyClient.InvestigateAsync(new MemoryStream([1]), "image/png"));
    }

    [Fact]
    public async Task StructurallyCorrectButGeometricallyForgedMotifsAreRejected()
    {
        // These mutations leave the canonical D-symbol and the periodic
        // reciprocal edge graph intact: only the measured image geometry is
        // false. The independent topology verifier alone would accept them.
        var warped = JsonNode.Parse(Candidate())!;
        warped["candidate"]!["motifCells"]![0]!["polygonSourcePixels"]![1]!["x"] = 110;
        var crossing = JsonNode.Parse(Candidate())!;
        crossing["candidate"]!["motifCells"]![0]!["polygonSourcePixels"]![1]!["y"] = 64;
        crossing["candidate"]!["motifCells"]![0]!["polygonSourcePixels"]![2]!["y"] = 0;
        var wrongPeriod = JsonNode.Parse(Candidate())!;
        wrongPeriod["candidate"]!["translationBasisSourcePixels"]![0]!["x"] = 120;

        foreach (var payload in new[]
        {
            warped.ToJsonString(), crossing.ToJsonString(), wrongPeriod.ToJsonString()
        })
        {
            var client = Client(new DelegateHandler((request, _) =>
                Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
            await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
                client.InvestigateAsync(new MemoryStream([1]), "image/png"));
        }
    }

    [Fact]
    public async Task SubpixelUncertaintyDoesNotRequireArtificiallyExactSharedInkBoundaries()
    {
        var noisy = JsonNode.Parse(Candidate())!;
        var poly = noisy["candidate"]!["motifCells"]![0]!["polygonSourcePixels"]!;
        poly[1]!["x"] = 64.4;
        poly[2]!["x"] = 63.8;
        poly[2]!["y"] = 64.2;
        poly[3]!["y"] = 63.6;
        var payload = noisy.ToJsonString();
        var client = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
        var result = await client.InvestigateAsync(new MemoryStream([1]), "image/png");
        Assert.Equal("consistent-candidate", result.Status);
        Assert.False(result.Authoritative);
        Assert.NotNull(result.Candidate);
    }

    [Fact]
    public async Task DownsampledSourceUsesAnalysisPixelToleranceWithoutRelaxingTopology()
    {
        var observed = JsonNode.Parse(Candidate())!;
        var candidate = observed["candidate"]!;
        foreach (var vector in (JsonArray)candidate["translationBasisSourcePixels"]!)
        {
            vector!["x"] = vector["x"]!.GetValue<int>() * 10;
            vector["y"] = vector["y"]!.GetValue<int>() * 10;
        }
        var polygon = (JsonArray)candidate["motifCells"]![0]!["polygonSourcePixels"]!;
        foreach (var point in polygon)
        {
            point!["x"] = point["x"]!.GetValue<int>() * 10;
            point["y"] = point["y"]!.GetValue<int>() * 10;
        }
        polygon[1]!["x"] = 695; // 5.5 pixels of contour disagreement at analysis resolution
        observed["source"]!["width"] = 1280;
        observed["source"]!["height"] = 1280;
        observed["analysis"]!["scale"] = 0.1;
        observed["analysis"]!["sourceResolutionVerified"] = false;
        observed["evidence"]!["maximumRigidVertexResidualSourcePixels"] = 12.0;
        var payload = observed.ToJsonString();
        var client = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
        var result = await client.InvestigateAsync(new MemoryStream([1]), "image/png");
        Assert.Equal("consistent-candidate", result.Status);
        Assert.False(result.Analysis!.SourceResolutionVerified);
        Assert.Equal(0.1, result.Analysis.Scale, 8);

        // A much larger displaced corner remains invalid even when a scan
        // was downscaled. Contour tolerance is not unlimited.
        polygon[1]!["x"] = 900;
        var corrupted = observed.ToJsonString();
        var invalid = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : corrupted))));
        await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
            invalid.InvestigateAsync(new MemoryStream([1]), "image/png"));
    }

    [Theory]
    [InlineData("<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>")]
    [InlineData("<14:2 5 7 9 11 13 14,1 4 6 8 10 12 14 13,3 5 4 6 7 8 9 14 11 13:6 4,3 4 4 3>")]
    public async Task GeneralMixedAndNonEdgeTopologySurvivesExperimentalWireParsing(string sourceSymbol)
    {
        // An independent C# D-symbol-to-periodic-metric constructor creates
        // unfamiliar mixed-cell geometry without registration or a raster.
        // Encode it using Surveyor's v3 wire shape and validate that the
        // protocol consumer preserves its full motif rather than assuming
        // a single four-sided or six-sided cell.
        var built = DelaneyDressHarmonicMetricRealization.Construct(sourceSymbol, 1, "pixel");
        Assert.True(built.Status == "realized", built.Reason);
        var topology = built.Topology!;
        var geometry = built.Realization!;
        Assert.True(PeriodicMetricWitnessValidator.Validate(topology, geometry));
        var points = geometry.Polygons.Values.SelectMany(x => x).ToArray();
        double minimumX = points.Min(p => p.X);
        double minimumY = points.Min(p => p.Y);
        double maximumX = points.Max(p => p.X);
        double maximumY = points.Max(p => p.Y);
        const double magnification = 160;
        int width = (int)Math.Ceiling((maximumX - minimumX) * magnification) + 80;
        int height = (int)Math.Ceiling((maximumY - minimumY) * magnification) + 80;
        MapAnalysisPoint Pixel(TilingWorldPoint p) => new(
            40 + (p.X - minimumX) * magnification,
            40 + (p.Y - minimumY) * magnification);
        MapAnalysisPoint Period(TilingWorldPoint p) => new(
            p.X * magnification, p.Y * magnification);
        var payload = JsonSerializer.Serialize(new
        {
            apiVersion = "v3", capability = "map.periodic-tiling.investigate",
            maturity = "experimental", authoritative = false,
            status = "consistent-candidate", reason = "independent polygon witness",
            candidate = new
            {
                dsSymbol = topology.TranslationDsSymbol,
                translationBasisSourcePixels = new[]
                {
                    Period(geometry.TranslationU), Period(geometry.TranslationV)
                },
                motifCells = topology.MotifCells.Select(cell => new
                {
                    provisionalId = cell.Id,
                    polygonSourcePixels = geometry.Polygons[cell.Id].Select(Pixel).ToArray(),
                    boundaries = cell.Boundary.Select(edge => new
                    {
                        sideIndex = edge.Index,
                        targetProvisionalId = edge.TargetMotifCellId,
                        targetSideIndex = edge.ReciprocalInterfaceIndex,
                        translation = new
                        {
                            u = edge.TargetTranslation.U,
                            v = edge.TargetTranslation.V
                        },
                        supportingObservations = 6
                    }).ToArray()
                }).ToArray()
            },
            evidence = new
            {
                matchedHypotheses = 1, checkedHypotheses = 2,
                rejectedHypotheses = 1, minimumEdgeObservations = 6,
                originalRasterEdgeSupport = 0.98,
                maximumRigidVertexResidualSourcePixels = 0.5,
                translationRefinementResidualSourcePixels = (double?)null,
                metricRegistration = VerifiedMetricEvidence()
            },
            source = new { width, height, mediaType = "image/png" },
            analysis = new
            {
                width, height, scale = 1, sourceResolutionVerified = true
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var client = Client(new DelegateHandler((request, _) =>
            Task.FromResult(Json(request.Method == HttpMethod.Get ? Discovery() : payload))));
        var observed = await client.InvestigateAsync(new MemoryStream([1]), "image/png");
        Assert.False(observed.Authoritative);
        Assert.Equal("consistent-candidate", observed.Status);
        Assert.Equal(topology.TranslationDsSymbol, observed.Candidate!.DsSymbol);
        Assert.Equal(topology.MotifCells.Count, observed.Candidate.MotifCells.Count);
        Assert.Equal(topology.MotifCells.Sum(c => c.Boundary.Count),
            observed.Candidate.MotifCells.Sum(c => c.Boundaries.Count));
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
            translationRefinementResidualSourcePixels = (double?)null,
            metricRegistration = VerifiedMetricEvidence()
        },
        source = new { width = 128, height = 128, mediaType = "image/png" },
        analysis = new { width = 128, height = 128, scale = 1,
            sourceResolutionVerified = true }
    }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static object VerifiedMetricEvidence() => new
    {
        status = "registered",
        reason = (string?)null,
        maximumContourResidualSourcePixels = 1.5,
        rmsContourResidualSourcePixels = 0.8,
        originalRasterEdgeSupport = 0.95,
        mathematicalMetricSymmetries = (int?)null,
        rasterSymmetriesChecked = 0,
        rasterSymmetriesSupported = 0,
        sourceProjection = new
        {
            status = "supported", reason = (string?)null,
            edgeSupport = 0.95, interiorSupport = 0.97,
            checkedRegions = 9, supportedRegions = 9
        }
    };

    [Fact]
    public async Task MissingOrUnprovenMetricAndRasterProjectionAreNotQualifiedCandidates()
    {
        var missing = JsonNode.Parse(Candidate())!;
        ((JsonObject)missing["evidence"]!).Remove("metricRegistration");
        var incompleteMetric = JsonNode.Parse(Candidate())!;
        incompleteMetric["evidence"]!["metricRegistration"]!["status"] = "inconclusive";
        var incompleteProjection = JsonNode.Parse(Candidate())!;
        incompleteProjection["evidence"]!["metricRegistration"]!["sourceProjection"]!["status"] = "inconclusive";
        var exaggeratedContour = JsonNode.Parse(Candidate())!;
        exaggeratedContour["evidence"]!["metricRegistration"]!["maximumContourResidualSourcePixels"] = 90;
        foreach (var forged in new[] { missing, incompleteMetric, incompleteProjection, exaggeratedContour })
        {
            var client = Client(new DelegateHandler((request, _) =>
                Task.FromResult(Json(request.Method == HttpMethod.Get
                    ? Discovery() : forged.ToJsonString()))));
            await Assert.ThrowsAsync<MapAnalysisProtocolException>(() =>
                client.InvestigateAsync(new MemoryStream([1]), "image/png"));
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => action(request, cancellationToken);
    }
}
