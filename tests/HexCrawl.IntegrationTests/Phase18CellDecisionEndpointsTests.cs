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
    public async Task MixedMotifHttpTravelUsesExactReciprocalAtomicInterfacesAndPersists()
    {
        const string mixedSymbol = "<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>";
        await WithWorldAsync(async (db, store, world, procedure, cell) =>
        {
            var tiling = world.Tiling!;
            var boundary = tiling.Boundaries(cell.Id.Address).First(x =>
                x.To.Address.MotifCellId != cell.Id.Address.MotifCellId);
            var midpoint = Midpoint(boundary);
            var heading = midpoint - cell.Center;
            var start = BaseState(cell) with
            {
                Traversal = BaseState(cell).Traversal with
                {
                    SelectedExitInterfaceIndex = boundary.InterfaceIndex
                }
            };
            await SeedAsync(store, world, procedure, start);
            using (var factory = TestWebHost.Create(db, "alice"))
            using (var client = factory.CreateClient())
            {
                using var advanced = await client.PostAsJsonAsync(
                    $"/api/expeditions/{start.Id:D}/cells/advance",
                    new AdvanceCellWatchRequest(1, Plan(heading), Inputs(1)), ApiJson);
                Assert.True(advanced.IsSuccessStatusCode,
                    $"Mixed-motif crossing rejected: {await advanced.Content.ReadAsStringAsync()}");
                var result = await advanced.Content.ReadFromJsonAsync<JsonElement>();
                var runtime = result.GetProperty("expedition");
                Assert.Equal(1, runtime.GetProperty("completedWatches").GetInt32());
                Assert.Equal(boundary.To, JsonSerializer.Deserialize<WorldCellId>(
                    runtime.GetProperty("currentCell").GetRawText(), ApiJson));
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("currentHex").ValueKind);
                Assert.Equal(JsonValueKind.Null, runtime.GetProperty("distanceTraveled").ValueKind);
                Assert.Empty(result.GetProperty("knownHexes").EnumerateArray());
                var exit = Assert.Single(result.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "CellExited");
                var entered = Assert.Single(result.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "CellEntered");
                Assert.Equal(boundary.InterfaceIndex,
                    exit.GetProperty("boundaryInterfaceIndex").GetInt32());
                Assert.Equal(boundary.ReciprocalInterfaceIndex,
                    exit.GetProperty("reciprocalInterfaceIndex").GetInt32());
                Assert.Equal(boundary.ReciprocalInterfaceIndex,
                    entered.GetProperty("boundaryInterfaceIndex").GetInt32());
                Assert.Equal(boundary.InterfaceIndex,
                    entered.GetProperty("reciprocalInterfaceIndex").GetInt32());
                Assert.Equal(cell.Id, JsonSerializer.Deserialize<WorldCellId>(
                    exit.GetProperty("cell").GetRawText(), ApiJson));
                Assert.Equal(boundary.To, JsonSerializer.Deserialize<WorldCellId>(
                    entered.GetProperty("cell").GetRawText(), ApiJson));
            }

            var reopened = new PostgresHexCrawlStore(db);
            await reopened.InitializeAsync();
            var saved = await reopened.GetExpeditionAsync(start.Id, "alice");
            var persisted = Assert.IsType<CellExpeditionState>(saved!.Runtime);
            Assert.Equal(boundary.To, persisted.Traversal.CurrentCell);
            Assert.Equal(cell.Id, persisted.Traversal.EnteredFrom);
            Assert.Equal(boundary.ReciprocalInterfaceIndex,
                persisted.Traversal.EntryInterfaceIndex);
            Assert.Null(persisted.DistanceTraveled);
            Assert.Null(await reopened.GetExpeditionAsync(start.Id, "bob"));
        }, mixedSymbol);
    }

    [Fact]
    public async Task GeneralizedStartAppliesSelectedPresentationPolicyToActualStartingAndEnteredCells()
    {
        await WithWorldAsync(async (database, store, world, procedure, cell) =>
        {
            await new CampaignProcedureService(store).CreateAsync("alice", procedure, null);
            var req = new StartCellExpeditionRequest(
                "Exploration knowledge at the actual cell", procedure.ProcedureId,
                procedure.Revision, cell.Id.Address, "exploration-map");
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();

            using (var rejected = await client.PostAsJsonAsync(
                $"/api/overworlds/{world.Id:D}/cell-expeditions",
                req with { PresentationKey = "nonexistent-presentation" }, ApiJson))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

            using var started = await client.PostAsJsonAsync(
                $"/api/overworlds/{world.Id:D}/cell-expeditions", req, ApiJson);
            started.EnsureSuccessStatusCode();
            var initial = await started.Content.ReadFromJsonAsync<JsonElement>();
            var id = initial.GetProperty("id").GetGuid();
            var known = Assert.Single(initial.GetProperty("knownCells").EnumerateArray());
            Assert.Equal(cell.Id, JsonSerializer.Deserialize<WorldCellId>(
                known.GetRawText(), ApiJson));
            Assert.Empty(initial.GetProperty("knownHexes").EnumerateArray());

            var boundary = world.Tiling!.Boundaries(cell.Id.Address)[0];
            var heading = Midpoint(boundary) - cell.Center;
            using var advanced = await client.PostAsJsonAsync(
                $"/api/expeditions/{id:D}/cells/advance",
                new AdvanceCellWatchRequest(1, Plan(heading), Inputs(1)), ApiJson);
            advanced.EnsureSuccessStatusCode();
            var after = await advanced.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, after.GetProperty("knownCells").GetArrayLength());
            Assert.Contains(after.GetProperty("knownCells").EnumerateArray(),
                entry => JsonSerializer.Deserialize<WorldCellId>(entry.GetRawText(), ApiJson)
                    == boundary.To);
            Assert.Empty(after.GetProperty("knownHexes").EnumerateArray());
            var persisted = await new PostgresHexCrawlStore(database)
                .GetExpeditionAsync(id, "alice");
            Assert.Equal(2, persisted!.Knowledge!.KnownCells!.Count);
            Assert.Equal("exploration-map", persisted.Knowledge.PresentationPolicy!.Key);
        });
    }

    [Fact]
    public async Task CellExpeditionResourceMutationPersistsWithoutChangingQualifiedPosition()
    {
        await WithWorldAsync(async (database, store, world, procedure, cell) =>
        {
            var initial = BaseState(cell);
            await SeedAsync(store, world, procedure, initial);
            var resourceId = Guid.NewGuid();
            var route = $"/api/expeditions/{initial.Id:D}/survival/resources/{resourceId:D}";
            var request = new
            {
                expectedVersion = 1,
                resourceKey = "food",
                target = new { scope = "Party", targetId = (Guid?)null },
                inventoryModel = "Counted",
                quantity = 5.0,
                unit = "ration",
                symbolicState = (string?)null,
                supplyDieSides = (int?)null,
                note = (string?)null,
                provenance = new
                {
                    sourceKind = "Dm",
                    sourceKey = "cell-survival-resource-test",
                    sourceReference = (string?)null,
                    providerName = (string?)null,
                    note = (string?)null
                }
            };
            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                using var updated = await client.PutAsJsonAsync(route, request);
                Assert.True(updated.IsSuccessStatusCode,
                    $"Resource update failed: {await updated.Content.ReadAsStringAsync()}");
                var result = await updated.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(2L, result.GetProperty("expeditionVersion").GetInt64());
                var resource = Assert.Single(
                    result.GetProperty("state").GetProperty("resources").EnumerateArray());
                Assert.Equal(resourceId, resource.GetProperty("id").GetGuid());
                Assert.Equal(5, resource.GetProperty("quantity").GetDouble());
                using var replay = await client.PutAsJsonAsync(route, request);
                Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
            }
            var reopened = new PostgresHexCrawlStore(database);
            await reopened.InitializeAsync();
            var stored = await reopened.GetExpeditionAsync(initial.Id, "alice");
            Assert.Equal(2, stored!.Version);
            Assert.Single(stored.Resources.Resources);
            var runtime = Assert.IsType<CellExpeditionState>(stored.Runtime);
            Assert.Equal(cell.Id, runtime.Traversal.CurrentCell);
            Assert.Null(runtime.DistanceTraveled);
            Assert.Null(runtime.ActiveWatch);
            Assert.Empty(runtime.History);
            Assert.Null(await reopened.GetExpeditionAsync(initial.Id, "bob"));
        });
    }

    [Theory]
    [InlineData("<1:1,1,1:4,4>")]
    [InlineData("<1:1,1,1:6,3>")]
    public async Task CanonicalVersionTwoProcedureCanBeAuthoredAndPinnedThroughHttpForCellStart(string dsSymbol)
    {
        await WithWorldAsync(async (database, _, world, procedure, cell) =>
        {
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            var canonicalJson = JsonSerializer.Serialize(procedure, ApiJson);
            using (var validated = await client.PostAsJsonAsync(
                "/api/procedures/composer/canonical/validate",
                new { canonicalJson }))
            {
                validated.EnsureSuccessStatusCode();
                var diagnostics = await validated.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(diagnostics.GetProperty("isValid").GetBoolean(),
                    diagnostics.GetProperty("error").ToString());
            }

            using var created = await client.PostAsJsonAsync(
                "/api/procedures/canonical",
                new
                {
                    canonicalJson,
                    presetKey = (string?)null,
                    campaignId = (Guid?)null
                });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var saved = await created.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(procedure.ProcedureId, saved.GetProperty("procedureId").GetGuid());
            Assert.Equal(1, saved.GetProperty("revision").GetInt32());

            using var started = await client.PostAsJsonAsync(
                $"/api/overworlds/{world.Id:D}/cell-expeditions",
                new StartCellExpeditionRequest(
                    "Procedure authored through HTTP", procedure.ProcedureId,
                    1, cell.Id.Address), ApiJson);
            Assert.True(started.IsSuccessStatusCode,
                $"Pinned v2 start failed: {await started.Content.ReadAsStringAsync()}");
            var runtime = await started.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2,
                runtime.GetProperty("procedure").GetProperty("runtime")
                    .GetProperty("movementMechanicVersion").GetInt32());
            Assert.Equal(cell.Id,
                JsonSerializer.Deserialize<WorldCellId>(
                    runtime.GetProperty("expedition").GetProperty("currentCell")
                        .GetRawText(), ApiJson));
            Assert.Null(await new PostgresHexCrawlStore(database)
                .GetExpeditionAsync(runtime.GetProperty("id").GetGuid(), "bob"));
        }, dsSymbol);
    }

    [Fact]
    public async Task SavedProcedureCellStartCreatesAuthoritativeCellSessionWithNoHexProjection()
    {
        await WithWorldAsync(async (database, store, world, procedure, cell) =>
        {
            await new CampaignProcedureService(store).CreateAsync("alice", procedure, null);
            var path = $"/api/overworlds/{world.Id:D}/cell-expeditions";
            var request = new StartCellExpeditionRequest(
                "New generalized expedition", procedure.ProcedureId,
                procedure.Revision, cell.Id.Address);
            using var factory = TestWebHost.Create(database, "alice");
            using var client = factory.CreateClient();
            using (var unpinned = await client.PostAsJsonAsync(path,
                request with { ProcedureRevision = 0 }, ApiJson))
                Assert.Equal(HttpStatusCode.BadRequest, unpinned.StatusCode);
            using (var otherFactory = TestWebHost.Create(database, "bob"))
            using (var other = otherFactory.CreateClient())
            using (var denied = await other.PostAsJsonAsync(path, request, ApiJson))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

            using var response = await client.PostAsJsonAsync(path, request, ApiJson);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("id").GetGuid();
            Assert.Equal(1, created.GetProperty("version").GetInt64());
            Assert.Equal(cell.Id.TilingId,
                created.GetProperty("expedition").GetProperty("currentCell")
                    .GetProperty("tilingId").GetGuid());
            Assert.Equal(JsonValueKind.Null,
                created.GetProperty("expedition").GetProperty("currentHex").ValueKind);
            Assert.Equal(0, created.GetProperty("knownHexes").GetArrayLength());
            Assert.Equal(procedure.ProcedureId,
                created.GetProperty("procedure").GetProperty("procedureId").GetGuid());

            var saved = await new PostgresHexCrawlStore(database)
                .GetExpeditionAsync(id, "alice");
            Assert.NotNull(saved);
            Assert.Equal(procedure.Revision, saved!.CampaignProcedure.Revision);
            Assert.Equal(cell.Id,
                Assert.IsType<CellExpeditionState>(saved.Runtime).Traversal.CurrentCell);
            Assert.Null(((CellExpeditionState)saved.Runtime).DistanceTraveled);
        });
    }

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
    public async Task TerminalLostEncounterSurvivesPostgresRestartAndCompletesOnlyAfterResolution()
    {
        await WithWorldAsync(async (database, store, world, procedure, cell) =>
        {
            var heading = new WorldPoint(1, 0);
            var plan = Plan(heading);
            var duration = GenericProcedureRuntime.Bind(procedure).Time.IntervalDuration;
            var scheduled = new ResolvedEncounter(
                EncounterOutcomeKind.WanderingEncounter, duration, null,
                "Encounter at terminal crossing", Manual);
            var state = BaseState(cell) with
            {
                IntendedHeading = heading,
                IsLost = true,
                ResolvedVeerDegrees = 30,
                ElapsedTravelTime = duration,
                ActiveWatch = new CellActiveWatchState(
                    1, duration, duration, plan, scheduled,
                    false, RuntimePauseReason.LostRecognitionRequired)
            };
            state.Validate(world.Tiling!);
            await SeedAsync(store, world, procedure, state);
            var advanceRoute = $"/api/expeditions/{state.Id:D}/cells/advance";

            // Open fresh server/database handles to establish durable restart
            // of the zero-remaining lost-recognition checkpoint.
            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                var before = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/expeditions/{state.Id:D}");
                Assert.Equal("LostRecognitionRequired",
                    before.GetProperty("expedition")
                        .GetProperty("activeWatchPendingDecision").GetString());

                using var recognized = await client.PostAsJsonAsync(advanceRoute,
                    new AdvanceCellWatchRequest(1, plan,
                        Inputs(0) with
                        {
                            BoundaryDecision = new BoundaryNavigationDecision(
                                true, true, Manual)
                        }), ApiJson);
                Assert.True(recognized.IsSuccessStatusCode,
                    $"Recognition failed: {await recognized.Content.ReadAsStringAsync()}");
                var paused = await recognized.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(2L, paused.GetProperty("version").GetInt64());
                Assert.Equal("EncounterTriggered",
                    paused.GetProperty("expedition")
                        .GetProperty("activeWatchPendingDecision").GetString());
                var occurrence = paused.GetProperty("expedition")
                    .GetProperty("pendingEncounter").GetProperty("id").GetGuid();
                Assert.Equal(duration.TotalHours, paused.GetProperty("expedition")
                    .GetProperty("elapsedTravelHours").GetDouble());
                Assert.Single(paused.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "EncounterTriggered"
                        && e.GetProperty("encounterOccurrenceId").GetGuid() == occurrence);
                Assert.DoesNotContain(paused.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "WatchCompleted");

                using var blocked = await client.PostAsJsonAsync(advanceRoute,
                    new AdvanceCellWatchRequest(2, plan, Inputs(0)), ApiJson);
                Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            }

            var restored = new PostgresHexCrawlStore(database);
            await restored.InitializeAsync();
            var saved = await restored.GetExpeditionAsync(state.Id, "alice");
            var pending = Assert.IsType<CellExpeditionState>(saved!.Runtime).PendingEncounter;
            Assert.NotNull(pending);
            Assert.Equal(duration, pending!.ExpeditionElapsedTime);
            var resolveRoute =
                $"/api/expeditions/{state.Id:D}/cells/encounters/{pending.Id:D}/resolve";
            using (var factory = TestWebHost.Create(database, "alice"))
            using (var client = factory.CreateClient())
            {
                using var resolved = await client.PostAsJsonAsync(resolveRoute,
                    new ResolveCellEncounterRequest(2, "Handled after server restart"), ApiJson);
                resolved.EnsureSuccessStatusCode();

                using var replay = await client.PostAsJsonAsync(resolveRoute,
                    new ResolveCellEncounterRequest(2, "Handled after server restart"), ApiJson);
                Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

                using var complete = await client.PostAsJsonAsync(advanceRoute,
                    new AdvanceCellWatchRequest(3, plan, Inputs(0)), ApiJson);
                complete.EnsureSuccessStatusCode();
                var finished = await complete.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(4L, finished.GetProperty("version").GetInt64());
                Assert.Equal(1, finished.GetProperty("expedition")
                    .GetProperty("completedWatches").GetInt32());
                Assert.Single(finished.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "EncounterTriggered");
                Assert.Single(finished.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "EncounterResolved");
                Assert.Single(finished.GetProperty("history").EnumerateArray(),
                    e => e.GetProperty("kind").GetString() == "WatchCompleted");
                Assert.Equal(duration.TotalHours,
                    finished.GetProperty("expedition")
                        .GetProperty("elapsedTravelHours").GetDouble());
            }
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

    [Fact]
    public async Task KeyedLocationDiscoveryUsesSavedCurrentCellLocationAndResolvesExactlyOnce()
    {
        await WithWorldAsync(async (database, store, world, procedure, cell) =>
        {
            var valid = Guid.NewGuid();
            var distant = Guid.NewGuid();
            var worldWithLocations = world with
            {
                Locations =
                [
                    new Location(valid, "Stone well", "landmark",
                        cell.Center, LocationDiscoverability.Obvious, []),
                    new Location(distant, "Faraway ruins", "landmark",
                        new WorldPoint(100000, 100000), LocationDiscoverability.Hidden, [])
                ]
            };
            var savedWorld = await store.GetOverworldAsync(world.Id, "alice");
            Assert.NotNull(savedWorld);
            Assert.Equal(SaveOutcome.Saved, (await store.SaveOverworldAsync(
                savedWorld! with { World = worldWithLocations }, 1)).Outcome);

            var state = BaseState(cell);
            await SeedAsync(store, worldWithLocations, procedure, state);
            var plan = Plan(new WorldPoint(1, 0));
            var route = $"/api/expeditions/{state.Id:D}/cells/advance";
            var encounterTime = TimeSpan.FromMinutes(15);
            CellWatchAdvanceInputs WithKeyed(Guid id) => Inputs(0) with
            {
                Encounter = new ResolvedEncounter(
                    EncounterOutcomeKind.KeyedLocationDiscovery, encounterTime, id,
                    "Found by the party", Manual)
            };

            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                // The selected preset schedules an encounter check each watch.
                // A location outside the actual cell is therefore rejected
                // when the encounter becomes due, before any state is saved.
                using var invalid = await client.PostAsJsonAsync(route,
                    new AdvanceCellWatchRequest(1, plan, WithKeyed(distant)), ApiJson);
                Assert.False(invalid.IsSuccessStatusCode);
                Assert.Equal(1, (await store.GetExpeditionAsync(state.Id, "alice"))!.Version);

                using var advanced = await client.PostAsJsonAsync(route,
                    new AdvanceCellWatchRequest(1, plan, WithKeyed(valid)), ApiJson);
                Assert.True(advanced.IsSuccessStatusCode,
                    await advanced.Content.ReadAsStringAsync());
                var started = await advanced.Content.ReadFromJsonAsync<JsonElement>();
                var pending = started.GetProperty("expedition").GetProperty("pendingEncounter");
                Assert.Equal(valid, pending.GetProperty("locationId").GetGuid());
                var occurrence = pending.GetProperty("id").GetGuid();
                Assert.Single(started.GetProperty("history").EnumerateArray(),
                    x => x.GetProperty("kind").GetString() == "KeyedLocationEncountered"
                        && x.GetProperty("subjectId").GetGuid() == valid);

                var resolvedRoute =
                    $"/api/expeditions/{state.Id:D}/cells/encounters/{occurrence:D}/resolve";
                using var completed = await client.PostAsJsonAsync(resolvedRoute,
                    new ResolveCellEncounterRequest(2, "The keyed site has been explored"), ApiJson);
                Assert.True(completed.IsSuccessStatusCode,
                    await completed.Content.ReadAsStringAsync());
                using var duplicate = await client.PostAsJsonAsync(resolvedRoute,
                    new ResolveCellEncounterRequest(2, "Duplicate"), ApiJson);
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            }

            var reopened = new PostgresHexCrawlStore(database);
            await reopened.InitializeAsync();
            var persisted = await reopened.GetExpeditionAsync(state.Id, "alice");
            Assert.NotNull(persisted);
            var cellState = Assert.IsType<CellExpeditionState>(persisted!.Runtime);
            Assert.Null(cellState.PendingEncounter);
            Assert.Single(cellState.History,
                evt => evt.Kind == CrawlRuntimeEventKind.LocationDiscovered
                    && evt.SubjectId == valid);
            Assert.Single(cellState.History,
                evt => evt.Kind == CrawlRuntimeEventKind.EncounterResolved);
        }, procedureKey: "alexandrian-advanced");
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
            CampaignProcedure, WorldCellGeometry, Task> action,
        string dsSymbol = "<1:1,1,1:4,4>",
        string procedureKey = "simple-fixed-distance")
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            var generated = DelaneyDressHarmonicMetricRealization.Construct(
                dsSymbol, 1.5, "world-unit");
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
                procedureKey).MaterializeGeneric().Procedure;
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
