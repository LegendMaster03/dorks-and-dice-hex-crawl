using HexCrawl.Application;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

/// <summary>
/// Verifies the explicit mapless V2 creation and application path through the
/// production PostgreSQL snapshot layer, not a plain domain JSON serializer.
/// </summary>
public sealed class Phase20MaplessCellApplicationTests
{
    private static AbstractHexCrawlSessionContext Context() => new(
        "Mapless V2", HexOrientation.PointyTop,
        new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles)));

    [Theory]
    [InlineData("alexandrian-advanced")]
    [InlineData("simple-fixed-distance")]
    [InlineData("simple-hex-step")]
    public async Task NewMaplessSpatialStartReconstructsExactlyAfterPostgresRestart(string key)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var startService = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var startHex = new HexCoordinate(2, -1);
        var created = await startService.StartStandaloneCellsAsync(
            "owner", "No imported map", new ProcedureStartSelection(PresetKey: key),
            Context(), startHex);

        Assert.IsType<AbstractHexCrawlSessionContext>(created.Context);
        Assert.IsType<CellExpeditionState>(created.Runtime);
        Assert.Null(created.Knowledge);
        Assert.Equal(key, created.ProcedureOrigin?.PresetKey);
        Assert.Null(created.ProcedureOrigin?.PresetRevision);
        Assert.Equal(2, GenericProcedureRuntime.Bind(created.CampaignProcedure).Movement.MechanicVersion);

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();
        Assert.Null(await restarted.GetExpeditionAsync(created.Id, "other-owner"));
        var saved = (await restarted.GetExpeditionAsync(created.Id, "owner"))!;
        var service = new HexCrawlService(restarted);
        var geometry = await service.ResolveCellGeometryAsync(saved, "owner");
        Assert.Null(geometry.World);
        var cell = Assert.IsType<CellExpeditionState>(saved.Runtime);
        cell.Validate(geometry.Tiling);
        var expected = AbstractCellSessionGeometry.CreateNewHexSession(
            created.Id, Context(), startHex);
        Assert.Equal(expected.Expedition.Traversal, cell.Traversal);
        Assert.Equal(expected.World.Id, geometry.Tiling.Id);
        Assert.Equal(expected.World.Resolve(cell.Traversal.CurrentCell.Address).Center,
            cell.Traversal.Position);
        Assert.Equal(created.CampaignProcedure.ProcedureId, saved.CampaignProcedure.ProcedureId);
    }

    [Fact]
    public async Task MaplessV2WatchUsesSameCellRuntimeAndOwnerVersionProtection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var starter = new ExpeditionStartService(store, new HexCrawlService(store),
            new CampaignProcedureService(store));
        var startHex = new HexCoordinate(0, 0);
        var created = await starter.StartStandaloneCellsAsync(
            "owner", "Unmapped travel", new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
            Context(), startHex);
        var service = new HexCrawlService(store);
        var world = (await service.ResolveCellGeometryAsync(created, "owner")).Tiling;
        var heading = SpatialCourseSelection.FromLegacyHexDirection(
            world, startHex, new HexDirection(0));
        var provenance = new ResolutionProvenance(ResolutionSource.ManualRoll);
        var plan = new CellWatchTravelPlan(
            heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None);
        var inputs = new CellWatchAdvanceInputs(
            ResolvedTravelAmount.Distance(
                new DistanceMeasure(0.25, DistanceUnit.Miles),
                new DistanceMeasure(0.25, DistanceUnit.Miles), provenance),
            new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, provenance),
            ResolvedEncounter.None);

        var forged = inputs with
        {
            Travel = ResolvedTravelAmount.Distance(
                new DistanceMeasure(0.25, DistanceUnit.Miles),
                new DistanceMeasure(0.25, DistanceUnit.Miles),
                new ResolutionProvenance(ResolutionSource.AutomaticRoll))
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdvanceCellExpeditionAsync(created.Id, "owner", created.Version, plan, forged));
        var afterForgery = (await store.GetExpeditionAsync(created.Id, "owner"))!;
        Assert.Equal(created.Version, afterForgery.Version);
        Assert.Empty(afterForgery.Runtime.History);

        await Assert.ThrowsAsync<HexCrawlNotFoundException>(() =>
            service.AdvanceCellExpeditionAsync(created.Id, "other-owner", created.Version, plan, inputs));
        Assert.Null(await store.GetExpeditionAsync(created.Id, "other-owner"));
        var advanced = await service.AdvanceCellExpeditionAsync(
            created.Id, "owner", created.Version, plan, inputs);
        var after = Assert.IsType<CellExpeditionState>(advanced.Runtime);
        Assert.Null(advanced.Knowledge);
        Assert.Equal(1, after.CompletedWatches);
        Assert.NotEqual(((CellExpeditionState)created.Runtime).Traversal.Position,
            after.Traversal.Position);
        Assert.DoesNotContain(after.History, e => e.Hex is not null);
        Assert.Contains(after.History, e =>
            e.Kind == CrawlRuntimeEventKind.ResolutionProvenanceRecorded
            && e.Cell == after.Traversal.CurrentCell
            && e.Message.Contains("Cell travel source submitted as ManualRoll", StringComparison.Ordinal));
        Assert.True(advanced.Version > created.Version);
        await Assert.ThrowsAsync<HexCrawlConcurrencyException>(() =>
            service.AdvanceCellExpeditionAsync(created.Id, "owner", created.Version, plan, inputs));

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();
        var readBack = (await restarted.GetExpeditionAsync(created.Id, "owner"))!;
        var loadedState = Assert.IsType<CellExpeditionState>(readBack.Runtime);
        var loadedGeometry = await new HexCrawlService(restarted)
            .ResolveCellGeometryAsync(readBack, "owner");
        loadedState.Validate(loadedGeometry.Tiling);
        Assert.Equal(after.Traversal, loadedState.Traversal);
        Assert.Equal(after.History.Count, loadedState.History.Count);
        Assert.Equal(advanced.Version, readBack.Version);
    }

    [Fact]
    public void ExactCellRepositionRefusesAnUnfinishedWatchAndOutstandingTurnCharge()
    {
        var session = AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(), Context(), new HexCoordinate(0, 0));
        var state = session.Expedition;
        var world = session.World;
        var target = world.Boundaries(state.Traversal.CurrentCell.Address)[0].To;
        var position = world.Resolve(target.Address).Center;
        var heading = position - state.Traversal.Position;
        var pending = state with
        {
            IntendedHeading = heading,
            ActiveWatch = new CellActiveWatchState(
                1, TimeSpan.FromHours(4), TimeSpan.Zero,
                new CellWatchTravelPlan(heading, false, true,
                    TravelModeSelection.Normal, NavigationAidSelection.None),
                ResolvedEncounter.None, true, null)
        };
        pending.Validate(world);
        Assert.Throws<InvalidOperationException>(() => CrawlRuntimeActions.RepositionCell(
            world, pending, target, position));
        var debt = state with
        {
            PendingTravelBudgetCharge = new DistanceMeasure(0.125, DistanceUnit.Miles)
        };
        debt.Validate(world);
        Assert.Throws<InvalidOperationException>(() => CrawlRuntimeActions.RepositionCell(
            world, debt, target, position));
        Assert.Equal(state.Traversal, pending.Traversal);
        Assert.Empty(pending.History);
        Assert.Equal(state.Traversal, debt.Traversal);
    }

    [Fact]
    public async Task MaplessStartsAcceptPinnedNativeProceduresAndRejectNonExecutablePresets()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var procedures = new CampaignProcedureService(store);
        var starter = new ExpeditionStartService(store, new HexCrawlService(store), procedures);
        var saved = await procedures.CreateFromPresetAsync("owner", "simple-fixed-distance");
        var started = await starter.StartStandaloneCellsAsync(
            "owner", "Native cell session", new ProcedureStartSelection(
                ProcedureId: saved.ProcedureId, ProcedureRevision: saved.Revision),
            Context(), new HexCoordinate(0, 0));
        Assert.IsType<CellExpeditionState>(started.Runtime);
        Assert.Equal(2, GenericProcedureRuntime.Bind(started.CampaignProcedure).Movement.MechanicVersion);
        await Assert.ThrowsAsync<NotSupportedException>(() => starter.StartStandaloneCellsAsync(
            "owner", "Declarative only", new ProcedureStartSelection(PresetKey: "bx"),
            Context(), new HexCoordinate(0, 0)));
        Assert.Single(await store.ListExpeditionsAsync("owner"));
        Assert.Single(await procedures.ListLatestAsync("owner"));
    }
}
