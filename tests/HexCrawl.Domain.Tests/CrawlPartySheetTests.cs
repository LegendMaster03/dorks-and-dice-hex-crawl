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
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Participant,
                    scout,
                    "navigate",
                    "navigator")
            ],
            BaseMovement = new PartyMovementReference
            {
                PerHour = new DistanceMeasure(3, DistanceUnit.Miles),
                PerWatch = new DistanceMeasure(12, DistanceUnit.Miles),
                LimitingMemberId = guard
            }
        };

        sheet.Validate();
        Assert.Equal("navigate", sheet.ActivityAssignments.Single().ActivityKey);
        Assert.Equal("navigator", sheet.ActivityAssignments.Single().RoleKey);
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
    public void ParticipantAssignmentMustTargetKnownMember()
    {
        var known = Guid.NewGuid();
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(known, "Known")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Participant,
                    Guid.NewGuid(),
                    "custom-activity",
                    null)
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(() => sheet.Validate());

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public void RoleAssignmentMustTargetKnownMember()
    {
        var known = Guid.NewGuid();
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(known, "Known")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Role,
                    Guid.NewGuid(),
                    null,
                    "custom-role")
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(() => sheet.Validate());

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public void PartyWideAssignmentDoesNotRequireSyntheticMember()
    {
        var assignment = new ParticipantActivityAssignment(
            Guid.NewGuid(),
            ParticipantActivityAssignmentScope.Party,
            null,
            "custom-party-activity",
            null);
        var sheet = new CrawlPartySheet { ActivityAssignments = [assignment] };

        sheet.Validate();

        Assert.Null(sheet.ActivityAssignments.Single().ParticipantId);
    }

    [Fact]
    public void DuplicateActivityAssignmentIdentityFailsValidation()
    {
        var member = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Known")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    assignmentId,
                    ParticipantActivityAssignmentScope.Participant,
                    member,
                    "first",
                    null),
                new ParticipantActivityAssignment(
                    assignmentId,
                    ParticipantActivityAssignmentScope.Participant,
                    member,
                    "second",
                    null)
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(() => sheet.Validate());

        Assert.Contains("ids must be unique", error.Message);
    }

    [Fact]
    public void ParticipantAssignmentRequiresNonBlankActivityKey()
    {
        var member = Guid.NewGuid();
        var assignment = new ParticipantActivityAssignment(
            Guid.NewGuid(),
            ParticipantActivityAssignmentScope.Participant,
            member,
            " ",
            null);
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Known")],
            ActivityAssignments = [assignment]
        };

        var error = Assert.Throws<InvalidOperationException>(() => sheet.Validate());

        Assert.Contains("Activity key can not be blank", error.Message);
    }

    [Fact]
    public void RoleAssignmentRequiresNonBlankRoleKey()
    {
        var member = Guid.NewGuid();
        var assignment = new ParticipantActivityAssignment(
            Guid.NewGuid(),
            ParticipantActivityAssignmentScope.Role,
            member,
            null,
            " ");
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Known")],
            ActivityAssignments = [assignment]
        };

        var error = Assert.Throws<InvalidOperationException>(() => sheet.Validate());

        Assert.Contains("Role key can not be blank", error.Message);
    }

    [Fact]
    public void GenericUnknownActivityAndRoleKeysRequireNoDomainChange()
    {
        var member = Guid.NewGuid();
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Known")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Participant,
                    member,
                    "future-custom-activity",
                    "future-custom-role")
            ]
        };

        sheet.Validate();

        var assignment = Assert.Single(sheet.ActivityAssignments);
        Assert.Equal("future-custom-activity", assignment.ActivityKey);
        Assert.Equal("future-custom-role", assignment.RoleKey);
    }

    [Fact]
    public void RoleAndActivityNameSimilarityCreatesNoImplicitMapping()
    {
        var member = Guid.NewGuid();
        var assignment = new ParticipantActivityAssignment(
            Guid.NewGuid(),
            ParticipantActivityAssignmentScope.Role,
            member,
            "search",
            "navigator");
        var sheet = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Known")],
            ActivityAssignments = [assignment]
        };

        sheet.Validate();

        Assert.Equal("search", sheet.ActivityAssignments.Single().ActivityKey);
        Assert.Equal("navigator", sheet.ActivityAssignments.Single().RoleKey);
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
