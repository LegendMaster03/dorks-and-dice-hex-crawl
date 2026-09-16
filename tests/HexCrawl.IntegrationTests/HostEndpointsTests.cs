using System.Net;

namespace HexCrawl.IntegrationTests;

public sealed class HostEndpointsTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    [InlineData("/api")]
    public async Task FoundationEndpointsAreAvailable(string path)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Theory]
    [InlineData("/worlds")]
    [InlineData("/worlds/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/edit")]
    [InlineData("/worlds/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/expeditions/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    public async Task StandaloneDeepRoutesReturnApplicationShell(string path)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            var html = await client.GetStringAsync(path);
            Assert.Contains("id=\"tool-root\"", html, StringComparison.Ordinal);
            Assert.Contains("/app.js", html, StringComparison.Ordinal);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task UnknownApiPathDoesNotFallBackToHtmlShell()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/api/not-a-route");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
