using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HexCrawl.IntegrationTests;

public sealed class HostEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HostEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    [InlineData("/api")]
    public async Task FoundationEndpointsAreAvailable(string path)
    {
        using var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DemoWorldExposesConfigurableGridWithoutPersistenceDependency()
    {
        using var response = await _client.GetAsync("/api/demo/world?orientation=flat&scale=6");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("FlatTop", document.GetProperty("grid").GetProperty("orientation").GetString());
        Assert.Equal(6, document.GetProperty("grid").GetProperty("neighborCenterDistance").GetProperty("value").GetDouble());
        Assert.True(document.GetProperty("features").GetArrayLength() >= 3);
    }
}
