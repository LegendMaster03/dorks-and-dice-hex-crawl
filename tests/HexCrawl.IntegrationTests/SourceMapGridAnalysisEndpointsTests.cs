using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HexCrawl.IntegrationTests;

public sealed class SourceMapGridAnalysisEndpointsTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task AnalysisLoadsAuthorizedStoredAssetReturnsTypedObservationAndDoesNotMutateWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var fake = new RecordingMapAnalysisService((bytes, mediaType, _, _, _) =>
            {
                Assert.Equal(TinyPng, bytes);
                Assert.Equal("image/png", mediaType);
                return Task.FromResult(Detected(width: 1, height: 1));
            });
            using var factory = WithAnalysis(TestWebHost.Create(database, "alice"), fake);
            using var client = factory.CreateClient();
            var (worldId, sourceMapId, version) = await CreateWorldAndMap(client);

            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-analysis",
                content: null);
            response.EnsureSuccessStatusCode();
            var analyzed = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("v2", analyzed.GetProperty("apiVersion").GetString());
            Assert.Equal("map.periodic-tiling.detect", analyzed.GetProperty("capability").GetString());
            Assert.Equal("detected", analyzed.GetProperty("status").GetString());
            Assert.Equal(80, analyzed.GetProperty("fit").GetProperty("centerSpacingPixels").GetDouble(), 8);
            Assert.Equal(1, fake.Calls);

            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
            Assert.Equal(JsonValueKind.Null, reopened.GetProperty("sourceMaps")[0].GetProperty("alignment").ValueKind);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task AnalysisAuthorizationOccursBeforeSurveyorAndOtherOwnerReceivesNotFound()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var fake = new RecordingMapAnalysisService((_, _, _, _, _) => Task.FromResult(Detected(1, 1)));
            Guid worldId;
            Guid sourceMapId;
            using (var ownerFactory = WithAnalysis(TestWebHost.Create(database, "alice"), fake))
            using (var owner = ownerFactory.CreateClient())
            {
                (worldId, sourceMapId, _) = await CreateWorldAndMap(owner);
            }

            using var otherFactory = WithAnalysis(TestWebHost.Create(database, "bob"), fake);
            using var other = otherFactory.CreateClient();
            using var response = await other.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-analysis",
                null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(0, fake.Calls);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task DimensionMismatchIsRejectedWithoutWorldMutation()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var fake = new RecordingMapAnalysisService((_, _, _, _, _) => Task.FromResult(Detected(2, 1)));
            using var factory = WithAnalysis(TestWebHost.Create(database), fake);
            using var client = factory.CreateClient();
            var (worldId, sourceMapId, version) = await CreateWorldAndMap(client);

            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-analysis",
                null);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Theory]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData("timeout", HttpStatusCode.GatewayTimeout)]
    [InlineData("auth", HttpStatusCode.BadGateway)]
    [InlineData("protocol", HttpStatusCode.BadGateway)]
    public async Task AnalysisFailureCategoriesAreDistinctAndNeverMutateWorld(string failure, HttpStatusCode expectedStatus)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var fake = new RecordingMapAnalysisService((_, _, _, _, _) => throw failure switch
            {
                "unavailable" => new MapAnalysisUnavailableException("offline"),
                "timeout" => new MapAnalysisTimeoutException("slow"),
                "auth" => new MapAnalysisAuthenticationException("bad credential"),
                "protocol" => new MapAnalysisProtocolException("bad response"),
                _ => new InvalidOperationException(failure)
            });
            using var factory = WithAnalysis(TestWebHost.Create(database), fake);
            using var client = factory.CreateClient();
            var (worldId, sourceMapId, version) = await CreateWorldAndMap(client);

            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-analysis",
                null);
            Assert.Equal(expectedStatus, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("Automatic map analysis", problem.GetProperty("title").GetString(), StringComparison.Ordinal);
            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task RequestCancellationPropagatesToAnalysisAndDoesNotMutateWorld()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new RecordingMapAnalysisService(async (_, _, _, _, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    throw;
                }
                return Detected(1, 1);
            });
            using var factory = WithAnalysis(TestWebHost.Create(database), fake);
            using var client = factory.CreateClient();
            var (worldId, sourceMapId, version) = await CreateWorldAndMap(client);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/grid-analysis",
                null,
                cancellation.Token));
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task OptInMotifInvestigationReadsOwnerMapWithoutPersistingCandidateOrChangingV2()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var fake = new RecordingMotifInvestigationService((bytes, mediaType) =>
            {
                Assert.Equal(TinyPng, bytes);
                Assert.Equal("image/png", mediaType);
                return new PeriodicMotifInvestigation(
                    "inconclusive", "insufficient repeated evidence", false, "experimental", "v3",
                    null, null, new MapAnalysisSource(1, 1, "image/png"),
                    new MapAnalysisRaster(1, 1, 1, true));
            });
            using var factory = WithInvestigation(TestWebHost.Create(database, "alice"), fake);
            using var client = factory.CreateClient();
            var (worldId, sourceMapId, version) = await CreateWorldAndMap(client);
            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/motif-investigation", null);
            response.EnsureSuccessStatusCode();
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("inconclusive", result.GetProperty("status").GetString());
            Assert.False(result.GetProperty("authoritative").GetBoolean());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("candidate").ValueKind);
            Assert.Equal(1, fake.Calls);

            var reopened = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, reopened.GetProperty("version").GetInt64());
            Assert.Equal(JsonValueKind.Null,
                reopened.GetProperty("sourceMaps")[0].GetProperty("alignment").ValueKind);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task OldSurveyorUnsupportedInvestigationIsExplicitAndReadOnly()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var fake = new RecordingMotifInvestigationService((_, _) =>
                new PeriodicMotifInvestigation(
                    "unsupported", "Connected Surveyor supports only v2.", false,
                    "experimental", null, null, null, null, null));
            using var factory = WithInvestigation(TestWebHost.Create(database, "alice"), fake);
            using var client = factory.CreateClient();
            var (worldId, sourceMapId, version) = await CreateWorldAndMap(client);
            using var response = await client.PostAsync(
                $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/motif-investigation", null);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("unsupported", result.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("candidate").ValueKind);
            var world = await client.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
            Assert.Equal(version, world.GetProperty("version").GetInt64());
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    [Fact]
    public async Task ExperimentalInvestigationChecksOwnershipAndRasterMetadataBeforeExposingResults()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var fake = new RecordingMotifInvestigationService((_, _) =>
                new PeriodicMotifInvestigation(
                    "inconclusive", "unmatched raster", false, "experimental", "v3",
                    null, null, new MapAnalysisSource(2, 1, "image/png"),
                    new MapAnalysisRaster(2, 1, 1, true)));
            Guid worldId, sourceMapId;
            long version;
            using (var ownerFactory = WithInvestigation(TestWebHost.Create(database, "alice"), fake))
            using (var owner = ownerFactory.CreateClient())
                (worldId, sourceMapId, version) = await CreateWorldAndMap(owner);

            using (var otherFactory = WithInvestigation(TestWebHost.Create(database, "bob"), fake))
            using (var other = otherFactory.CreateClient())
            {
                using var denied = await other.PostAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/motif-investigation", null);
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            }
            Assert.Equal(0, fake.Calls);
            using (var ownerFactory = WithInvestigation(TestWebHost.Create(database, "alice"), fake))
            using (var owner = ownerFactory.CreateClient())
            {
                using var rejected = await owner.PostAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/motif-investigation", null);
                Assert.Equal(HttpStatusCode.BadGateway, rejected.StatusCode);
                var reopened = await owner.GetFromJsonAsync<JsonElement>($"/api/overworlds/{worldId:D}");
                Assert.Equal(version, reopened.GetProperty("version").GetInt64());
            }
            Assert.Equal(1, fake.Calls);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }

    private static WebApplicationFactory<Program> WithInvestigation(
        WebApplicationFactory<Program> factory,
        IPeriodicMotifInvestigationService investigation) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPeriodicMotifInvestigationService>();
            services.AddSingleton(investigation);
        }));

    private static WebApplicationFactory<Program> WithAnalysis(
        WebApplicationFactory<Program> factory,
        IMapAnalysisService analysis) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMapAnalysisService>();
            services.AddSingleton(analysis);
        }));

    private static MapHexGridAnalysis Detected(int width, int height) => new(
        "v2",
        "map.periodic-tiling.detect",
        "detected",
        "fixture",
        new MapAnalysisSource(width, height, "image/png"),
        new MapAnalysisRaster(width, height, 1, true),
        new MapHexGridFit(
            "FlatTop",
            0,
            80,
            new MapAnalysisPoint(0.5, 0.5),
            0.98,
            0.5,
            0.8,
            0.9,
            0.9,
            0.1,
            0.9,
            0.8));

    private static async Task<(Guid WorldId, Guid SourceMapId, long Version)> CreateWorldAndMap(HttpClient client)
    {
        using var create = await client.PostAsJsonAsync("/api/overworlds", new
        {
            name = "Surveyor endpoint fixture",
            orientation = "PointyTop",
            origin = new { x = 0, y = 0 },
            rotationDegrees = 0,
            hexRadiusWorldUnits = 1,
            neighborCenterDistance = 12,
            distanceUnit = new { kind = "Mile", symbol = "mi", metersPerUnit = 1609.344 }
        });
        create.EnsureSuccessStatusCode();
        var world = await create.Content.ReadFromJsonAsync<JsonElement>();
        var worldId = world.GetProperty("id").GetGuid();

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(TinyPng);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(file, "file", "fixture.png");
        content.Add(new StringContent("Fixture"), "name");
        content.Add(new StringContent("Fixture geography"), "geographyKey");
        content.Add(new StringContent("GM"), "role");
        content.Add(new StringContent("True"), "containsBakedGrid");
        content.Add(new StringContent(world.GetProperty("version").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedVersion");
        using var upload = await client.PostAsync($"/api/overworlds/{worldId:D}/source-maps", content);
        upload.EnsureSuccessStatusCode();
        var updated = await upload.Content.ReadFromJsonAsync<JsonElement>();
        return (
            worldId,
            updated.GetProperty("sourceMaps")[0].GetProperty("id").GetGuid(),
            updated.GetProperty("version").GetInt64());
    }

    private sealed class RecordingMotifInvestigationService(
        Func<byte[], string, PeriodicMotifInvestigation> investigate)
        : IPeriodicMotifInvestigationService
    {
        public int Calls { get; private set; }
        public async Task<PeriodicMotifInvestigation> InvestigateAsync(
            Stream raster, string mediaType, string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            using var copy = new MemoryStream();
            await raster.CopyToAsync(copy, cancellationToken);
            return investigate(copy.ToArray(), mediaType);
        }
    }

    private sealed class RecordingMapAnalysisService(
        Func<byte[], string, MapAnalysisOptions, string?, CancellationToken, Task<MapHexGridAnalysis>> analyze)
        : IMapAnalysisService
    {
        public int Calls { get; private set; }

        public async Task<MapHexGridAnalysis> DetectHexGridAsync(
            Stream raster,
            string mediaType,
            MapAnalysisOptions options,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            using var copy = new MemoryStream();
            await raster.CopyToAsync(copy, cancellationToken);
            return await analyze(copy.ToArray(), mediaType, options, correlationId, cancellationToken);
        }
    }
}
