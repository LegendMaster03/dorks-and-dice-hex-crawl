using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Non-axial expedition authority. A cell-bound expedition has exactly one
/// authoritative position: Traversal.Position, qualified by Traversal.CurrentCell.
/// No legacy hex coordinate is invented to satisfy old clients.
/// </summary>
public sealed record CellExpeditionState : CrawlSessionRuntimeState
{
    public required PeriodicCellTraversal Traversal { get; init; }
    public WorldPoint? IntendedHeading { get; init; }
    public bool IsLost { get; init; }
    public double? ResolvedVeerDegrees { get; init; }
    public required DistanceMeasure DistanceTraveled { get; init; }
    public TimeSpan ElapsedTravelTime { get; init; }
    public int CompletedWatches { get; init; }
    public CellActiveWatchState? ActiveWatch { get; init; }

    public void Validate(PeriodicWorldTiling world)
    {
        Traversal.Validate(world);
        if (IntendedHeading is { } direction
            && (!double.IsFinite(direction.X) || !double.IsFinite(direction.Y)
                || direction.X == 0 && direction.Y == 0))
            throw new InvalidOperationException("Intended heading must be finite and nonzero.");
        if (ResolvedVeerDegrees is { } veer && !double.IsFinite(veer))
            throw new InvalidOperationException("Resolved veer angle must be finite.");
        if (!IsLost && ResolvedVeerDegrees is not null)
            throw new InvalidOperationException("Resolved veer requires a lost navigation state.");
        if (CompletedWatches < 0 || ElapsedTravelTime < TimeSpan.Zero)
            throw new InvalidOperationException("Expedition time and watch counters must be non-negative.");
        ActiveWatch?.Validate();
    }
}

public sealed record CellWatchTravelPlan(
    WorldPoint IntendedHeading,
    WorldPoint ActualHeading,
    int? ExitInterfaceIndex,
    bool DeliberateDoubleBack,
    bool ContinueAcrossBoundaries,
    TravelModeSelection Mode,
    NavigationAidSelection NavigationAid);

/// <summary>
/// A generalized watch does not store a second spatial position. It preserves
/// its pending decision and chosen course for deterministic resumption.
/// </summary>
public sealed record CellActiveWatchState(
    int WatchNumber,
    TimeSpan TotalDuration,
    TimeSpan Elapsed,
    CellWatchTravelPlan Plan,
    ResolvedEncounter Encounter,
    bool EncounterHandled,
    RuntimePauseReason? PendingDecision)
{
    public TimeSpan Remaining => TotalDuration > Elapsed ? TotalDuration - Elapsed : TimeSpan.Zero;

    public void Validate()
    {
        if (WatchNumber <= 0 || TotalDuration <= TimeSpan.Zero
            || Elapsed < TimeSpan.Zero || Elapsed > TotalDuration)
            throw new InvalidOperationException("Invalid generalized watch duration or progress.");
        foreach (var direction in new[] { Plan.IntendedHeading, Plan.ActualHeading })
            if (!double.IsFinite(direction.X) || !double.IsFinite(direction.Y)
                || direction.X == 0 && direction.Y == 0)
                throw new InvalidOperationException("A generalized travel course requires a nonzero finite heading.");
        if (Plan.ExitInterfaceIndex < 0)
            throw new InvalidOperationException("Selected exit interface cannot be negative.");
        ArgumentNullException.ThrowIfNull(Plan.Mode);
        ArgumentNullException.ThrowIfNull(Plan.NavigationAid);
    }
}
