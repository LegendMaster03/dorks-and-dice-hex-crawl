using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Tests;

public sealed class CrawlPartySheetTests
{
    [Fact]
    public void EmptyPartySheetIsValid()
    {
        CrawlPartySheet.Empty.Validate();
    }

    [Fact]
    public void FlexibleWorksheetDataValidatesWithoutRequiringEverySection()
    {
        var scout = Guid.NewGuid();
        var guard = Guid.NewGuid();
        var sheet = new CrawlPartySheet
        {
            Members =
            [
                new CrawlPartyMember(scout, "Scout"),
                new CrawlPartyMember(guard, "Guard")
            ],
            MarchingOrder =
            [
                new MarchingOrderPosition(scout, Rank: 0, File: 0),
                new MarchingOrderPosition(guard, Rank: 1, File: 0)
            ],
            WatchList =
            [
                new WatchRotationEntry(1, [guard], "First rest watch")
            ],
            StandingOrders =
            [
                new StandingOrder(Guid.NewGuid(), "Wake the navigator if the trail disappears.")
            ],
            DefaultNavigatorMemberId = scout,
            BaseMovement = new PartyMovementReference
            {
                PerHour = new DistanceMeasure(3, DistanceUnit.Miles),
                PerWatch = new DistanceMeasure(12, DistanceUnit.Miles),
                LimitingMemberId = guard
            }
        };

        sheet.Validate();
    }

    [Fact]
    public void PartyReferencesMustTargetKnownMembers()
    {
        var member = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Known")],
            MarchingOrder = [new MarchingOrderPosition(missing, 0, 0)]
        };

        var error = Assert.Throws<InvalidOperationException>(() => sheet.Validate());

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public void MarchingOrderDoesNotAssumeAFixedNumberOfFiles()
    {
        var members = Enumerable.Range(0, 5)
            .Select(index => new CrawlPartyMember(Guid.NewGuid(), $"Member {index + 1}"))
            .ToArray();
        var sheet = new CrawlPartySheet
        {
            Members = members,
            MarchingOrder = members
                .Select((member, index) => new MarchingOrderPosition(member.Id, 0, index))
                .ToArray()
        };

        sheet.Validate();

        Assert.Equal(5, sheet.MarchingOrder.Count);
    }
}
