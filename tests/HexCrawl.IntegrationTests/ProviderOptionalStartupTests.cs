using System.Net;

namespace HexCrawl.IntegrationTests;

public sealed class ProviderOptionalStartupTests
{
    [Fact]
    public async Task HexCrawlStartsWithoutToolHostOrRulesCoreProviderConfiguration()
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            await using var factory = TestWebHost.Create(database);
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
