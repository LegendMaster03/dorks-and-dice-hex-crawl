using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using Microsoft.Extensions.Options;

namespace HexCrawl.Web.MapAnalysis;

/// <summary>
/// Opt-in v3 observation path. It cannot persist or accept a candidate as
/// authoritative world topology. The existing v2 client remains unchanged.
/// </summary>
public sealed class SurveyorPeriodicMotifInvestigationClient(
    HttpClient httpClient,
    IOptions<SurveyorOptions> configuredOptions) : IPeriodicMotifInvestigationService
{
    private const string Capability = "map.periodic-tiling.investigate";
    private const string Path = "v3/periodic-tiling/investigate";
    private const int MaximumResponseBytes = 1_500_000;

    public async Task<PeriodicMotifInvestigation> InvestigateAsync(
        Stream raster, string mediaType, string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = configuredOptions.Value;
        if (!configuration.IsConfigured)
            throw new MapAnalysisUnavailableException("Surveyor is not configured.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(configuration.RequestTimeout);
        try
        {
            var serviceRoot = new Uri(configuration.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            using var discoveryRequest = new HttpRequestMessage(HttpMethod.Get, serviceRoot);
            discoveryRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", configuration.ServiceToken);
            using var discoveryResponse = await httpClient.SendAsync(
                discoveryRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (discoveryResponse.StatusCode == HttpStatusCode.NotFound)
                return Unsupported("The connected Surveyor does not advertise generalized motif investigation.");
            CheckHttpStatus(discoveryResponse);
            using var discovery = await ReadBoundedJsonAsync(discoveryResponse, 65_536, timeout.Token);
            if (!AdvertisesSafeInvestigation(discovery.RootElement))
                return Unsupported("The connected Surveyor does not support the experimental non-authoritative v3 investigation contract.");

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(serviceRoot, Path));
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", configuration.ServiceToken);
            if (!string.IsNullOrWhiteSpace(correlationId))
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            request.Content = new StreamContent(raster);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            CheckHttpStatus(response);
            using var document = await ReadBoundedJsonAsync(response, MaximumResponseBytes, timeout.Token);
            return Parse(document.RootElement);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MapAnalysisTimeoutException("Surveyor motif investigation timed out.", error);
        }
        catch (HttpRequestException error)
        {
            throw new MapAnalysisUnavailableException("Surveyor motif investigation could not be reached.", error);
        }
    }

    private static PeriodicMotifInvestigation Unsupported(string reason) =>
        new("unsupported", reason, false, "experimental", null, null, null, null, null);

    private static void CheckHttpStatus(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new MapAnalysisAuthenticationException("Surveyor rejected the internal service credential.");
        if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
            throw new MapAnalysisTimeoutException("Surveyor timed out.");
        if (response.StatusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable)
            throw new MapAnalysisUnavailableException("Surveyor analysis capacity is unavailable.");
        if (!response.IsSuccessStatusCode)
            throw new MapAnalysisProtocolException(
                $"Surveyor returned HTTP {(int)response.StatusCode} during motif investigation.");
    }

    private static async Task<JsonDocument> ReadBoundedJsonAsync(
        HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is { } length && length > limit)
            throw new MapAnalysisProtocolException("Surveyor JSON response exceeds the size limit.");
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            int count = await body.ReadAsync(chunk, token);
            if (count == 0) break;
            if (buffer.Length + count > limit)
                throw new MapAnalysisProtocolException("Surveyor JSON response exceeds the size limit.");
            buffer.Write(chunk, 0, count);
        }
        try
        {
            return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException error)
        {
            throw new MapAnalysisProtocolException("Surveyor returned malformed investigation JSON.", error);
        }
    }

    private static bool AdvertisesSafeInvestigation(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("capabilities", out var capabilities)
            || capabilities.ValueKind != JsonValueKind.Array
            || capabilities.GetArrayLength() > 100)
            return false;
        int matching = 0;
        foreach (var item in capabilities.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String
                || id.GetString() != Capability)
                continue;
            matching++;
            if (matching > 1 || !IsString(item, "path", "/" + Path)
                || !IsString(item, "apiVersion", "v3")
                || !IsString(item, "maturity", "experimental")
                || !IsFalse(item, "authoritative")
                || !IsFalse(item, "acceptedWorldTopology")
                || !IsFalse(item, "supportsExpectedSymbol"))
                return false;
        }
        return matching == 1;
    }

    private static bool IsString(JsonElement item, string key, string expected) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
    private static bool IsFalse(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.False;

    private static JsonElement Object(JsonElement parent, string key)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.Object)
            return child;
        throw new MapAnalysisProtocolException($"Surveyor investigation is missing object '{key}'.");
    }

    private static JsonElement Array(JsonElement parent, string key, int min, int max)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.Array
            && child.GetArrayLength() >= min && child.GetArrayLength() <= max)
            return child;
        throw new MapAnalysisProtocolException($"Surveyor investigation has invalid array '{key}'.");
    }

    private static string String(JsonElement parent, string key, int maxLength = 4096)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.String
            && child.GetString() is { Length: > 0 } value && value.Length <= maxLength)
            return value;
        throw new MapAnalysisProtocolException($"Surveyor investigation has invalid string '{key}'.");
    }

    private static double Number(JsonElement parent, string key, double min, double max)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.Number
            && child.TryGetDouble(out var value) && double.IsFinite(value)
            && value >= min && value <= max)
            return value;
        throw new MapAnalysisProtocolException($"Surveyor investigation has invalid number '{key}'.");
    }

    private static int Integer(JsonElement parent, string key, int min, int max)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.Number
            && child.TryGetInt32(out var value) && value >= min && value <= max)
            return value;
        throw new MapAnalysisProtocolException($"Surveyor investigation has invalid integer '{key}'.");
    }

    private static long WireInteger(JsonElement parent, string key)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.Number
            && child.TryGetInt64(out long value)
            && value >= -PeriodicTopologyContractVersion.MaxWireTranslation
            && value <= PeriodicTopologyContractVersion.MaxWireTranslation)
            return value;
        throw new MapAnalysisProtocolException($"Surveyor investigation has invalid lattice offset '{key}'.");
    }

    private static MapAnalysisPoint Point(JsonElement parent)
        => new(Number(parent, "x", -1e9, 1e9), Number(parent, "y", -1e9, 1e9));

    private static PeriodicMotifInvestigation Parse(JsonElement root)
    {
        if (!IsString(root, "apiVersion", "v3") || !IsString(root, "capability", Capability)
            || !IsString(root, "maturity", "experimental") || !IsFalse(root, "authoritative"))
            throw new MapAnalysisProtocolException("Surveyor supplied an unsupported or authoritative investigation contract.");
        string status = String(root, "status");
        if (status is not ("consistent-candidate" or "ambiguous" or "inconclusive"))
            throw new MapAnalysisProtocolException("Surveyor investigation status is unknown.");
        string reason = String(root, "reason");
        var source = Object(root, "source");
        var sourceDimensions = new MapAnalysisSource(
            Integer(source, "width", 1, 100_000), Integer(source, "height", 1, 100_000),
            String(source, "mediaType", 80));
        var analysis = Object(root, "analysis");
        var analysisDetails = new MapAnalysisRaster(
            Integer(analysis, "width", 1, sourceDimensions.Width),
            Integer(analysis, "height", 1, sourceDimensions.Height),
            Number(analysis, "scale", 1e-9, 1),
            analysis.TryGetProperty("sourceResolutionVerified", out var verified)
                && verified.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? verified.GetBoolean()
                : throw new MapAnalysisProtocolException("Missing source-resolution verification status."));
        if (analysisDetails.SourceResolutionVerified
            && (analysisDetails.Width != sourceDimensions.Width
                || analysisDetails.Height != sourceDimensions.Height
                || Math.Abs(analysisDetails.Scale - 1) > 1e-9))
            throw new MapAnalysisProtocolException("Surveyor source-resolution verification conflicts with image dimensions.");

        if (!root.TryGetProperty("candidate", out var candidateJson)
            || !root.TryGetProperty("evidence", out var evidenceJson))
            throw new MapAnalysisProtocolException("Surveyor investigation candidate and evidence are required.");
        if (status != "consistent-candidate")
        {
            if (candidateJson.ValueKind != JsonValueKind.Null || evidenceJson.ValueKind != JsonValueKind.Null)
                throw new MapAnalysisProtocolException("Inconclusive investigation must not assert a candidate.");
            return new(status, reason, false, "experimental", "v3", null, null,
                sourceDimensions, analysisDetails);
        }
        if (candidateJson.ValueKind != JsonValueKind.Object
            || evidenceJson.ValueKind != JsonValueKind.Object)
            throw new MapAnalysisProtocolException("Consistent investigation must carry candidate and evidence.");

        string symbol = String(candidateJson, "dsSymbol", 131_072);
        var parsed = DelaneyDressTopology.Inspect(symbol, 2048);
        if (parsed.Status != DelaneyDressStatus.Euclidean
            || parsed.Symbol!.Canonical != symbol)
            throw new MapAnalysisProtocolException("Surveyor reported an invalid or noncanonical Euclidean candidate.");

        var basisJson = Array(candidateJson, "translationBasisSourcePixels", 2, 2);
        var basis = basisJson.EnumerateArray().Select(Point).ToArray();
        double determinant = basis[0].X * basis[1].Y - basis[0].Y * basis[1].X;
        double firstLength = Math.Sqrt(basis[0].X * basis[0].X + basis[0].Y * basis[0].Y);
        double secondLength = Math.Sqrt(basis[1].X * basis[1].X + basis[1].Y * basis[1].Y);
        double relativeArea = determinant / (firstLength * secondLength);
        if (!double.IsFinite(relativeArea) || Math.Abs(relativeArea) <= 1e-6)
            throw new MapAnalysisProtocolException("Surveyor translation basis is degenerate.");

        var cellsJson = Array(candidateJson, "motifCells", 1, 24);
        var cells = new List<ObservedMotifCell>();
        foreach (var cell in cellsJson.EnumerateArray())
        {
            string id = String(cell, "provisionalId", 128);
            var polygon = Array(cell, "polygonSourcePixels", 3, 12)
                .EnumerateArray().Select(Point).ToArray();
            if (polygon.Any(point => point.X < -16 || point.Y < -16
                || point.X > sourceDimensions.Width + 16
                || point.Y > sourceDimensions.Height + 16))
                throw new MapAnalysisProtocolException("Surveyor polygon extends outside supported source-image coordinates.");
            var boundaries = new List<ObservedMotifBoundary>();
            foreach (var edge in Array(cell, "boundaries", polygon.Length, polygon.Length).EnumerateArray())
            {
                var translation = Object(edge, "translation");
                boundaries.Add(new(
                    Integer(edge, "sideIndex", 0, polygon.Length - 1),
                    String(edge, "targetProvisionalId", 128),
                    Integer(edge, "targetSideIndex", 0, 11),
                    WireInteger(translation, "u"), WireInteger(translation, "v"),
                    Integer(edge, "supportingObservations", 1, 1_000_000)));
            }
            cells.Add(new(id, polygon, boundaries));
        }

        // Independently reconstruct the entire claimed translational
        // chamber graph, including primitive Z² connectivity and zero vertex
        // holonomy. This is only structural consistency; the noisy raster
        // polygon measurements are NOT certified world-space geometry.
        var witness = new PeriodicTopologyWitness(
            PeriodicTopologyContractVersion.Current, symbol, symbol,
            cells.Select(cell => new PeriodicMotifCell(
                cell.ProvisionalId,
                cell.Boundaries.Select(edge => new PeriodicEdgeInterface(
                    edge.SideIndex, edge.SideIndex, edge.TargetProvisionalId,
                    new LatticeDisplacement(edge.TranslationU, edge.TranslationV),
                    edge.TargetSideIndex)).ToArray())).ToArray(),
            "observed:surveyor-v3-experimental-untrusted");
        try
        {
            witness.ValidateAdjacency();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or OverflowException or KeyNotFoundException)
        {
            throw new MapAnalysisProtocolException(
                "Surveyor candidate failed independent translational topology validation.", error);
        }
        var evidence = new PeriodicMotifEvidence(
            Integer(evidenceJson, "matchedHypotheses", 1, 10_000),
            Integer(evidenceJson, "checkedHypotheses", 1, 10_000),
            Integer(evidenceJson, "rejectedHypotheses", 0, 10_000),
            Integer(evidenceJson, "minimumEdgeObservations", 1, 1_000_000),
            Number(evidenceJson, "originalRasterEdgeSupport", 0, 1),
            Number(evidenceJson, "maximumRigidVertexResidualSourcePixels", 0, 1e9),
            evidenceJson.TryGetProperty("translationRefinementResidualSourcePixels", out var refinement)
                && refinement.ValueKind == JsonValueKind.Null
                ? null : Number(evidenceJson, "translationRefinementResidualSourcePixels", 0, 1e9));
        if (evidence.MatchedHypotheses > evidence.CheckedHypotheses
            || evidence.RejectedHypotheses != evidence.CheckedHypotheses - evidence.MatchedHypotheses)
            throw new MapAnalysisProtocolException("Surveyor hypothesis counts are inconsistent.");

        return new(status, reason, false, "experimental", "v3",
            new(symbol, basis, cells), evidence, sourceDimensions, analysisDetails);
    }
}
