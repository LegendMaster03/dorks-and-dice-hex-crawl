using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class Phase18CellExpeditionPersistenceTests
{
    [Fact]
    public async Task GeneralizedExpeditionRestartsMidCellWithQualifiedEventsAndNoAxialState()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();

        const string symbol = "<1:1,1,1:4,4>";
        var generated = DelaneyDressHarmonicMetricRealization.Construct(symbol, 1.5, "world-unit");
        Assert.Equal("realized", generated.Status);
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(), generated.Topology!,
            generated.Realization!, new WorldPoint(0, 0), PhysicalDistancePerWorldUnit:
            new DistanceMeasure(2, DistanceUnit.Miles));
        tiling.Validate();
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Generalized persisted world", Tiling = tiling
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));

        var procedure = CellProcedure(tiling);
        var startingCell = tiling.Resolve(new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(2, -1)));
        var edge = tiling.Boundaries(startingCell.Id.Address)[0];
        var middle = new WorldPoint(
            edge.Start.X + (edge.End.X - edge.Start.X) / 2,
            edge.Start.Y + (edge.End.Y - edge.Start.Y) / 2);
        var heading = middle - startingCell.Center;
        var initial = new PeriodicCellTraversal
        {
            CurrentCell = startingCell.Id,
            Position = startingCell.Center,
            TravelHeading = heading,
            SelectedExitInterfaceIndex = edge.InterfaceIndex
        };
        var quarter = tiling.MeasurePhysicalDistance(startingCell.Center,
            new WorldPoint(startingCell.Center.X + heading.X / 4,
                startingCell.Center.Y + heading.Y / 4));
        var remainingToCross = new DistanceMeasure(quarter.Value * 3.1, quarter.Unit);
        var first = new CrawlRuntimeEngine().ResolveCellTravel(
            tiling, procedure, initial,
            ResolvedTravelAmount.Distance(quarter, quarter, ResolutionProvenance.ProcedureDefault), true);
        Assert.Empty(first.Transitions);

        var runtimeEvent = new CrawlRuntimeEvent(
            1, 1, CrawlRuntimeEventKind.DistanceTraveled,
            TimeSpan.FromHours(1), null, "Partial generalized cell movement.",
            DistanceValue: quarter.Value, DistanceUnit: quarter.Unit.Symbol)
        { Cell = startingCell.Id };
        var active = new CellActiveWatchState(
            1, TimeSpan.FromHours(4), TimeSpan.FromHours(1),
            new CellWatchTravelPlan(heading,
                false, true, TravelModeSelection.Normal, NavigationAidSelection.None),
            ResolvedEncounter.None, true, null);
        var state = new CellExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = first.Traversal,
            IntendedHeading = heading,
            DistanceTraveled = first.ConsumedPhysicalDistance!.Value,
            ElapsedTravelTime = TimeSpan.FromHours(1),
            ActiveWatch = active,
            History = [runtimeEvent]
        };
        state.Validate(tiling);
        var expedition = new StoredExpedition(
            "Cell expedition", state, new WorldBoundCrawlSessionContext(world.Id),
            null, procedure, null, TimeSpan.FromHours(3),
            "owner", 1, now, now);
        await store.CreateExpeditionAsync(expedition);

        await using (var sql = new NpgsqlConnection(database.ConnectionString))
        {
            await sql.OpenAsync();
            await using var query = sql.CreateCommand();
            query.CommandText = "SELECT state_json::text FROM expeditions WHERE id = @id;";
            query.Parameters.AddWithValue("id", expedition.Id);
            var json = (string)(await query.ExecuteScalarAsync())!;
            Assert.Contains("\"cellTraversal\"", json);
            Assert.DoesNotContain("\"currentHex\":{\"q\":0", json, StringComparison.Ordinal);
        }

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();
        Assert.Null(await restarted.GetExpeditionAsync(expedition.Id, "different-owner"));
        var loaded = await restarted.GetExpeditionAsync(expedition.Id, "owner");
        Assert.NotNull(loaded);
        var loadedState = Assert.IsType<CellExpeditionState>(loaded!.Runtime);
        loadedState.Validate(tiling);
        Assert.Equal(startingCell.Id, loadedState.Traversal.CurrentCell);
        Assert.Equal(first.Traversal.Position, loadedState.Traversal.Position);
        Assert.Equal(edge.InterfaceIndex, loadedState.Traversal.SelectedExitInterfaceIndex);
        Assert.Equal(active.TotalDuration, loadedState.ActiveWatch!.TotalDuration);
        Assert.Equal(active.Elapsed, loadedState.ActiveWatch.Elapsed);
        Assert.Equal(quarter, loadedState.DistanceTraveled);
        Assert.Equal(runtimeEvent.Cell, Assert.Single(loadedState.History).Cell);

        var second = new CrawlRuntimeEngine().ResolveCellTravel(
            tiling, loaded.CampaignProcedure, loadedState.Traversal,
            ResolvedTravelAmount.Distance(remainingToCross, remainingToCross,
                ResolutionProvenance.ProcedureDefault), true);
        var crossing = Assert.Single(second.Transitions);
        Assert.Equal(edge.To, crossing.To);
        var nextSequence = loadedState.History[^1].Sequence + 1;
        var entered = new CrawlRuntimeEvent(
            nextSequence, 1, CrawlRuntimeEventKind.CellEntered,
            TimeSpan.FromHours(3), null, "Entered cell through reciprocal interface.")
        {
            Cell = second.Traversal.CurrentCell,
            BoundaryInterfaceIndex = crossing.ExitInterfaceIndex,
            ReciprocalInterfaceIndex = crossing.EntryInterfaceIndex
        };
        var updated = loaded with
        {
            Runtime = loadedState with
            {
                Traversal = second.Traversal,
                DistanceTraveled = new DistanceMeasure(
                    loadedState.DistanceTraveled!.Value.Value + second.ConsumedPhysicalDistance!.Value.Value,
                    DistanceUnit.Miles),
                ElapsedTravelTime = TimeSpan.FromHours(3),
                History = [.. loadedState.History, entered]
            }
        };
        var saved = await restarted.SaveExpeditionAsync(updated, loaded.Version);
        Assert.Equal(SaveOutcome.Saved, saved.Outcome);
        Assert.Equal(SaveOutcome.Conflict, (await restarted.SaveExpeditionAsync(loaded, loaded.Version)).Outcome);
        var verified = await restarted.GetExpeditionAsync(expedition.Id, "owner");
        var verifiedState = Assert.IsType<CellExpeditionState>(verified!.Runtime);
        Assert.Equal(crossing.To, verifiedState.Traversal.CurrentCell);
        Assert.Equal(crossing.EntryInterfaceIndex, verifiedState.Traversal.EntryInterfaceIndex);
        Assert.Equal(2, verifiedState.History.Count);
        Assert.Equal(crossing.To, verifiedState.History[1].Cell);
        Assert.Null(verifiedState.History[1].Hex);
    }

    [Fact]
    public async Task UncalibratedPendingEncounterAndWatchRestartWithoutFakeHexOrDistance()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var generated = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1, "world-unit");
        Assert.Equal("realized", generated.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), generated.Topology!, generated.Realization!, new WorldPoint(0, 0));
        tiling.Validate();
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Uncalibrated pending watch", Tiling = tiling
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));
        var cell = tiling.Resolve(new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(-2, 3)));
        var heading = new WorldPoint(1, 0);
        var cursor = new PeriodicCellTraversal
        {
            CurrentCell = cell.Id, Position = cell.Center, TravelHeading = heading
        };
        var occurrenceId = Guid.NewGuid();
        var provenance = new ResolutionProvenance(ResolutionSource.DmOverride, "Explicit test encounter");
        var eventRecord = new CrawlRuntimeEvent(
            1, 1, CrawlRuntimeEventKind.EncounterTriggered,
            TimeSpan.FromHours(1), null, "A pending custom encounter.",
            EncounterOutcome: EncounterOutcomeKind.ManualCustom,
            EncounterOccurrenceId: occurrenceId)
        { Cell = cell.Id };
        var pending = new PendingEncounterOccurrence(
            occurrenceId, 1, 1, EncounterOutcomeKind.ManualCustom,
            TimeSpan.FromHours(1), null, null, "Encounter pending", provenance)
        { Cell = cell.Id };
        var active = new CellActiveWatchState(
            1, TimeSpan.FromHours(4), TimeSpan.FromHours(1),
            new CellWatchTravelPlan(heading, false, false,
                TravelModeSelection.Normal, NavigationAidSelection.None),
            new ResolvedEncounter(EncounterOutcomeKind.ManualCustom,
                TimeSpan.FromHours(1), null, "Encounter pending", provenance),
            true, RuntimePauseReason.EncounterTriggered);
        var state = new CellExpeditionState
        {
            Id = Guid.NewGuid(), Traversal = cursor, IntendedHeading = heading,
            DistanceTraveled = null, ElapsedTravelTime = TimeSpan.FromHours(1),
            ActiveWatch = active, PendingEncounter = pending, History = [eventRecord]
        };
        state.Validate(tiling);
        var expedition = new StoredExpedition(
            "Paused generalized", state, new WorldBoundCrawlSessionContext(world.Id),
            null, CellProcedure(tiling), RuntimePauseReason.EncounterTriggered,
            TimeSpan.FromHours(3), "owner", 1, now, now);
        await store.CreateExpeditionAsync(expedition);

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();
        var loaded = await restarted.GetExpeditionAsync(expedition.Id, "owner");
        var spatial = Assert.IsType<CellExpeditionState>(loaded!.Runtime);
        spatial.Validate(tiling);
        Assert.Null(spatial.DistanceTraveled);
        Assert.Null(spatial.PendingEncounter!.Hex);
        Assert.Equal(cell.Id, spatial.PendingEncounter.Cell);
        Assert.Equal(occurrenceId, spatial.PendingEncounter.Id);
        Assert.Equal(RuntimePauseReason.EncounterTriggered, spatial.ActiveWatch!.PendingDecision);
        Assert.Equal(TimeSpan.FromHours(3), spatial.ActiveWatch.Remaining);
        Assert.Equal(cell.Id, Assert.Single(spatial.History).Cell);

        var saved = await restarted.SaveExpeditionAsync(loaded, loaded.Version);
        Assert.Equal(SaveOutcome.Saved, saved.Outcome);
        await using var db = new NpgsqlConnection(database.ConnectionString);
        await db.OpenAsync();
        await using var count = db.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM expedition_events WHERE expedition_id = @id;";
        count.Parameters.AddWithValue("id", expedition.Id);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task OwnerCanCommitCellWatchThroughApplicationWithVersionAndRestartProtection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var generated = DelaneyDressHarmonicMetricRealization.Construct(
            "<1:1,1,1:4,4>", 1.5, "world-unit");
        Assert.Equal("realized", generated.Status);
        var tiling = new PeriodicWorldTiling(
            Guid.NewGuid(), generated.Topology!, generated.Realization!,
            new WorldPoint(0, 0));
        tiling.Validate();
        var now = DateTimeOffset.UtcNow;
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Cell watch application world", Tiling = tiling
        };
        await store.CreateOverworldAsync(new StoredOverworld(world, "alice", 1, now, now));

        var startingCell = tiling.Resolve(new PeriodicCellAddress(
            tiling.Topology.MotifCells[0].Id, new LatticeDisplacement(0, 0)));
        var exit = tiling.Boundaries(startingCell.Id.Address)[0];
        var midpoint = new WorldPoint(
            (exit.Start.X + exit.End.X) / 2, (exit.Start.Y + exit.End.Y) / 2);
        var heading = midpoint - startingCell.Center;
        var state = new CellExpeditionState
        {
            Id = Guid.NewGuid(),
            Traversal = new PeriodicCellTraversal
            {
                CurrentCell = startingCell.Id,
                Position = startingCell.Center,
                SelectedExitInterfaceIndex = exit.InterfaceIndex
            }
        };
        var initial = new StoredExpedition(
            "Cell steps through app", state,
            new WorldBoundCrawlSessionContext(world.Id),
            null, CellProcedure(tiling, TravelResolutionMode.CellSteps),
            null, TimeSpan.Zero, "alice", 1, now, now);
        await store.CreateExpeditionAsync(initial);

        var plan = new CellWatchTravelPlan(
            heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None);
        var provenance = new ResolutionProvenance(ResolutionSource.ManualRoll);
        var inputs = new CellWatchAdvanceInputs(
            ResolvedTravelAmount.CellTransitions(1, provenance),
            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, provenance),
            ResolvedEncounter.None);
        var service = new HexCrawlService(store);

        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            service.AdvanceCellExpeditionAsync(initial.Id, "bob", 1, plan, inputs));
        var forgedAssignment = new ParticipantActivityAssignment(
            Guid.NewGuid(), ParticipantActivityAssignmentScope.Party,
            null, "scouting", null);
        var forgedPlan = plan with
        {
            Mode = new TravelModeSelection("normal", [forgedAssignment])
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdvanceCellExpeditionAsync(initial.Id, "alice", 1, forgedPlan, inputs));
        Assert.Equal(1, (await store.GetExpeditionAsync(initial.Id, "alice"))!.Version);

        var saved = await service.AdvanceCellExpeditionAsync(initial.Id, "alice", 1, plan, inputs);
        var moved = Assert.IsType<CellExpeditionState>(saved.Runtime);
        Assert.Equal(exit.To, moved.Traversal.CurrentCell);
        Assert.Null(moved.DistanceTraveled);
        Assert.Equal(1, moved.CompletedWatches);
        Assert.Equal(1, moved.History.Count(e => e.Kind == CrawlRuntimeEventKind.CellEntered));
        Assert.DoesNotContain(moved.History, e => e.Hex is not null);
        Assert.True(saved.Version > initial.Version);

        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() =>
            service.AdvanceCellExpeditionAsync(initial.Id, "alice", 1, plan, inputs));

        var reopened = new PostgresHexCrawlStore(database.ConnectionString);
        await reopened.InitializeAsync();
        var loaded = await reopened.GetExpeditionAsync(initial.Id, "alice");
        Assert.NotNull(loaded);
        var restored = Assert.IsType<CellExpeditionState>(loaded!.Runtime);
        restored.Validate(tiling);
        Assert.Equal(moved.Traversal.CurrentCell, restored.Traversal.CurrentCell);
        Assert.Equal(moved.ElapsedTravelTime, restored.ElapsedTravelTime);
        Assert.Equal(saved.Version, loaded.Version);
        Assert.Single(restored.History, e => e.Kind == CrawlRuntimeEventKind.CellEntered);
    }

    private static CampaignProcedure CellProcedure(PeriodicWorldTiling world, TravelResolutionMode mode = TravelResolutionMode.ContinuousDistance)
    {
        var original = SyntheticProcedureFixtures.MixedProcedure();
        var modules = original.Modules
            .Where(m => m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.HexProgressPolicy)
            .Select(m =>
            {
                if (m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return m;
                var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                parameters["travelResolution"] = mode.ToString();
                parameters["tracksIntraHexProgress"] = "false";
                parameters["actualDistanceResolution"] = ActualDistanceResolutionMode.Fixed.ToString();
                return m with { Mechanic = m.Mechanic with { Version = 2 }, Parameters = parameters };
            }).ToArray();
        var procedure = original with
        {
            TilingDsSymbol = world.Topology.QuotientDsSymbol,
            Modules = modules
        };
        procedure.Validate();
        return procedure;
    }
}
