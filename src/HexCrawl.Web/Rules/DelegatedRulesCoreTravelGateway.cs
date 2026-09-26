using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application.Hosting;
using HexCrawl.Application.Rules;
using HexCrawl.Web.Authentication;

namespace HexCrawl.Web.Rules;

public sealed class DelegatedRulesCoreTravelGateway(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ILogger<DelegatedRulesCoreTravelGateway> logger)
    : IRulesCoreTravelGateway
{
    private const string TargetSlug = "rules-core";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<TravelEnvironmentCatalogView> GetCatalogAsync(
        Guid? campaignId,
        CancellationToken cancellationToken = default)
    {
        var path = campaignId.HasValue
            ? $"/api/campaigns/{campaignId.Value:D}/rules/travel-environment"
            : "/api/rules/travel-environment";
        return SendRequiredJsonAsync<TravelEnvironmentCatalogView>(
            HttpMethod.Get,
            path,
            content: null,
            cancellationToken);
    }

    public Task<TravelEnvironmentEvaluationView?> ResolveAsync(
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

        var escaped = Uri.EscapeDataString(mechanicKey.Trim());
        var path = campaignId.HasValue
            ? $"/api/campaigns/{campaignId.Value:D}/rules/travel-environment/{escaped}/resolve"
            : $"/api/rules/travel-environment/{escaped}/resolve";
        return SendJsonAsync<TravelEnvironmentEvaluationView>(
            HttpMethod.Post,
            path,
            JsonContent.Create(request, options: JsonOptions),
            allowNotFound: true,
            cancellationToken);
    }

    private async Task<T> SendRequiredJsonAsync<T>(
        HttpMethod method,
        string targetPath,
        HttpContent? content,
        CancellationToken cancellationToken)
        where T : class
    {
        var result = await SendJsonAsync<T>(
            method,
            targetPath,
            content,
            allowNotFound: false,
            cancellationToken);
        return result ?? throw new RulesCoreTravelGatewayException("Rules Core returned an empty response.");
    }

    private async Task<T?> SendJsonAsync<T>(
        HttpMethod method,
        string targetPath,
        HttpContent? content,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where T : class
    {
        var (capability, delegationPrefix) = GetDelegation();
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
            throw new RulesCoreTravelGatewayException("Rules Core transport is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Delegated Rules Core travel request timed out.");
            throw new RulesCoreTravelGatewayException("Rules Core transport is unavailable.", exception);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Delegated Rules Core travel request {Method} {Path} returned status {StatusCode}.",
                    method,
                    targetPath,
                    (int)response.StatusCode);
                var message = response.StatusCode == HttpStatusCode.BadRequest
                    ? "Rules Core rejected the travel/environment mechanic inputs."
                    : "Rules Core is unavailable for travel/environment resolution.";
                throw new RulesCoreTravelGatewayException(message);
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return value ?? throw new RulesCoreTravelGatewayException("Rules Core returned an empty response.");
            }
            catch (Exception exception) when (exception is JsonException
                or NotSupportedException
                or HttpRequestException)
            {
                logger.LogWarning(exception, "Rules Core returned an unreadable travel/environment response.");
                throw new RulesCoreTravelGatewayException("Rules Core returned an invalid travel/environment response.", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Rules Core travel/environment response reading timed out.");
                throw new RulesCoreTravelGatewayException("Rules Core transport is unavailable.", exception);
            }
        }
    }

    private (string Capability, string DelegationPrefix) GetDelegation()
    {
        if (httpClient.BaseAddress is null)
        {
            throw new RulesCoreTravelGatewayException(
                "ToolHost:BaseUrl is not configured for Rules Core delegation.");
        }

        var httpContext = httpContextAccessor.HttpContext;
        var authenticationContext = httpContext is null
            ? null
            : HostedToolAuthenticationMiddleware.GetAuthenticationContext(httpContext);
        if (authenticationContext is null
            || string.IsNullOrWhiteSpace(authenticationContext.DelegationCapability)
            || string.IsNullOrWhiteSpace(authenticationContext.DelegationPath))
        {
            throw new RulesCoreTravelGatewayException(
                "The Site did not issue Hex Crawl a Rules Core delegation capability.");
        }

        var prefix = authenticationContext.DelegationPath.Replace(
            "{targetSlug}",
            TargetSlug,
            StringComparison.Ordinal);
        return (authenticationContext.DelegationCapability, prefix);
    }
}
