using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Presentation;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using HexCrawl.Web.Api;

namespace HexCrawl.IntegrationTests;

public sealed class Phase18CellDecisionEndpointsTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    private static readonly ResolutionProvenance Manual = new(ResolutionSource.ManualRoll);

    [Fact]
    public async Task VertexPauseRequiresAuthorizedValidatedCourseAndThenResumesSameWatch()
    {
        await WithWorldAsync(async (database, store, world, procedure, cell) =>
        {
            var polygon = world.Tiling!.Resolve(cell.Id.Address).Polygon;
            var ambiguousHeading = polygon[0] - cell.Center;
            var initial = BaseState(cell) with
            {
                Traversal = BaseState(cell).Traversal with
                {
                    SelectedExitInterfaceIndex = null
                }
            };
            var ambiguousPlan = Plan(ambiguousHeading);
            var paused = new CrawlRuntimeEngine().AdvanceCellWatch(
                world.Tiling, procedure, initial, ambiguousPlan, Inputs(1));
            Assert.Equal(RuntimePauseReason.CellCourseAdjudicationRequired, paused.PauseReason);
            await SeedAsync(store, world, procedure, paused.Expedition);

            var boundary = world.Tiling.Boundaries(cell.Id.Address)[0];
            var midpoint = new WorldPoint(
                (boundary.Start.X + boundary.End.X) / 2,
                (boundary.Start.Y + boundary.End.Y) / 2);
            var heading = midpoint - cell.Center;
            var choice = new ResolveCellCourseRequest(1,
                new CellCourseDecision(heading, boundary.InterfaceIndex, Manual));

            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var route = $"/api/expeditions/{initial.Id:D}/cells/course";
            var advanceRoute = $"/api/expeditions/{initial.Id:D}/cells/advance";
            using (var blocked = await client.PostAsJsonAsync(advanceRoute,
                new AdvanceCellWatchRequest(1, ambiguousPlan, Inputs(1)), ApiJson))
                Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

            using (var rejected = await client.PostAsJsonAsync(route,
                choice with { Decision = choice.Decision with
                {
                    SelectedExitInterfaceIndex = -1
                } }, ApiJson))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            using (var denied = await other.PostAsJsonAsync(route, choice, ApiJson))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

            using var result = await client.PostAsJsonAsync(route, choice, ApiJson);
            result.EnsureSuccessStatusCode();
            var updated = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, updated.GetProperty("version").GetInt64());
            Assert.Equal(JsonValueKind.Null,
                updated.GetProperty("expedition").GetProperty("activeWatchPendingDecision").ValueKind);
            Assert.Single(updated.GetProperty("history").EnumerateArray(),
                e => e.GetProperty("kind").GetString() == "DirectionChanged");

            using (var stale = await client.PostAsJsonAsync(route, choice, ApiJson))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            using var resumed = await client.PostAsJsonAsync(advanceRoute,
                new AdvanceCellWatchRequest(2, Plan(heading), Inputs(1)), ApiJson);
            Assert.True(resumed.IsSuccessStatusCode,
                $"Resume returned {resumed.StatusCode}: {await resumed.Content.ReadAsStringAsync()}");
            var body = await resumed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
            Assert.Equal(0, body.GetProperty("knownHexes").GetArrayLength());
            Assert.Single(body.GetProperty("history").EnumerateArray(),
                e => e.GetProperty("kind").GetString() == "CellEntered");
            Assert.Equal(JsonValueKind.Null,
                body.GetProperty("expedition").GetProperty("currentHex").ValueKind);
            var persisted = await new PostgresHexCrawlStore(database)
                .GetExpeditionAsync(initial.Id, "alice");
            Assert.Equal(3, persisted!.Version);
            Assert.Equal(boundary.To,
                ((CellExpeditionState)persisted.Runtime).Traversal.CurrentCell);
        });
    }

    [Fact]
    public async Task EncounterOccurrenceResolvesOnceAndResumesRemainingWatchTime()
    {
        await WithWorldAsync(async (database, store, world, procedure, cell) =>
        {
            var boundary = world.Tiling!.Boundaries(cell.Id.Address)[0];
            var heading = Midpoint(boundary) - cell.Center;
            var plan = Plan(heading);
            var duration = GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration;
            var half = TimeSpan.FromTicks(duration.Ticks / 2);
            var id = Guid.NewGuid();
            var encounter = new ResolvedEncounter(
                EncounterOutcomeKind.WanderingEncounter,
                half, null, "Trail ambush", Manual);
            var triggered = new CrawlRuntimeEvent(
                1, 1, CrawlRuntimeEventKind.EncounterTriggered, half,
                null, "Trail ambush",
                EncounterOutcome: encounter.Kind,
                EncounterOccurrenceId: id)
            { Cell = cell.Id };
            var state = BaseState(cell) with
            {
                ElapsedTravelTime = half,
                IntendedHeading = heading,
                ActiveWatch = new CellActiveWatchState(
                    1, duration, half, plan, encounter, true,
                    RuntimePauseReason.EncounterTriggered),
                PendingEncounter = new PendingEncounterOccurrence(
                    id, 1, 1, encounter.Kind, half, null,
                    null, encounter.Note, Manual) { Cell = cell.Id },
                History = [triggered]
            };
            state.Validate(world.Tiling);
            await SeedAsync(store, world, procedure, state);

            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var resolveRoute = $"/api/expeditions/{state.Id:D}/cells/encounters/{id:D}/resolve";
            var advanceRoute = $"/api/expeditions/{state.Id:D}/cells/advance";
            var req = new ResolveCellEncounterRequest(1, "Handled by DM");
            using (var cannotAdvance = await client.PostAsJsonAsync(advanceRoute,
                new AdvanceCellWatchRequest(1, plan, Inputs(0)), ApiJson))
                Assert.Equal(HttpStatusCode.BadRequest, cannotAdvance.StatusCode);
            using (var wrongId = await client.PostAsJsonAsync(
                $"/api/expeditions/{state.Id:D}/cells/encounters/{Guid.NewGuid():D}/resolve", req, ApiJson))
                Assert.Equal(HttpStatusCode.BadRequest, wrongId.StatusCode);
            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            using (var blocked = await other.PostAsJsonAsync(resolveRoute, req, ApiJson))
                Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);

            using var resolved = await client.PostAsJsonAsync(resolveRoute, req, ApiJson);
            resolved.EnsureSuccessStatusCode();
            var body = await resolved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, body.GetProperty("version").GetInt64());
            Assert.Equal(JsonValueKind.Null,
                body.GetProperty("expedition").GetProperty("pendingEncounter").ValueKind);
            Assert.Single(body.GetProperty("history").EnumerateArray(),
                e => e.GetProperty("kind").GetString() == "EncounterResolved"
                     && e.GetProperty("encounterOccurrenceId").GetGuid() == id);
            using (var stale = await client.PostAsJsonAsync(resolveRoute, req, ApiJson))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            using var continued = await client.PostAsJsonAsync(advanceRoute,
                new AdvanceCellWatchRequest(2, plan, Inputs(0)), ApiJson);
            continued.EnsureSuccessStatusCode();
            var completed = await continued.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, completed.GetProperty("expedition").GetProperty("completedWatches").GetInt32());
            Assert.Equal(duration.TotalHours,
                completed.GetProperty("expedition").GetProperty("elapsedTravelHours").GetDouble(), 8);
            Assert.Single(completed.GetProperty("history").EnumerateArray(),
                e => e.GetProperty("kind").GetString() == "EncounterTriggered");
            Assert.Single(completed.GetProperty("history").EnumerateArray(),
                e => e.GetProperty("kind").GetString() == "EncounterResolved");
            var stored = await new PostgresHexCrawlStore(database)
                .GetExpeditionAsync(state.Id, "alice");
            Assert.Equal(3, stored!.Version);
            Assert.Null(((CellExpeditionState)stored.Runtime).PendingEncounter);
        });
    }

    private static CellWatchAdvanceInputs Inputs(int steps) => new(
        ResolvedTravelAmount.CellTransitions(steps, Manual),
        new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, Manual),
        ResolvedEncounter.None);

    private static CellWatchTravelPlan Plan(WorldPoint heading) => new(
        heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None);

    private static WorldPoint Midpoint(WorldCellBoundary boundary) => new(
        (boundary.Start.X + boundary.End.X) / 2,
        (boundary.Start.Y + boundary.End.Y) / 2);

    private static CellExpeditionState BaseState(WorldCellGeometry cell) => new()
    {
        Id = Guid.NewGuid(),
        Traversal = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id, Position = cell.Center
        }
    };

    private static async Task SeedAsync(
        PostgresHexCrawlStore store, OverworldDefinition world,
        CampaignProcedure procedure, CellExpeditionState state)
    {
        var now = DateTimeOffset.UtcNow;
        await store.CreateExpeditionAsync(new StoredExpedition(
            "HTTP cell decisions", state,
            new WorldBoundCrawlSessionContext(world.Id),
            new PlayerKnowledgeState
            {
                ScopeId = Guid.NewGuid(),
                OverworldId = world.Id,
                PresentationPolicy = MapPresentationPolicy.DmControlled()
            },
            procedure, state.ActiveWatch?.PendingDecision,
            state.ActiveWatch?.Remaining ?? TimeSpan.Zero,
            "alice", 1, now, now));
    }

    private static async Task WithWorldAsync(
        Func<string, PostgresHexCrawlStore, OverworldDefinition,
            CampaignProcedure, WorldCellGeometry, Task> action)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var generated = DelaneyDressHarmonicMetricRealization.Construct(
                "<1:1,1,1:4,4>", 1.5, "world-unit");
            Assert.Equal("realized", generated.Status);
            var tiling = new PeriodicWorldTiling(
                Guid.NewGuid(), generated.Topology!, generated.Realization!,
                new WorldPoint(0, 0));
            tiling.Validate();
            var world = new OverworldDefinition
            {
                Id = Guid.NewGuid(), Name = "Mutation contract world", Tiling = tiling
            };
            var store = new PostgresHexCrawlStore(database);
            var now = DateTimeOffset.UtcNow;
            await store.CreateOverworldAsync(new StoredOverworld(
                world, "alice", 1, now, now));
            var original = CrawlProcedureCatalog.Resolve(
                "simple-fixed-distance").MaterializeGeneric().Procedure;
            var modules = original.Modules
                .Where(m => m.Mechanic.ExecutionHandler !=
                    GenericProcedureExecutionHandlers.HexProgressPolicy)
                .Select(m =>
                {
                    if (m.Mechanic.ExecutionHandler !=
                        GenericProcedureExecutionHandlers.MovementResolutionPolicy) return m;
                    var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                    parameters["travelResolution"] = TravelResolutionMode.CellSteps.ToString();
                    parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                    parameters["tracksIntraHexProgress"] = "false";
                    return m with
                    {
                        Mechanic = m.Mechanic with { Version = 2 },
                        Parameters = parameters
                    };
                }).ToArray();
            var procedure = original with
            {
                ProcedureId = Guid.NewGuid(),
                TilingDsSymbol = tiling.Topology.QuotientDsSymbol,
                Modules = modules
            };
            procedure.Validate();
            var cell = tiling.Resolve(new PeriodicCellAddress(
                tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
            await action(database, store, world, procedure, cell);
        }
        finally
        {
            TestWebHost.DeleteDatabase(database);
        }
    }
}
