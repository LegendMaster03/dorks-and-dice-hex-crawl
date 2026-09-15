using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HexCrawl.IntegrationTests;

public sealed class RuntimeEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RuntimeEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RuntimeProfilesExposeAdvancedAndSimplifiedProcedures()
    {
        var profiles = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/demo/runtime/profiles");

        Assert.Equal(3, profiles.GetArrayLength());
        Assert.Contains(profiles.EnumerateArray(), profile => profile.GetProperty("key").GetString() == "alexandrian-advanced");
        Assert.Contains(profiles.EnumerateArray(), profile => profile.GetProperty("key").GetString() == "simple-fixed-distance");
        Assert.Contains(profiles.EnumerateArray(), profile => profile.GetProperty("key").GetString() == "simple-hex-step");
    }

    [Fact]
    public async Task ResetUsesSelectedProcedureAndPhysicalGridScale()
    {
        using var response = await _client.PostAsJsonAsync("/api/demo/runtime/reset", new
        {
            profileKey = "simple-fixed-distance",
            orientation = "flat",
            scale = 6,
            unit = "km"
        });
        response.EnsureSuccessStatusCode();
        var runtime = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal("simple-fixed-distance", runtime.GetProperty("profile").GetProperty("key").GetString());
        Assert.Equal(6, runtime.GetProperty("hexCenterDistance").GetProperty("value").GetDouble());
        Assert.Equal("km", runtime.GetProperty("hexCenterDistance").GetProperty("unit").GetProperty("symbol").GetString());
        Assert.Equal(0, runtime.GetProperty("expedition").GetProperty("currentHex").GetProperty("q").GetInt32());
    }

    [Fact]
    public async Task RuntimeAdvanceCrossesBoundaryWithoutDiscoveringKeyedLocation()
    {
        await ResetSimpleRuntime();
        using var response = await _client.PostAsJsonAsync("/api/demo/runtime/advance", new
        {
            intendedDirection = 0,
            paceKey = "normal",
            activities = Array.Empty<string>(),
            navigationAidKey = "none",
            expectedDistance = 8,
            actualDistance = 8,
            resolutionSource = "ManualRoll",
            deliberateDoubleBack = false,
            continueAcrossBoundaries = true
        });
        response.EnsureSuccessStatusCode();
        var runtime = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(1, runtime.GetProperty("expedition").GetProperty("currentHex").GetProperty("q").GetInt32());
        Assert.Equal(2, runtime.GetProperty("expedition").GetProperty("hexProgress").GetProperty("value").GetDouble());
        Assert.Empty(runtime.GetProperty("knowledge").EnumerateArray());
        Assert.Contains(runtime.GetProperty("history").EnumerateArray(), item => item.GetProperty("kind").GetString() == "HexEntered");
    }

    [Fact]
    public async Task ManualDiscoveryAddsOnlyRequestedSubject()
    {
        await ResetSimpleRuntime();
        var world = await _client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/demo/world");
        var locationId = world.GetProperty("locations")[0].GetProperty("id").GetGuid();

        using var response = await _client.PostAsJsonAsync("/api/demo/runtime/discover", new
        {
            subjectId = locationId,
            subjectType = "Location",
            source = "integration-test"
        });
        response.EnsureSuccessStatusCode();
        var runtime = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Single(runtime.GetProperty("knowledge").EnumerateArray());
        var entry = runtime.GetProperty("knowledge")[0];
        Assert.Equal(locationId, entry.GetProperty("subjectId").GetGuid());
        Assert.Equal("Discovered", entry.GetProperty("state").GetString());
    }

    [Fact]
    public async Task AdvancedWatchRejectsMissingNavigationOutcome()
    {
        using var reset = await _client.PostAsJsonAsync("/api/demo/runtime/reset", new { profileKey = "alexandrian-advanced" });
        reset.EnsureSuccessStatusCode();

        using var response = await _client.PostAsJsonAsync("/api/demo/runtime/advance", new
        {
            intendedDirection = 0,
            paceKey = "normal",
            activities = Array.Empty<string>(),
            navigationAidKey = "none",
            expectedDistance = 12,
            actualDistance = 12,
            resolutionSource = "ManualRoll",
            encounterOutcome = "none",
            deliberateDoubleBack = false,
            continueAcrossBoundaries = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task ResetSimpleRuntime()
    {
        using var response = await _client.PostAsJsonAsync("/api/demo/runtime/reset", new
        {
            profileKey = "simple-fixed-distance",
            orientation = "pointy",
            scale = 12,
            unit = "mi"
        });
        response.EnsureSuccessStatusCode();
    }
}
