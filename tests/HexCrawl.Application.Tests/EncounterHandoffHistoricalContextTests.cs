using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Application.Tests;

public sealed class EncounterHandoffHistoricalContextTests
{
    [Fact]
    public async Task RuntimeHandoffUsesLocationSnapshotCapturedAtEncounterTime()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (core, workbench) = await database.ServicesAsync();
        var world = await core.CreateOverworldAsync("alice", new CreateOverworldCommand(
            "Historical handoff world",
            HexOrientation.PointyTop,
            new WorldPoint(0, 0),
            0,
            1,
            12,
            DistanceUnit.Miles));
        world = await core.CreateLocationAsync(world.World.Id, "alice", new CreateLocationCommand(
            "Old Ruin",
            "ruin",
            new WorldPoint(0, 0),
            LocationDiscoverability.Hidden,
            world.Version));
        var originalLocation = Assert.Single(world.World.Locations);

        var expedition = await workbench.StartAsync(
            world.World.Id,
            "alice",
            new StartExpeditionWorkbenchCommand(
                "Historical location",
                "alexandrian-advanced",
                "dm-controlled",
                new HexCoordinate(0, 0)));
        expedition = await workbench.AdvanceAsync(
            expedition.State.Id,
            "alice",
            new AdvanceExpeditionWorkbenchCommand
            {
                ExpectedVersion = expedition.Version,
                IntendedDirection = 0,
                ExpectedDistance = 1,
                ActualDistance = 1,
                NavigationOutcome = NavigationCheckOutcome.Succeeded,
                EncounterOutcome = EncounterOutcomeKind.KeyedLocationDiscovery,
                EncounterHour = 0,
                LocationId = originalLocation.Id,
                TravelResolutionSource = ResolutionSource.ManualRoll,
                NavigationResolutionSource = ResolutionSource.ManualRoll,
                EncounterResolutionSource = ResolutionSource.ManualRoll
            });

        var encounterEvent = Assert.Single(
            expedition.State.History,
            value => value.Kind == CrawlRuntimeEventKind.EncounterTriggered);
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
            expedition.State.Id,
            "alice",
            new CreateEncounterHandoffCommand(
                expedition.Version,
                Guid.NewGuid(),
                null,
                encounterEvent.Sequence,
                null));

        Assert.Equal(world.World.Id, handoff.WorldContext.OverworldId);
        Assert.Equal(new HexCoordinate(0, 0), handoff.WorldContext.Hex);
        var handoffLocation = Assert.IsType<EncounterHandoffLocation>(handoff.WorldContext.Location);
        Assert.Equal(originalLocation.Id, handoffLocation.Id);
        Assert.Equal("Old Ruin", handoffLocation.Name);
        Assert.Equal("ruin", handoffLocation.Category);
        Assert.Empty(handoff.LinkedScenes);
    }
}
