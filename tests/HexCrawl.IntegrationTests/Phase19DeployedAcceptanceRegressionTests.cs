using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

/// <summary>
/// Reproduces the actual public path: image-free world, generated immutable
/// cell-step procedure, fresh cell expedition, normal/fixed watch movement,
/// exact destination cell, and database reload. No bespoke procedure JSON,
/// arbitrary pace keys, or world calibration are required.
/// </summary>
public sealed class Phase19DeployedAcceptanceRegressionTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Theory]
    [InlineData("<1:1,1,1:4,4>", false)]
    [InlineData("<1:1,1,1:3,6>", true)]
    public async Task GeneratedCellTravelRulesetAdvancesAndReloadsWithoutConfiguredPaceChoices(
        string symbol, bool calibrated)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid expeditionId;
            WorldCellId neighbor;
            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var created = await client.PostAsJsonAsync(
                    "/api/overworlds/periodic", new
                    {
                        name = calibrated ? "Calibrated triangle acceptance" : "Uncalibrated square acceptance",
                        tilingDsSymbol = symbol,
                        worldUnitsPerAbstractPeriod = 2.0,
                        origin = new { x = 0.0, y = 0.0 },
                        physicalDistancePerWorldUnit = calibrated
                            ? new DistanceMeasure(1, DistanceUnit.Miles) : (DistanceMeasure?)null
                    }, ApiJson);
                Assert.True(created.IsSuccessStatusCode,
                    await created.Content.ReadAsStringAsync());
                var world = await created.Content.ReadFromJsonAsync<JsonElement>();
                var worldId = world.GetProperty("id").GetGuid();

                using var viewport = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/cells/viewport?minX=-3&minY=-3&maxX=3&maxY=3");
                viewport.EnsureSuccessStatusCode();
                var cell = (await viewport.Content.ReadFromJsonAsync<JsonElement>())
                    .EnumerateArray().First();

                using var generated = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-procedures", new { }, ApiJson);
                Assert.True(generated.IsSuccessStatusCode,
                    await generated.Content.ReadAsStringAsync());
                var savedProcedure = await generated.Content.ReadFromJsonAsync<JsonElement>();

                using var started = await client.PostAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/cell-expeditions", new
                    {
                        name = "Generated compatible ruleset can advance",
                        procedureId = savedProcedure.GetProperty("procedureId").GetGuid(),
                        procedureRevision = savedProcedure.GetProperty("revision").GetInt32(),
                        presentationKey = "dm-controlled",
                        startCell = cell.GetProperty("id").GetProperty("address")
                    }, ApiJson);
                Assert.True(started.IsSuccessStatusCode,
                    await started.Content.ReadAsStringAsync());
                var state = await started.Content.ReadFromJsonAsync<JsonElement>();
                expeditionId = state.GetProperty("id").GetGuid();
                Assert.Equal("CellSteps", state.GetProperty("procedure").GetProperty("runtime")
                    .GetProperty("travelResolution").GetString());
                Assert.Empty(state.GetProperty("movementComposition")
                    .GetProperty("policy").GetProperty("travelModeKeys").EnumerateArray());
                Assert.Equal(JsonValueKind.Null,
                    state.GetProperty("expedition").GetProperty("distanceTraveled").ValueKind);
                Assert.Equal(JsonValueKind.Null,
                    state.GetProperty("party").GetProperty("baseMovement").ValueKind);

                using var topologyResponse = await client.GetAsync(
                    $"/api/expeditions/{expeditionId:D}/cell-topology");
                topologyResponse.EnsureSuccessStatusCode();
                var topology = await topologyResponse.Content.ReadFromJsonAsync<JsonElement>();
                var edge = topology.GetProperty("interfaces").EnumerateArray().First();
                neighbor = JsonSerializer.Deserialize<WorldCellId>(
                    edge.GetProperty("neighborCell").GetRawText(), ApiJson);
                var position = topology.GetProperty("position");
                var begin = edge.GetProperty("start");
                var end = edge.GetProperty("end");
                var heading = new WorldPoint(
                    (begin.GetProperty("x").GetDouble() + end.GetProperty("x").GetDouble()) / 2
                        - position.GetProperty("x").GetDouble(),
                    (begin.GetProperty("y").GetDouble() + end.GetProperty("y").GetDouble()) / 2
                        - position.GetProperty("y").GetDouble());
                Assert.True(heading.X != 0 || heading.Y != 0);
                var travel = new CellWatchAdvanceInputs(
                    ResolvedTravelAmount.CellTransitions(1,
                        new ResolutionProvenance(ResolutionSource.DmOverride, "DM-confirmed one cell step")));
                var plan = new CellWatchTravelPlan(
                    heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None);
                using var advanced = await client.PostAsJsonAsync(
                    $"/api/expeditions/{expeditionId:D}/cells/advance",
                    new AdvanceCellWatchRequest(state.GetProperty("version").GetInt64(), plan, travel),
                    ApiJson);
                Assert.True(advanced.IsSuccessStatusCode,
                    await advanced.Content.ReadAsStringAsync());
                var result = await advanced.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(2, result.GetProperty("version").GetInt64());
                Assert.Equal(1, result.GetProperty("expedition")
                    .GetProperty("completedWatches").GetInt32());
                Assert.Equal(neighbor, JsonSerializer.Deserialize<WorldCellId>(
                    result.GetProperty("expedition").GetProperty("currentCell").GetRawText(), ApiJson));
                Assert.Equal(JsonValueKind.Null,
                    result.GetProperty("expedition").GetProperty("distanceTraveled").ValueKind);
            }

            var reopened = new PostgresHexCrawlStore(database);
            await reopened.InitializeAsync();
            var persisted = await reopened.GetExpeditionAsync(expeditionId, "alice");
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted!.Version);
            var cellRuntime = Assert.IsType<CellExpeditionState>(persisted.Runtime);
            Assert.Equal(neighbor, cellRuntime.Traversal.CurrentCell);
            Assert.Equal(1, cellRuntime.CompletedWatches);
            Assert.Null(cellRuntime.DistanceTraveled);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
