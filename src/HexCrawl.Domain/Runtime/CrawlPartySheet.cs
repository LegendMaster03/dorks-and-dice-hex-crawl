using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Optional party information carried by a crawl session. This is deliberately separate
/// from the movement/navigation engine: it supplies the familiar running-sheet data and
/// can later feed automation without making any of it mandatory.
/// </summary>
public sealed record CrawlPartySheet
{
    public IReadOnlyList<CrawlPartyMember> Members { get; init; } = [];
    public IReadOnlyList<MarchingOrderPosition> MarchingOrder { get; init; } = [];
    public IReadOnlyList<WatchRotationEntry> WatchList { get; init; } = [];
    public IReadOnlyList<StandingOrder> StandingOrders { get; init; } = [];
    public Guid? DefaultNavigatorMemberId { get; init; }
    public PartyMovementReference? BaseMovement { get; init; }

    public static CrawlPartySheet Empty { get; } = new();

    public void Validate()
    {
        var memberIds = new HashSet<Guid>();
        foreach (var member in Members)
        {
            member.Validate();
            if (!memberIds.Add(member.Id))
            {
                throw new InvalidOperationException("Party member ids must be unique.");
            }
        }

        if (DefaultNavigatorMemberId.HasValue && !memberIds.Contains(DefaultNavigatorMemberId.Value))
        {
            throw new InvalidOperationException("The default navigator must reference a party member.");
        }

        var occupiedPositions = new HashSet<(int Rank, int File)>();
        var orderedMembers = new HashSet<Guid>();
        foreach (var position in MarchingOrder)
        {
            position.Validate();
            RequireMember(memberIds, position.MemberId, "Marching order");
            if (!occupiedPositions.Add((position.Rank, position.File)))
            {
                throw new InvalidOperationException("Marching-order positions must be unique.");
            }
            if (!orderedMembers.Add(position.MemberId))
            {
                throw new InvalidOperationException("A party member can appear only once in the marching order.");
            }
        }

        var watchSlots = new HashSet<int>();
        foreach (var watch in WatchList)
        {
            watch.Validate();
            if (!watchSlots.Add(watch.Slot))
            {
                throw new InvalidOperationException("Watch-list slots must be unique.");
            }
            foreach (var memberId in watch.MemberIds)
            {
                RequireMember(memberIds, memberId, "Watch list");
            }
        }

        var standingOrderIds = new HashSet<Guid>();
        foreach (var order in StandingOrders)
        {
            order.Validate();
            if (!standingOrderIds.Add(order.Id))
            {
                throw new InvalidOperationException("Standing-order ids must be unique.");
            }
        }

        BaseMovement?.Validate(memberIds);
    }

    private static void RequireMember(HashSet<Guid> memberIds, Guid memberId, string label)
    {
        if (!memberIds.Contains(memberId))
        {
            throw new InvalidOperationException($"{label} references a party member that does not exist.");
        }
    }
}

public sealed record CrawlPartyMember(
    Guid Id,
    string Name,
    string? ExternalCharacterId = null,
    bool CountsTowardPartyMovement = true)
{
    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Party member id is required.");
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Party member name is required.");
        }
        if (ExternalCharacterId is { Length: > 512 })
        {
            throw new InvalidOperationException("External character id is too long.");
        }
    }
}

public sealed record MarchingOrderPosition(
    Guid MemberId,
    int Rank,
    int File)
{
    public void Validate()
    {
        if (MemberId == Guid.Empty)
        {
            throw new InvalidOperationException("Marching-order member id is required.");
        }
        if (Rank < 0 || File < 0)
        {
            throw new InvalidOperationException("Marching-order rank and file must be non-negative.");
        }
    }
}

public sealed record WatchRotationEntry(
    int Slot,
    IReadOnlyList<Guid> MemberIds,
    string? Label = null)
{
    public void Validate()
    {
        if (Slot <= 0)
        {
            throw new InvalidOperationException("Watch-list slot must be positive.");
        }
        if (MemberIds.Count != MemberIds.Distinct().Count())
        {
            throw new InvalidOperationException("A watch-list slot can not contain the same party member more than once.");
        }
        if (MemberIds.Any(id => id == Guid.Empty))
        {
            throw new InvalidOperationException("Watch-list member ids are required.");
        }
        if (Label is { Length: > 200 })
        {
            throw new InvalidOperationException("Watch-list label is too long.");
        }
    }
}

public sealed record StandingOrder(
    Guid Id,
    string Text,
    bool Enabled = true)
{
    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Standing-order id is required.");
        }
        if (string.IsNullOrWhiteSpace(Text))
        {
            throw new InvalidOperationException("Standing-order text is required.");
        }
        if (Text.Length > 2000)
        {
            throw new InvalidOperationException("Standing-order text is too long.");
        }
    }
}

/// <summary>
/// Familiar Hour / Watch / March reference values from the paper worksheet.
/// Each value is optional and authoritative only when supplied; no duration or unit is assumed.
/// </summary>
public sealed record PartyMovementReference
{
    public DistanceMeasure? PerHour { get; init; }
    public DistanceMeasure? PerWatch { get; init; }
    public DistanceMeasure? PerMarch { get; init; }
    public Guid? LimitingMemberId { get; init; }
    public string? Note { get; init; }

    public void Validate(IReadOnlySet<Guid> memberIds)
    {
        if (LimitingMemberId.HasValue && !memberIds.Contains(LimitingMemberId.Value))
        {
            throw new InvalidOperationException("The limiting movement member must reference a party member.");
        }
        if (Note is { Length: > 1000 })
        {
            throw new InvalidOperationException("Movement reference note is too long.");
        }
    }
}
