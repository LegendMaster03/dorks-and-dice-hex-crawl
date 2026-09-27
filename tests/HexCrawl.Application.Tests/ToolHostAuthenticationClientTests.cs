using System.Net;
using System.Net.Http.Headers;
using System.Text;
using HexCrawl.Application.Hosting;
using HexCrawl.Infrastructure.Hosting;

namespace HexCrawl.Application.Tests;

public sealed class ToolHostAuthenticationClientTests
{
    [Fact]
    public async Task RedeemsTicketAgainstFixedHexCrawlIntrospectionPath()
    {
        var handler = new RecordingHandler(includeDelegation: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://tool-host.internal/") };
        var client = new DorksAndDiceToolHostAuthenticationClient(http);

        var context = await client.RedeemAsync(
            "one-time-ticket",
            DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath);

        Assert.NotNull(context);
        Assert.Equal("hex-crawl", context.ToolSlug);
        Assert.Equal(DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath, handler.Path);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("one-time-ticket", handler.Authorization?.Parameter);
        Assert.Equal("ddtd_v1_test-capability", context.DelegationCapability);
        Assert.Equal(DorksAndDiceToolHostAuthenticationClient.ExpectedDelegationPath, context.DelegationPath);
    }

    [Fact]
    public async Task RejectsBrowserSuppliedAlternateIntrospectionPathBeforeNetworkCall()
    {
        var handler = new RecordingHandler(includeDelegation: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://tool-host.internal/") };
        var client = new DorksAndDiceToolHostAuthenticationClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.RedeemAsync("ticket", "/other/path"));
        Assert.Null(handler.Path);
    }

    [Fact]
    public async Task RejectsIncompleteDelegationHeaders()
    {
        var handler = new RecordingHandler(includeDelegation: false, includeCapabilityOnly: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://tool-host.internal/") };
        var client = new DorksAndDiceToolHostAuthenticationClient(http);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.RedeemAsync(
            "ticket",
            DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath));
    }

    private sealed class RecordingHandler(
        bool includeDelegation,
        bool includeCapabilityOnly = false) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization;
            const string json = """
                {"contractVersion":1,"toolSlug":"hex-crawl","siteMode":"dorks","user":{"id":"user-1","displayName":"DM"},"globalRoles":[],"campaigns":[]}
                """;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (includeDelegation || includeCapabilityOnly)
            {
                response.Headers.Add(ToolHostAuthenticationHeaders.DelegationCapability, "ddtd_v1_test-capability");
            }
            if (includeDelegation)
            {
                response.Headers.Add(
                    ToolHostAuthenticationHeaders.DelegationPath,
                    DorksAndDiceToolHostAuthenticationClient.ExpectedDelegationPath);
            }
            return Task.FromResult(response);
        }
    }
}
