using HexCrawl.Application;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Web.Api;

public sealed record CrawlPartyMemberContract(
    Guid Id,
    string Name,
    string? ExternalCharacterId,
    bool CountsTowardPartyMovement)
{
    public static CrawlPartyMemberContract From(CrawlPartyMember member) => new(
        member.Id,
        member.Name,
        member.ExternalCharacterId,
        member.CountsTowardPartyMovement);

    public CrawlPartyMember ToDomain() => new(
        Id,
        Name,
        string.IsNullOrWhiteSpace(ExternalCharacterId) ? null : ExternalCharacterId.Trim(),
        CountsTowardPartyMovement);
}

public sealed record MarchingOrderPositionContract(
    Guid MemberId,
    int Rank,
    int File)
{
    public static MarchingOrderPositionContract From(MarchingOrderPosition position) => new(
        position.MemberId,
        position.Rank,
        position.File);

    public MarchingOrderPosition ToDomain() => new(MemberId, Rank, File);
}

public sealed record WatchRotationEntryContract(
    int Slot,
    IReadOnlyList<Guid> MemberIds,
    string? Label)
{
    public static WatchRotationEntryContract From(WatchRotationEntry entry) => new(
        entry.Slot,
        entry.MemberIds,
        entry.Label);

    public WatchRotationEntry ToDomain() => new(
        Slot,
        MemberIds,
        string.IsNullOrWhiteSpace(Label) ? null : Label.Trim());
}

public sealed record StandingOrderContract(
    Guid Id,
    string Text,
    bool Enabled)
{
    public static StandingOrderContract From(StandingOrder order) => new(
        order.Id,
        order.Text,
        order.Enabled);

    public StandingOrder ToDomain() => new(Id, Text.Trim(), Enabled);
}

public sealed record PartyMovementReferenceContract(
    DistanceContract? PerHour,
    DistanceContract? PerWatch,
    DistanceContract? PerMarch,
    Guid? LimitingMemberId,
    string? Note)
{
    public static PartyMovementReferenceContract From(PartyMovementReference movement) => new(
        movement.PerHour is { } perHour ? DistanceContract.From(perHour) : null,
        movement.PerWatch is { } perWatch ? DistanceContract.From(perWatch) : null,
        movement.PerMarch is { } perMarch ? DistanceContract.From(perMarch) : null,
        movement.LimitingMemberId,
        movement.Note);

    public PartyMovementReference ToDomain() => new()
    {
        PerHour = ToDistance(PerHour),
        PerWatch = ToDistance(PerWatch),
        PerMarch = ToDistance(PerMarch),
        LimitingMemberId = LimitingMemberId,
        Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()
    };

    private static DistanceMeasure? ToDistance(DistanceContract? value) =>
        value is null ? null : new DistanceMeasure(value.Value, value.Unit.ToDomain());
}

public sealed record ExpeditionPartyContract(
    IReadOnlyList<CrawlPartyMemberContract> Members,
    IReadOnlyList<MarchingOrderPositionContract> MarchingOrder,
    IReadOnlyList<WatchRotationEntryContract> WatchList,
    IReadOnlyList<StandingOrderContract> StandingOrders,
    Guid? DefaultNavigatorMemberId,
    PartyMovementReferenceContract? BaseMovement)
{
    public static ExpeditionPartyContract From(CrawlPartySheet party) => new(
        party.Members.Select(CrawlPartyMemberContract.From).ToArray(),
        party.MarchingOrder.Select(MarchingOrderPositionContract.From).ToArray(),
        party.WatchList.Select(WatchRotationEntryContract.From).ToArray(),
        party.StandingOrders.Select(StandingOrderContract.From).ToArray(),
        party.DefaultNavigatorMemberId,
        party.BaseMovement is null ? null : PartyMovementReferenceContract.From(party.BaseMovement));

    public CrawlPartySheet ToDomain()
    {
        var party = new CrawlPartySheet
        {
            Members = Members.Select(item => item.ToDomain()).ToArray(),
            MarchingOrder = MarchingOrder.Select(item => item.ToDomain()).ToArray(),
            WatchList = WatchList.Select(item => item.ToDomain()).ToArray(),
            StandingOrders = StandingOrders.Select(item => item.ToDomain()).ToArray(),
            DefaultNavigatorMemberId = DefaultNavigatorMemberId,
            BaseMovement = BaseMovement?.ToDomain()
        };
        party.Validate();
        return party;
    }
}

public sealed record UpdateExpeditionPartyRequest(
    long ExpectedVersion,
    IReadOnlyList<CrawlPartyMemberContract>? Members = null,
    IReadOnlyList<MarchingOrderPositionContract>? MarchingOrder = null,
    IReadOnlyList<WatchRotationEntryContract>? WatchList = null,
    IReadOnlyList<StandingOrderContract>? StandingOrders = null,
    Guid? DefaultNavigatorMemberId = null,
    PartyMovementReferenceContract? BaseMovement = null)
{
    public UpdateExpeditionPartyCommand ToCommand()
    {
        var contract = new ExpeditionPartyContract(
            Members ?? [],
            MarchingOrder ?? [],
            WatchList ?? [],
            StandingOrders ?? [],
            DefaultNavigatorMemberId,
            BaseMovement);
        return new UpdateExpeditionPartyCommand(ExpectedVersion, contract.ToDomain());
    }
}
