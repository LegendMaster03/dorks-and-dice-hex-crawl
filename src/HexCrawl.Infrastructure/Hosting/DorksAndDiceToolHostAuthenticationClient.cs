using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application.Hosting;

namespace HexCrawl.Infrastructure.Hosting;

public sealed class DorksAndDiceToolHostAuthenticationClient(HttpClient httpClient)
    : IToolHostAuthenticationClient
{
    public const string ExpectedToolSlug = "hex-crawl";
    public const string ExpectedIntrospectionPath = "/tool-host/hex-crawl/api/introspect";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ToolHostAuthenticationContext?> RedeemAsync(
        string ticket,
        string introspectionPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            throw new ArgumentException("Tool Host ticket can not be blank.", nameof(ticket));
        }
        if (!string.Equals(introspectionPath, ExpectedIntrospectionPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Tool Host introspection path is not valid for Hex Crawl.");
        }
        if (httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException("ToolHost:BaseUrl must be configured before hosted authentication can be used.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ExpectedIntrospectionPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.Trim());
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        var context = await response.Content.ReadFromJsonAsync<ToolHostAuthenticationContext>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Tool Host introspection returned an empty authentication context.");
        Validate(context);
        return context;
    }

    private static void Validate(ToolHostAuthenticationContext context)
    {
        if (context.ContractVersion != 1)
        {
            throw new InvalidDataException($"Unsupported Tool Host authentication contract version '{context.ContractVersion}'.");
        }
        if (!string.Equals(context.ToolSlug, ExpectedToolSlug, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Tool Host authentication context was issued for another Tool.");
        }
        if (string.IsNullOrWhiteSpace(context.SiteMode))
        {
            throw new InvalidDataException("Tool Host authentication context does not include a site mode.");
        }
        if (context.User is null || string.IsNullOrWhiteSpace(context.User.Id))
        {
            throw new InvalidDataException("Tool Host authentication context does not include a stable user ID.");
        }
    }
}
