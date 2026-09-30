using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application.Rules;
using HexCrawl.Web.Authentication;

namespace HexCrawl.Web.Rules;

public sealed class RulesCoreTravelEnvironmentProvider(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ILogger<RulesCoreTravelEnvironmentProvider> logger)
    : ITravelEnvironmentProvider
{
    private const string TargetSlug = "rules-core";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TravelEnvironmentProviderMetadata Metadata { get; } = new(
        "rules-core",
        "Rules Core",
        IsDefault: true);

    public async Task<TravelEnvironmentProviderCatalogResult> GetCatalogAsync(
        Guid? campaignId,
        CancellationToken cancellationToken = default)
    {
        var path = campaignId.HasValue
            ? $"/api/campaigns/{campaignId.Value:D}/rules/travel-environment"
            : "/api/rules/travel-environment";
        var transport = await SendJsonAsync<RulesCoreTravelEnvironmentCatalogDto>(
            HttpMethod.Get,
            path,
            content: null,
            allowNotFound: false,
            cancellationToken);
        if (transport.Kind != ProviderTransportKind.Success || transport.Value is null)
        {
            return new TravelEnvironmentProviderCatalogResult(
                Metadata,
                AvailabilityFor(transport.Kind),
                null,
                transport.Detail);
        }

        return new TravelEnvironmentProviderCatalogResult(
            Metadata,
            TravelEnvironmentProviderAvailabilityStates.Available,
            transport.Value.ToApplication(),
            null);
    }

    public async Task<TravelEnvironmentProviderResolutionResult> ResolveAsync(
        Guid? campaignId,
        string mechanicKey,
        TravelEnvironmentResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mechanicKey))
        {
            throw new ArgumentException("Travel/environment mechanic key can not be blank.", nameof(mechanicKey));
        }
        ArgumentNullException.ThrowIfNull(request);

        var normalizedKey = mechanicKey.Trim();
        var escaped = Uri.EscapeDataString(normalizedKey);
        var path = campaignId.HasValue
            ? $"/api/campaigns/{campaignId.Value:D}/rules/travel-environment/{escaped}/resolve"
            : $"/api/rules/travel-environment/{escaped}/resolve";
        var transport = await SendJsonAsync<RulesCoreTravelEnvironmentEvaluationDto>(
            HttpMethod.Post,
            path,
            JsonContent.Create(request, options: JsonOptions),
            allowNotFound: true,
            cancellationToken);

        if (transport.Kind == ProviderTransportKind.NotFound)
        {
            return new TravelEnvironmentProviderResolutionResult(
                Metadata,
                normalizedKey,
                TravelEnvironmentProviderResolutionStates.Unsupported,
                null,
                [],
                transport.Detail ?? "Rules Core does not expose this capability in the effective rules.");
        }
        if (transport.Kind != ProviderTransportKind.Success || transport.Value is null)
        {
            var status = transport.Kind == ProviderTransportKind.Unavailable
                ? TravelEnvironmentProviderResolutionStates.Unavailable
                : TravelEnvironmentProviderResolutionStates.Failed;
            return new TravelEnvironmentProviderResolutionResult(
                Metadata,
                normalizedKey,
                status,
                null,
                [],
                transport.Detail);
        }

        var evaluation = transport.Value.ToApplication();
        return new TravelEnvironmentProviderResolutionResult(
            Metadata,
            evaluation.MechanicKey,
            ResolutionStatus(evaluation),
            evaluation,
            evaluation.MissingInputKeys,
            null);
    }

    private async Task<ProviderTransportResult<T>> SendJsonAsync<T>(
        HttpMethod method,
        string targetPath,
        HttpContent? content,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!TryGetDelegation(out var capability, out var delegationPrefix, out var unavailableDetail))
        {
            return new ProviderTransportResult<T>(
                ProviderTransportKind.Unavailable,
                null,
                unavailableDetail);
        }

        using var request = new HttpRequestMessage(method, $"{delegationPrefix}{targetPath}")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", capability);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Delegated Rules Core travel request failed before a response was received.");
            return new ProviderTransportResult<T>(
                ProviderTransportKind.Failed,
                null,
                "Rules Core transport is unreachable.");
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Delegated Rules Core travel request timed out.");
            return new ProviderTransportResult<T>(
                ProviderTransportKind.Failed,
                null,
                "Rules Core transport timed out.");
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ProviderTransportResult<T>(
                    ProviderTransportKind.NotFound,
                    null,
                    "Rules Core does not expose this capability in the effective rules.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Delegated Rules Core travel request {Method} {Path} returned status {StatusCode}.",
                    method,
                    targetPath,
                    (int)response.StatusCode);
                var detail = response.StatusCode == HttpStatusCode.BadRequest
                    ? "Rules Core rejected the travel/environment capability inputs."
                    : $"Rules Core returned HTTP {(int)response.StatusCode}.";
                return new ProviderTransportResult<T>(ProviderTransportKind.Failed, null, detail);
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return value is null
                    ? new ProviderTransportResult<T>(
                        ProviderTransportKind.Failed,
                        null,
                        "Rules Core returned an empty response.")
                    : new ProviderTransportResult<T>(ProviderTransportKind.Success, value, null);
            }
            catch (Exception exception) when (exception is JsonException
                or NotSupportedException
                or HttpRequestException)
            {
                logger.LogWarning(exception, "Rules Core returned an unreadable travel/environment response.");
                return new ProviderTransportResult<T>(
                    ProviderTransportKind.Failed,
                    null,
                    "Rules Core returned an invalid travel/environment response.");
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Rules Core travel/environment response reading timed out.");
                return new ProviderTransportResult<T>(
                    ProviderTransportKind.Failed,
                    null,
                    "Rules Core response reading timed out.");
            }
        }
    }

    private bool TryGetDelegation(
        out string capability,
        out string delegationPrefix,
        out string detail)
    {
        capability = string.Empty;
        delegationPrefix = string.Empty;
        detail = string.Empty;

        if (httpClient.BaseAddress is null)
        {
            detail = "ToolHost:BaseUrl is not configured for the Rules Core adapter.";
            return false;
        }

        var httpContext = httpContextAccessor.HttpContext;
        var authenticationContext = httpContext is null
            ? null
            : HostedToolAuthenticationMiddleware.GetAuthenticationContext(httpContext);
        if (authenticationContext is null
            || string.IsNullOrWhiteSpace(authenticationContext.DelegationCapability)
            || string.IsNullOrWhiteSpace(authenticationContext.DelegationPath))
        {
            detail = "The Site did not issue Hex Crawl a Rules Core delegation capability.";
            return false;
        }

        capability = authenticationContext.DelegationCapability;
        delegationPrefix = authenticationContext.DelegationPath.Replace(
            "{targetSlug}",
            TargetSlug,
            StringComparison.Ordinal);
        return true;
    }

    private static string AvailabilityFor(ProviderTransportKind kind) => kind switch
    {
        ProviderTransportKind.Unavailable => TravelEnvironmentProviderAvailabilityStates.Unavailable,
        ProviderTransportKind.Success => TravelEnvironmentProviderAvailabilityStates.Available,
        _ => TravelEnvironmentProviderAvailabilityStates.Failed
    };

    private static string ResolutionStatus(TravelEnvironmentEvaluationView evaluation)
    {
        if (string.Equals(
                evaluation.MechanicState,
                TravelEnvironmentMechanicStates.Conflicted,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                evaluation.MechanicState,
                TravelEnvironmentMechanicStates.RequiresAdjudication,
                StringComparison.OrdinalIgnoreCase))
        {
            return TravelEnvironmentProviderResolutionStates.RequiresAdjudication;
        }
        if (!string.Equals(
                evaluation.MechanicState,
                TravelEnvironmentMechanicStates.Resolved,
                StringComparison.OrdinalIgnoreCase))
        {
            return TravelEnvironmentProviderResolutionStates.Failed;
        }

        if (string.Equals(
                evaluation.EvaluationState,
                TravelEnvironmentEvaluationStates.InputRequired,
                StringComparison.OrdinalIgnoreCase))
        {
            return TravelEnvironmentProviderResolutionStates.InputRequired;
        }
        if (string.Equals(
                evaluation.EvaluationState,
                TravelEnvironmentEvaluationStates.NotApplicable,
                StringComparison.OrdinalIgnoreCase))
        {
            return TravelEnvironmentProviderResolutionStates.NotApplicable;
        }
        return string.Equals(
            evaluation.EvaluationState,
            TravelEnvironmentEvaluationStates.Resolved,
            StringComparison.OrdinalIgnoreCase)
                ? TravelEnvironmentProviderResolutionStates.Resolved
                : TravelEnvironmentProviderResolutionStates.Failed;
    }

    private enum ProviderTransportKind
    {
        Success,
        NotFound,
        Unavailable,
        Failed
    }

    private sealed record ProviderTransportResult<T>(
        ProviderTransportKind Kind,
        T? Value,
        string? Detail)
        where T : class;

    private sealed record RulesCoreTravelEnvironmentCatalogDto(
        string Scope,
        Guid? CampaignId,
        int? RevisionNumber,
        DateTimeOffset? PublishedAt,
        IReadOnlyList<TravelEnvironmentMechanicView> Mechanics)
    {
        public TravelEnvironmentCatalogView ToApplication() => new(
            Scope,
            CampaignId,
            RevisionNumber,
            PublishedAt,
            Mechanics);
    }

    private sealed record RulesCoreTravelEnvironmentEvaluationDto(
        string MechanicKey,
        string MechanicState,
        string EvaluationState,
        TravelEnvironmentQuantity? Quantity,
        decimal? Factor,
        TravelEnvironmentCheckResolution? Check,
        IReadOnlyList<string> MissingInputKeys,
        IReadOnlyList<TravelEnvironmentSourceAttributionView> SourceAttributions)
    {
        public TravelEnvironmentEvaluationView ToApplication() => new(
            MechanicKey,
            MechanicState,
            EvaluationState,
            Quantity,
            Factor,
            Check,
            MissingInputKeys,
            SourceAttributions);
    }
}
