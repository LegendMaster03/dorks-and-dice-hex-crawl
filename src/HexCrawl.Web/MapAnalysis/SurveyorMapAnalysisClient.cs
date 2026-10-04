using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HexCrawl.Application;
using Microsoft.Extensions.Options;

namespace HexCrawl.Web.MapAnalysis;

public sealed class SurveyorMapAnalysisClient(
    HttpClient httpClient,
    IOptions<SurveyorOptions> configuredOptions,
    ILogger<SurveyorMapAnalysisClient> logger) : IMapAnalysisService
{
    private const string ApiVersion = "v1";
    private const string Capability = "map.hex-grid.detect";

    public async Task<MapHexGridAnalysis> DetectHexGridAsync(
        Stream raster,
        string mediaType,
        MapAnalysisOptions options,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var configuration = configuredOptions.Value;
        if (!configuration.IsConfigured)
        {
            throw new MapAnalysisUnavailableException("Surveyor is not configured.");
        }

        var requestUri = BuildRequestUri(configuration.BaseUrl, options);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ServiceToken);
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }
        var content = new StreamContent(raster);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        request.Content = content;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(configuration.RequestTimeout);
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MapAnalysisTimeoutException("Surveyor did not complete map analysis before the configured timeout.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MapAnalysisUnavailableException("Surveyor could not be reached.", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new MapAnalysisAuthenticationException("Surveyor rejected the internal service credential.");
            }
            if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
            {
                throw new MapAnalysisTimeoutException("Surveyor reported an analysis timeout.");
            }
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                throw new MapAnalysisUnavailableException("Surveyor analysis capacity is currently unavailable.");
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Surveyor returned HTTP {StatusCode} for map hex-grid analysis.",
                    (int)response.StatusCode);
                throw new MapAnalysisUnavailableException($"Surveyor analysis failed with HTTP {(int)response.StatusCode}.");
            }

            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
                return Parse(document.RootElement);
            }
            catch (JsonException exception)
            {
                throw new MapAnalysisProtocolException("Surveyor returned malformed JSON.", exception);
            }
        }
    }

    private static Uri BuildRequestUri(string baseUrl, MapAnalysisOptions options)
    {
        var parameters = new List<string>();
        AddDouble("minimumSpacingPixels", options.MinimumSpacingPixels);
        AddDouble("maximumSpacingPixels", options.MaximumSpacingPixels);
        AddInteger("maximumEdgeSamples", options.MaximumEdgeSamples);
        AddDouble("minimumConfidence", options.MinimumConfidence);
        var relative = "/v1/hex-grid/detect" + (parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters));
        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), relative.TrimStart('/'));

        void AddDouble(string name, double? value)
        {
            if (value is null) return;
            if (!double.IsFinite(value.Value))
                throw new ArgumentOutOfRangeException(nameof(options), $"{name} must be finite.");
            parameters.Add($"{name}={Uri.EscapeDataString(value.Value.ToString("R", CultureInfo.InvariantCulture))}");
        }

        void AddInteger(string name, int? value)
        {
            if (value is null) return;
            parameters.Add($"{name}={value.Value.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static MapHexGridAnalysis Parse(JsonElement root)
    {
        var apiVersion = RequiredString(root, "apiVersion");
        var capability = RequiredString(root, "capability");
        if (!string.Equals(apiVersion, ApiVersion, StringComparison.Ordinal)
            || !string.Equals(capability, Capability, StringComparison.Ordinal))
        {
            throw new MapAnalysisProtocolException("Surveyor returned an unsupported API version or capability identity.");
        }

        var status = RequiredString(root, "status");
        if (status is not ("detected" or "inconclusive" or "gridless"))
        {
            throw new MapAnalysisProtocolException("Surveyor returned an unknown analysis status.");
        }
        var reason = RequiredString(root, "reason");
        var sourceElement = RequiredObject(root, "source");
        var analysisElement = RequiredObject(root, "analysis");
        var source = new MapAnalysisSource(
            RequiredPositiveInt(sourceElement, "width"),
            RequiredPositiveInt(sourceElement, "height"),
            RequiredString(sourceElement, "mediaType"));
        var analysis = new MapAnalysisRaster(
            RequiredPositiveInt(analysisElement, "width"),
            RequiredPositiveInt(analysisElement, "height"),
            RequiredFinitePositive(analysisElement, "scale"),
            RequiredBoolean(analysisElement, "sourceResolutionVerified"));

        MapHexGridFit? fit = null;
        if (root.TryGetProperty("fit", out var fitElement) && fitElement.ValueKind != JsonValueKind.Null)
        {
            if (fitElement.ValueKind != JsonValueKind.Object)
                throw new MapAnalysisProtocolException("Surveyor fit must be an object or null.");
            var orientation = RequiredString(fitElement, "orientation");
            if (orientation is not ("PointyTop" or "FlatTop"))
                throw new MapAnalysisProtocolException("Surveyor returned an unknown hex-grid orientation.");
            var anchor = RequiredObject(fitElement, "anchorPixel");
            fit = new MapHexGridFit(
                orientation,
                RequiredFinite(fitElement, "rotationDegrees"),
                RequiredFinitePositive(fitElement, "centerSpacingPixels"),
                new MapAnalysisPoint(RequiredFinite(anchor, "x"), RequiredFinite(anchor, "y")),
                RequiredUnit(fitElement, "confidence"),
                RequiredFiniteNonNegative(fitElement, "residualPixels"),
                RequiredUnit(fitElement, "supportCoverage"),
                RequiredUnit(fitElement, "orientationSupport"),
                RequiredUnit(fitElement, "translationScore"),
                RequiredUnit(fitElement, "competingTranslationScore"),
                RequiredUnit(fitElement, "linePeriodicityScore"),
                RequiredUnit(fitElement, "phaseScore"));
        }
        if (status == "detected" && fit is null)
            throw new MapAnalysisProtocolException("A detected Surveyor result must include a fit.");

        return new MapHexGridAnalysis(apiVersion, capability, status, reason, source, analysis, fit);
    }

    private static JsonElement RequiredObject(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : throw new MapAnalysisProtocolException($"Surveyor response is missing object '{name}'.");

    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            && property.GetString() is { Length: > 0 } text
            ? text
            : throw new MapAnalysisProtocolException($"Surveyor response is missing string '{name}'.");

    private static bool RequiredBoolean(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : throw new MapAnalysisProtocolException($"Surveyor response is missing boolean '{name}'.");

    private static int RequiredPositiveInt(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || !property.TryGetInt32(out var result) || result <= 0)
            throw new MapAnalysisProtocolException($"Surveyor response field '{name}' must be a positive integer.");
        return result;
    }

    private static double RequiredFinite(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || !property.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new MapAnalysisProtocolException($"Surveyor response field '{name}' must be finite.");
        return result;
    }

    private static double RequiredFinitePositive(JsonElement value, string name)
    {
        var result = RequiredFinite(value, name);
        if (result <= 0) throw new MapAnalysisProtocolException($"Surveyor response field '{name}' must be positive.");
        return result;
    }

    private static double RequiredFiniteNonNegative(JsonElement value, string name)
    {
        var result = RequiredFinite(value, name);
        if (result < 0) throw new MapAnalysisProtocolException($"Surveyor response field '{name}' must be non-negative.");
        return result;
    }

    private static double RequiredUnit(JsonElement value, string name)
    {
        var result = RequiredFinite(value, name);
        if (result < 0 || result > 1) throw new MapAnalysisProtocolException($"Surveyor response field '{name}' must be between 0 and 1.");
        return result;
    }
}
