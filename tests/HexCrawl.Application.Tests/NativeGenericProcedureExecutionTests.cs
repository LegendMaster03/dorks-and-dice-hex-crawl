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
    public void NativeRuntimeExecutesModuleOverrideFromPinnedProcedure()
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
        var engine = new CrawlRuntimeEngine();

        var result = engine.Advance(
            Context(),
            revised,
            SpatialState(),
            Plan(),
            new WatchAdvanceInputs(TravelDistanceResolver.Fixed(Miles(2))));

        Assert.Equal(TimeSpan.FromHours(6), result.Expedition.ElapsedTravelTime);
        Assert.Equal(1, result.Expedition.CompletedWatches);
        Assert.Null(result.Expedition.ActiveWatch);
        Assert.Equal(2, result.Expedition.DistanceTraveled.Value, 6);
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
    public async Task WorldBoundWorkbenchPinsGenericProcedureAndExecutesWithoutRulesCore()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var core = new HexCrawlService(store);
        var contextResolver = new CrawlSessionContextResolver(core);
        var workbench = new ExpeditionWorkbenchService(store, core, contextResolver);
        var world = await core.CreateOverworldAsync(Owner, WorldCommand());

        var started = await workbench.StartAsync(
            world.World.Id,
            Owner,
            new StartExpeditionWorkbenchCommand(
                "Native world-bound",
                "simple-fixed-distance",
                "exploration-map",
                new HexCoordinate(0, 0)));

        Assert.NotNull(started.CampaignProcedure);
        var advanced = await workbench.AdvanceAsync(
            started.Id,
            Owner,
            FixedAdvance(started.Version, 2));

        var state = Assert.IsType<ExpeditionState>(advanced.Runtime);
        Assert.Equal(1, state.CompletedWatches);
        Assert.Equal(TimeSpan.FromHours(4), state.ElapsedTravelTime);
        Assert.NotNull(advanced.CampaignProcedure);
    }

    [Fact]
    public async Task AbstractHexSessionExecutesPinnedGenericProcedureAfterRestart()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var sessions = new CrawlSessionService(store);
        var started = await sessions.StartAsync(
            Owner,
            new StartStandaloneCrawlSessionCommand(
                "Native abstract hex",
                "simple-fixed-distance",
                new AbstractHexCrawlSessionContext("Abstract region", HexOrientation.PointyTop, Context()),
                new HexCoordinate(0, 0)));
        Assert.NotNull(started.CampaignProcedure);

        var restartedStore = await StoreAsync(database.ConnectionString);
        var restartedCore = new HexCrawlService(restartedStore);
        var restartedWorkbench = new ExpeditionWorkbenchService(
            restartedStore,
            restartedCore,
            new CrawlSessionContextResolver(restartedCore));
        var loaded = await restartedCore.GetExpeditionAsync(started.Id, Owner);

        var advanced = await restartedWorkbench.AdvanceAsync(
            loaded.Id,
            Owner,
            FixedAdvance(loaded.Version, 2));

        var state = Assert.IsType<ExpeditionState>(advanced.Runtime);
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
        Assert.NotNull(advanced.CampaignProcedure);
    }

    [Fact]
    public async Task MissingOrRenamedOriginPresetDoesNotAffectNativeExecution()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var sessions = new CrawlSessionService(store);
        var started = await sessions.StartAsync(
            Owner,
            new StartStandaloneCrawlSessionCommand(
                "Detached origin",
                "simple-fixed-distance",
                new AbstractHexCrawlSessionContext("Detached", HexOrientation.PointyTop, Context()),
                new HexCoordinate(0, 0)));
        var detached = started with
        {
            ProcedureOrigin = new ProcedureOriginMetadata("removed-or-renamed-preset", "Removed preset", 999)
        };
        var saved = await store.SaveExpeditionAsync(detached, started.Version);
        Assert.Equal(SaveOutcome.Saved, saved.Outcome);
        var persisted = Assert.IsType<StoredExpedition>(saved.Value);

        var core = new HexCrawlService(store);
        var workbench = new ExpeditionWorkbenchService(store, core, new CrawlSessionContextResolver(core));
        var advanced = await workbench.AdvanceAsync(
            persisted.Id,
            Owner,
            FixedAdvance(persisted.Version, 1));

        Assert.Equal("removed-or-renamed-preset", advanced.ProcedureOrigin?.PresetKey);
        Assert.Equal(1, Assert.IsType<ExpeditionState>(advanced.Runtime).CompletedWatches);
    }

    [Fact]
    public async Task GenericSnapshotIsRuntimeAuthorityEvenIfCompatibilityProfileDiffersInMemory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var sessions = new CrawlSessionService(store);
        var started = await sessions.StartAsync(
            Owner,
            new StartStandaloneCrawlSessionCommand(
                "Generic authority",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Authority")));
        var contradictoryCompatibilityData = started with
        {
            Procedure = started.Procedure with { WatchLength = TimeSpan.FromHours(99) }
        };

        var runtime = ExpeditionProcedureExecutionResolver.Resolve(contradictoryCompatibilityData);

        Assert.Equal(TimeSpan.FromHours(4), runtime.Time.IntervalDuration);
        Assert.Equal(TimeSpan.FromHours(99), contradictoryCompatibilityData.Procedure.WatchLength);
    }

    [Fact]
    public async Task HistoricalProfileOnlySessionRemainsExecutableThroughCompatibilityBoundary()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = await StoreAsync(database.ConnectionString);
        var sessions = new CrawlSessionService(store);
        var started = await sessions.StartAsync(
            Owner,
            new StartStandaloneCrawlSessionCommand(
                "Legacy profile only",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Legacy clock")));
        var profileOnlySave = await store.SaveExpeditionAsync(
            started with { CampaignProcedure = null },
            started.Version);
        Assert.Equal(SaveOutcome.Saved, profileOnlySave.Outcome);
        var profileOnly = Assert.IsType<StoredExpedition>(profileOnlySave.Value);
        Assert.Null(profileOnly.CampaignProcedure);

        var core = new HexCrawlService(store);
        var assistants = new ExpeditionAssistantService(store, core, new CrawlSessionContextResolver(core));
        var advanced = await assistants.RecordNonSpatialWatchAsync(
            profileOnly.Id,
            Owner,
            new NonSpatialWatchAssistantCommand
            {
                ExpectedVersion = profileOnly.Version,
                ElapsedHours = 4,
                ResolutionSource = ResolutionSource.ProcedureDefault
            });

        Assert.Null(advanced.CampaignProcedure);
        Assert.Equal(1, Assert.IsType<NonSpatialSessionState>(advanced.Runtime).CompletedWatches);
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

    private static AdvanceExpeditionWorkbenchCommand FixedAdvance(long version, double distance) => new()
    {
        ExpectedVersion = version,
        IntendedDirection = 0,
        EffectiveDistance = distance,
        ContinueAcrossBoundaries = true,
        TravelResolutionSource = ResolutionSource.ProcedureDefault
    };

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
