using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class Phase20CellMovementCompositionTests
{
    [Fact]
    public void PhysicalTravelSuggestionUsesAuthoritativeCellWatchRemainingTime()
    {
        var original = Session();
        var first = MovementCapabilityComposer.Compose(original);
        Assert.Equal(12d, first.SuggestedExpectedDistance?.Value);
        Assert.Equal(DistanceUnit.Miles, first.SuggestedExpectedDistance?.Unit);

        var state = Assert.IsType<CellExpeditionState>(original.Runtime);
        var direction = new WorldPoint(1, 0);
        var plan = new CellWatchTravelPlan(direction, false, true,
            TravelModeSelection.Normal, NavigationAidSelection.None);
        var partial = state with
        {
            IntendedHeading = direction,
            ActiveWatch = new CellActiveWatchState(
                1, TimeSpan.FromHours(4), TimeSpan.FromHours(2),
                plan, ResolvedEncounter.None, false, null)
        };
        var remaining = MovementCapabilityComposer.Compose(original with { Runtime = partial });
        Assert.Equal(6d, remaining.SuggestedExpectedDistance?.Value);
        Assert.Equal(DistanceUnit.Miles, remaining.SuggestedExpectedDistance?.Unit);
    }

    [Fact]
    public void NoPhysicalCalibrationNeverProducesCellDistanceOrNonspatialTravel()
    {
        var original = Session();
        var state = Assert.IsType<CellExpeditionState>(original.Runtime);
        var uncalibrated = MovementCapabilityComposer.Compose(
            original with { Runtime = state with { DistanceTraveled = null } });
        Assert.Null(uncalibrated.SuggestedExpectedDistance);

        var nonspatial = MovementCapabilityComposer.Compose(original with
        {
            Runtime = new NonSpatialSessionState { Id = original.Id },
            Context = new NonSpatialCrawlSessionContext("Independent interval bookkeeping")
        });
        Assert.Null(nonspatial.SuggestedExpectedDistance);
    }

    private static StoredExpedition Session()
    {
        var started = MaplessSessionFixture.Start();
        var context = new MaplessSpatialCrawlSessionContext(
            "Generalized movement test", started.World);
        var pinned = CampaignProcedureMaterializer.MaterializeExecutableSpatial(
            CrawlProcedureCatalog.Resolve("simple-fixed-distance")).Procedure;
        var now = DateTimeOffset.UtcNow;
        return new StoredExpedition(
            "Movement test", started.Expedition, context, null, pinned, null,
            TimeSpan.FromHours(4), "owner", 1, now, now)
        {
            Party = new CrawlPartySheet
            {
                BaseMovement = new PartyMovementReference
                {
                    PerHour = new DistanceMeasure(3, DistanceUnit.Miles)
                }
            }
        };
    }
}
