using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class EncounterHandoffHistoricalContextTests
{
    [Fact]
    public async Task RuntimeHandoffUsesLocationSnapshotCapturedAtEncounterTime()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, starter) = await database.ServicesAsync();
        var world = await core.CreatePeriodicOverworldAsync(
            "alice", "Historical handoff world", MaplessSessionFixture.Start().World);
        var startGeometry = world.World.SpatialTiling.Resolve(
            new PeriodicCellAddress("face-0", new LatticeDisplacement(0, 0)));
        world = await core.CreateLocationAsync(world.World.Id, "alice", new CreateLocationCommand(
            "Old Ruin",
            "ruin",
            startGeometry.Center,
            LocationDiscoverability.Hidden,
            world.Version));
        var originalLocation = Assert.Single(world.World.Locations);

        var startCell = startGeometry.Id;
        var expedition = await starter.StartWorldBoundCellsAsync(
            world.World.Id, "alice", "Historical location",
            new ProcedureStartSelection(PresetKey: "alexandrian-advanced"),
            startCell.Address, "dm-controlled");
        var heading = PeriodicCourseFixture.ThroughInterface(world.World.SpatialTiling, startCell);
        var source = new ResolutionProvenance(ResolutionSource.ManualRoll);
        expedition = await core.AdvanceCellExpeditionAsync(
            expedition.Id, "alice", expedition.Version,
            new CellWatchTravelPlan(
                heading, false, false, TravelModeSelection.Normal, NavigationAidSelection.None),
            new CellWatchAdvanceInputs(
                ResolvedTravelAmount.Distance(
                    new DistanceMeasure(1, DistanceUnit.Miles),
                    new DistanceMeasure(1, DistanceUnit.Miles), source),
                new ResolvedCellNavigation(NavigationCheckOutcome.Succeeded, null, source),
                new ResolvedEncounter(
                    EncounterOutcomeKind.KeyedLocationDiscovery, TimeSpan.Zero,
                    originalLocation.Id, null, source)));

        var encounterEvent = Assert.Single(
            expedition.Runtime.History,
            value => value.Kind == CrawlRuntimeEventKind.EncounterTriggered);
        Assert.Equal(startCell, encounterEvent.Cell);
        var historicalLocation = Assert.IsType<CrawlRuntimeLocationSnapshot>(encounterEvent.EncounterLocation);
        Assert.Equal(originalLocation.Id, historicalLocation.Id);
        Assert.Equal("Old Ruin", historicalLocation.Name);
        Assert.Equal("ruin", historicalLocation.Category);

        world = await core.UpdateLocationAsync(
            world.World.Id,
            originalLocation.Id,
            "alice",
            new UpdateLocationCommand(
                "Renamed Settlement",
                "settlement",
                originalLocation.Position,
                originalLocation.Discoverability,
                world.Version));
        Assert.Equal("Renamed Settlement", Assert.Single(world.World.Locations).Name);

        var handoff = await new EncounterHandoffService(core).CreateAsync(
            expedition.Id,
            "alice",
            new CreateEncounterHandoffCommand(
                expedition.Version,
                Guid.NewGuid(),
                null,
                encounterEvent.Sequence,
                null));

        Assert.Equal(world.World.Id, handoff.WorldContext.OverworldId);
        Assert.Equal(startCell, handoff.WorldContext.Cell);
        var handoffLocation = Assert.IsType<EncounterHandoffLocation>(handoff.WorldContext.Location);
        Assert.Equal(originalLocation.Id, handoffLocation.Id);
        Assert.Equal("Old Ruin", handoffLocation.Name);
        Assert.Equal("ruin", handoffLocation.Category);
        Assert.Empty(handoff.LinkedScenes);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        private string ConnectionString => _database.ConnectionString;

        private TestDatabase(PostgresTestDatabase database)
        {
            _database = database;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var database = await PostgresTestDatabase.CreateAsync();
            var store = new PostgresHexCrawlStore(database.ConnectionString);
            await store.InitializeAsync();
            return new TestDatabase(database);
        }

        public async Task<(HexCrawlService Core, ExpeditionStartService Starter)> ServicesAsync()
        {
            var store = new PostgresHexCrawlStore(ConnectionString);
            await store.InitializeAsync();
            var core = new HexCrawlService(store);
            return (core, new ExpeditionStartService(store, core, new CampaignProcedureService(store)));
        }

        public ValueTask DisposeAsync() => _database.DisposeAsync();
    }
}
