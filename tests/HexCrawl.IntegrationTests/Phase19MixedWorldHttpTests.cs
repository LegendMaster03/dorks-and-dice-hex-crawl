using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.IntegrationTests;

/// <summary>
/// Real HTTP + persisted-world smoke checks for different motif shapes, including
/// subdivided atomic edges. Never infer traversal from vertex/axial indices.
/// </summary>
public sealed class Phase19MixedWorldHttpTests
{
    [Theory]
    [InlineData(
        "mixed triangle/square",
        "<20:2 7 6 10 12 13 15 17 20 19,3 5 9 12 10 13 16 18 19 20,4 6 8 11 12 14 15 17 19 20:3 3 4,5 5>",
        3, 4)]
    [InlineData(
        "rhombille",
        "<24:2 7 6 10 14 16 18 20 17 19 24 23,3 5 9 12 13 15 17 19 21 22 24 23,4 6 8 11 14 15 18 21 22 19 23 24:4 4 4,3 6 3>",
        4, 4)]
    [InlineData(
        "subdivided non-edge-to-edge",
        "<28:2 7 6 10 13 15 17 18 20 22 25 23 27 28,3 5 9 12 11 14 16 19 21 24 23 26 27 28,4 6 8 11 13 14 17 19 20 23 25 22 28 27:6 4 4,3 4 4 3>",
        4, 6)]
    public async Task MixedWorldSupportsImageFreeAuthoringSelectionAndReciprocalNavigation(
        string label, string symbol, int sidesA, int sidesB)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            using var host = TestWebHost.Create(database, "alice");
            using var client = host.CreateClient();
            using var created = await client.PostAsJsonAsync("/api/overworlds/periodic", new
            {
                name = "HTTP mixed cell test: " + label,
                tilingDsSymbol = symbol,
                worldUnitsPerAbstractPeriod = 2.0,
                rotationDegrees = 17.0,
                origin = new { x = 0.0, y = 0.0 }
            });
            Assert.True(created.IsSuccessStatusCode,
                $"{label}: world creation failed: {await created.Content.ReadAsStringAsync()}");
            var world = await created.Content.ReadFromJsonAsync<JsonElement>();
            var worldId = world.GetProperty("id").GetGuid();
            Assert.False(world.TryGetProperty("grid", out _));
            Assert.NotEqual(JsonValueKind.Null, world.GetProperty("tiling").ValueKind);
            Assert.Empty(world.GetProperty("sourceMaps").EnumerateArray());

            using var viewport = await client.GetAsync(
                $"/api/overworlds/{worldId:D}/cells/viewport?minX=-5&minY=-5&maxX=5&maxY=5");
            Assert.True(viewport.IsSuccessStatusCode,
                $"{label}: viewport failed: {await viewport.Content.ReadAsStringAsync()}");
            var cells = (await viewport.Content.ReadFromJsonAsync<JsonElement>())
                .EnumerateArray().ToArray();
            Assert.NotEmpty(cells);
            var sideCounts = cells.Select(cell => cell.GetProperty("polygon").GetArrayLength()).ToArray();
            Assert.Contains(sidesA, sideCounts);
            Assert.Contains(sidesB, sideCounts);
            Assert.True(cells.Select(cell => cell.GetProperty("id")
                .GetProperty("address").GetProperty("motifCellId").GetString())
                .Distinct().Count() >= 3, $"{label}: expected multiple motif identities.");

            var first = cells.First(cell => cell.GetProperty("polygon").GetArrayLength() == sidesA);
            var center = first.GetProperty("center");
            using var selected = await client.GetAsync(
                $"/api/overworlds/{worldId:D}/cells/containing?x={Number(center.GetProperty("x"))}&y={Number(center.GetProperty("y"))}");
            selected.EnsureSuccessStatusCode();
            var lookup = await selected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Unique", lookup.GetProperty("status").GetString());
            var selectedCell = lookup.GetProperty("candidates").EnumerateArray().Single();
            Assert.Equal(
                first.GetProperty("id").GetRawText(),
                selectedCell.GetRawText());

            using var pinnedResponse = await client.PostAsJsonAsync(
                $"/api/overworlds/{worldId:D}/cell-procedures", new { });
            pinnedResponse.EnsureSuccessStatusCode();
            var pinned = await pinnedResponse.Content.ReadFromJsonAsync<JsonElement>();

            async Task<JsonElement> StartAt(JsonElement cellId, string name)
            {
                using var started = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions", new
                    {
                        name,
                        procedureId = pinned.GetProperty("procedureId").GetGuid(),
                        procedureRevision = pinned.GetProperty("revision").GetInt32(),
                        presentationKey = "dm-controlled",
                        startCell = cellId.GetProperty("address")
                    });
                Assert.True(started.IsSuccessStatusCode,
                    $"{label}: cell start failed: {await started.Content.ReadAsStringAsync()}");
                return await started.Content.ReadFromJsonAsync<JsonElement>();
            }

            var firstSession = await StartAt(selectedCell, label + " first");
            var firstSessionId = firstSession.GetProperty("id").GetGuid();
            using var firstTopologyResponse = await client.GetAsync(
                $"/api/expeditions/{firstSessionId:D}/cell-topology");
            firstTopologyResponse.EnsureSuccessStatusCode();
            var firstTopology = await firstTopologyResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, firstTopology.GetProperty("formatVersion").GetInt32());
            Assert.Equal(sidesA, firstTopology.GetProperty("polygon").GetArrayLength());
            var interfaces = firstTopology.GetProperty("interfaces").EnumerateArray().ToArray();
            Assert.NotEmpty(interfaces);
            Assert.Equal(Enumerable.Range(0, interfaces.Length),
                interfaces.Select(edge => edge.GetProperty("interfaceIndex").GetInt32()));
            var exit = interfaces[0];
            var reciprocalIndex = exit.GetProperty("reciprocalInterfaceIndex").GetInt32();
            var neighborId = exit.GetProperty("neighborCell");

            var secondSession = await StartAt(neighborId, label + " neighbor");
            using var secondTopologyResponse = await client.GetAsync(
                $"/api/expeditions/{secondSession.GetProperty("id").GetGuid():D}/cell-topology");
            secondTopologyResponse.EnsureSuccessStatusCode();
            var secondTopology = await secondTopologyResponse.Content.ReadFromJsonAsync<JsonElement>();
            var reciprocal = secondTopology.GetProperty("interfaces")
                .EnumerateArray().Single(edge =>
                    edge.GetProperty("interfaceIndex").GetInt32() == reciprocalIndex);
            Assert.Equal(exit.GetProperty("interfaceIndex").GetInt32(),
                reciprocal.GetProperty("reciprocalInterfaceIndex").GetInt32());
            Assert.Equal(selectedCell.GetRawText(), reciprocal.GetProperty("neighborCell").GetRawText());
            Assert.Equal(exit.GetProperty("start").GetRawText(), reciprocal.GetProperty("end").GetRawText());
            Assert.Equal(exit.GetProperty("end").GetRawText(), reciprocal.GetProperty("start").GetRawText());

            // Owner isolation applies to traversal contexts, not only worlds.
            using var bobHost = TestWebHost.Create(database, "bob");
            using var bob = bobHost.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound,
                (await bob.GetAsync($"/api/expeditions/{firstSessionId:D}/cell-topology")).StatusCode);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }

    private static string Number(JsonElement value) =>
        value.GetDouble().ToString("R", CultureInfo.InvariantCulture);
}
