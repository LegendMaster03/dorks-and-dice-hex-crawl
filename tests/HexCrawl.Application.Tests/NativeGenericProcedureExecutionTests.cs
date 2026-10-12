using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class NativeGenericProcedureExecutionTests
{
    private const string Owner = "alice";

    [Fact]
    public void NativeCellRuntimeExecutesModuleOverrideFromPinnedProcedure()
    {
        var baseProcedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
        var revised = CampaignProcedureMaterializer.CreateRevision(
            baseProcedure,
            [new CampaignProcedureOverride(
                "six-hour-native-runtime",
                GenericProcedureCatalog.TimeIntervalModule,
                null,
                null,
                new Dictionary<string, string> { ["durationTicks"] = sixHours })]);
        var context = new AbstractHexCrawlSessionContext(
            "Native cell override", HexOrientation.PointyTop, Context());
        var start = AbstractCellSessionGeometry.CreateNewHexSession(
            Guid.NewGuid(), context, new HexCoordinate(0, 0));
        var heading = SpatialCourseSelection.FromLegacyHexDirection(
            start.World, new HexCoordinate(0, 0), new HexDirection(0));
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        var result = new CrawlRuntimeEngine().AdvanceCellWatch(
            start.World, revised, start.Expedition,
            new CellWatchTravelPlan(
                heading, false, true, TravelModeSelection.Normal, NavigationAidSelection.None),
            new CellWatchAdvanceInputs(
                ResolvedTravelAmount.Distance(Miles(2), Miles(2), source),
                null, ResolvedEncounter.None));

        Assert.Equal(TimeSpan.FromHours(6), result.Expedition.ElapsedTravelTime);
        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Null(result.Expedition.ActiveWatch);
        Assert.Equal(2, result.Expedition.DistanceTraveled!.Value.Value, 6);
    }

    [Fact]
    public void NativeRuntimeRejectsUnsupportedPinnedMechanicWithoutRewritingSnapshot()
    {
        var original = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var movement = original.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        const string futureMechanic = "future-native-movement";
        const string futureHandler = "future.native.movement";
        var changedMovement = movement with
        {
            Module = movement.Module with
            {
                CompatibleMechanicTypes = movement.Module.CompatibleMechanicTypes
                    .Concat([futureMechanic])
                    .ToArray()
            },
            Mechanic = movement.Mechanic with
            {
                Key = futureMechanic,
                DisplayName = "Future native movement",
                ExecutionHandler = futureHandler,
                Version = 99
            }
        };
        var future = original with
        {
            Modules = original.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule
                    ? changedMovement
                    : module)
                .ToArray()
        };
        future.Validate();

        var exception = Assert.Throws<UnsupportedProcedureMechanicException>(() =>
            GenericProcedureRuntime.Bind(future));

        Assert.Equal(futureHandler, exception.ExecutionHandler);
        Assert.Equal(futureMechanic, exception.MechanicKey);
        Assert.Equal(futureMechanic, future.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule).Mechanic.Key);
    }

    [Fact]
    public async Task WorldBoundCellRuntimePinsNativeProcedureAndExecutesWithoutRulesCore()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var world = await core.CreateOverworldAsync(Owner, WorldCommand());
        var started = await new ExpeditionStartService(
            store, core, new CampaignProcedureService(store))
            .StartWorldBoundCellsAsync(
                world.World.Id, Owner, "Native world-bound",
                new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                LegacyHexTilingCompatibility.ToAddress(new HexCoordinate(0, 0)));

        var advanced = await AdvanceCellAsync(core, started, 2);
        var state = Assert.IsType<CellExpeditionState>(advanced.Runtime);
        Assert.Equal(1, state.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(4), state.ElapsedTravelTime);
        Assert.Equal(started.CampaignProcedure, advanced.CampaignProcedure);
        Assert.Equal(2, GenericProcedureRuntime.Bind(advanced.CampaignProcedure).Movement.MechanicVersion);
    }

    [Fact]
    public async Task AbstractCellSessionExecutesPinnedGenericProcedureAfterRestart()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var started = await new ExpeditionStartService(
            store, core, new CampaignProcedureService(store))
            .StartStandaloneCellsAsync(
                Owner, "Native abstract cell",
                new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                new AbstractHexCrawlSessionContext("Abstract region", HexOrientation.PointyTop, Context()),
                new HexCoordinate(0, 0));

        var restartedStore = await StoreAsync(database.ConnectionString);
        var restartedCore = new HexCrawlService(restartedStore);
        var loaded = await restartedCore.GetExpeditionAsync(started.Id, Owner);
        var advanced = await AdvanceCellAsync(restartedCore, loaded, 2);

        var state = Assert.IsType<CellExpeditionState>(advanced.Runtime);
        Assert.Equal(1, state.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(4), state.ElapsedTravelTime);
        Assert.Equal(started.CampaignProcedure, advanced.CampaignProcedure);
    }

    [Fact]
    public async Task NonSpatialAssistantExecutesPinnedGenericInterval()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var sessions = new CrawlSessionService(store);
        var core = new HexCrawlService(store);
        var assistants = new ExpeditionAssistantService(store, core, new CrawlSessionContextResolver(core));
        var started = await sessions.StartAsync(
            Owner,
            new StartStandaloneCrawlSessionCommand(
                "Native non-spatial",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));

        var advanced = await assistants.RecordNonSpatialWatchAsync(
            started.Id,
            Owner,
            new NonSpatialWatchAssistantCommand
            {
                ExpectedVersion = started.Version,
                ElapsedHours = 4,
                ResolutionSource = ResolutionSource.ProcedureDefault
            });

        var state = Assert.IsType<NonSpatialSessionState>(advanced.Runtime);
        Assert.Equal(1, state.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(4), state.ElapsedTime);
        Assert.Null(state.ActiveWatch);
        Assert.Equal(started.CampaignProcedure, advanced.CampaignProcedure);
    }

    [Fact]
    public async Task MissingOrRenamedOriginPresetDoesNotAffectNativeExecution()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var started = await new ExpeditionStartService(
            store, core, new CampaignProcedureService(store))
            .StartStandaloneCellsAsync(
                Owner, "Detached origin",
                new ProcedureStartSelection(PresetKey: "simple-fixed-distance"),
                new AbstractHexCrawlSessionContext("Detached", HexOrientation.PointyTop, Context()),
                new HexCoordinate(0, 0));
        var detached = started with
        {
            ProcedureOrigin = new ProcedureOriginMetadata("removed-or-renamed-preset", "Removed preset", 999)
        };
        var saved = await store.SaveExpeditionAsync(detached, started.Version);
        Assert.Equal(SaveOutcome.Saved, saved.Outcome);
        var persisted = Assert.IsType<StoredExpedition>(saved.Value);

        var advanced = await AdvanceCellAsync(core, persisted, 1);
        Assert.Equal("removed-or-renamed-preset", advanced.ProcedureOrigin?.PresetKey);
        Assert.Equal(started.CampaignProcedure, advanced.CampaignProcedure);
        Assert.Equal(1, Assert.IsType<CellExpeditionState>(advanced.Runtime).CompletedWatches);
    }

    private static async Task<StoredExpedition> AdvanceCellAsync(
        HexCrawlService service, StoredExpedition expedition, double distance)
    {
        var cells = Assert.IsType<CellExpeditionState>(expedition.Runtime);
        var geometry = (await service.ResolveCellGeometryAsync(expedition, Owner)).Tiling;
        var hex = LegacyHexTilingCompatibility.ToHex(cells.Traversal.CurrentCell.Address);
        var heading = SpatialCourseSelection.FromLegacyHexDirection(
            geometry, hex, new HexDirection(0));
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        return await service.AdvanceCellExpeditionAsync(
            expedition.Id, Owner, expedition.Version,
            new CellWatchTravelPlan(
                heading, false, true, TravelModeSelection.Normal, NavigationAidSelection.None),
            new CellWatchAdvanceInputs(
                ResolvedTravelAmount.Distance(Miles(distance), Miles(distance), source),
                new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, source),
                ResolvedEncounter.None));
    }

    private static async Task<PostgresHexCrawlStore> StoreAsync(string connectionString)
    {
        var store = new PostgresHexCrawlStore(connectionString);
        await store.InitializeAsync();
        return store;
    }

    private static CrawlRuntimeContext Context() => new(Miles(12));

    private static ExpeditionState SpatialState() => new()
    {
        Id = Guid.NewGuid(),
        Position = new WorldPoint(0, 0),
        PositionPrecision = WorldPositionPrecision.HexAnchor,
        Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
        Navigation = new NavigationRuntimeState(false, 0),
        DistanceTraveled = Miles(0)
    };

    private static WatchTravelPlan Plan() => new(
        new HexDirection(0),
        TravelModeSelection.Normal,
        NavigationAidSelection.None,
        false,
        true);

    private static DistanceMeasure Miles(double value) => new(value, DistanceUnit.Miles);

    private static CreateOverworldCommand WorldCommand() => new(
        "World",
        HexOrientation.PointyTop,
        new WorldPoint(0, 0),
        0,
        1,
        12,
        DistanceUnit.Miles);
}
