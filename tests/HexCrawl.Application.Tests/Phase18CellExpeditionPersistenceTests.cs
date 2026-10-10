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

    private static CampaignProcedure CellProcedure(PeriodicWorldTiling world)
    {
        var original = SyntheticProcedureFixtures.MixedProcedure();
        var modules = original.Modules
            .Where(m => m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.HexProgressPolicy)
            .Select(m =>
            {
                if (m.Mechanic.ExecutionHandler != GenericProcedureExecutionHandlers.MovementResolutionPolicy)
                    return m;
                var parameters = m.Parameters.ToDictionary(x => x.Key, x => x.Value);
                parameters["travelResolution"] = TravelResolutionMode.ContinuousDistance.ToString();
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
