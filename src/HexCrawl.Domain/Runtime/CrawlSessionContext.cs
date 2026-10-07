using System.Buffers.Binary;
using System.Security.Cryptography;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public enum CrawlSessionContextKind
{
    WorldBound,
    AbstractHex,
    NonSpatial
}

public abstract record CrawlSessionContext
{
    public abstract CrawlSessionContextKind Kind { get; }
    public virtual Guid? OverworldId => null;
    public virtual CrawlRuntimeContext? RuntimeContext => null;
    public virtual string DisplayName => Kind.ToString();
}

public sealed record WorldBoundCrawlSessionContext(Guid WorldId) : CrawlSessionContext
{
    public override CrawlSessionContextKind Kind => CrawlSessionContextKind.WorldBound;
    public override Guid? OverworldId => WorldId;
    public override string DisplayName => "World-bound crawl";
}

public sealed record AbstractHexCrawlSessionContext(
    string Name,
    HexOrientation Orientation,
    CrawlRuntimeContext HexContext) : CrawlSessionContext
{
    public override CrawlSessionContextKind Kind => CrawlSessionContextKind.AbstractHex;
    public override CrawlRuntimeContext? RuntimeContext => HexContext;
    public override string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Abstract hex crawl" : Name.Trim();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Abstract-hex context name is required.");
        }
        HexContext.Validate();
    }
}

public sealed record NonSpatialCrawlSessionContext(string Name) : CrawlSessionContext
{
    public override CrawlSessionContextKind Kind => CrawlSessionContextKind.NonSpatial;
    public override string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Non-spatial session" : Name.Trim();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Non-spatial context name is required.");
        }
    }
}

public static class EncounterOccurrenceIdentity
{
    public static Guid Create(Guid runtimeId, long triggerSequence)
    {
        Span<byte> input = stackalloc byte[24];
        runtimeId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64LittleEndian(input[16..], triggerSequence);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}

public sealed record PendingEncounterOccurrence(
    Guid Id,
    long TriggerSequence,
    int WatchNumber,
    EncounterOutcomeKind Outcome,
    TimeSpan ExpeditionElapsedTime,
    HexCoordinate? Hex,
    Guid? LocationId,
    string? Note,
    ResolutionProvenance Provenance);

public abstract record CrawlSessionRuntimeState
{
    public required Guid Id { get; init; }
    public PendingEncounterOccurrence? PendingEncounter { get; init; }
    public IReadOnlyList<CrawlRuntimeEvent> History { get; init; } = [];
}

public sealed record NonSpatialActiveWatchState(
    int WatchNumber,
    TimeSpan TotalDuration,
    TimeSpan Elapsed)
{
    public IReadOnlyList<ParticipantActivityAssignment> ActivityAssignments { get; init; } = [];
    public TimeSpan Remaining => TotalDuration - Elapsed;

    public void Validate()
    {
        if (WatchNumber <= 0)
        {
            throw new InvalidOperationException("Watch number must be positive.");
        }
        if (TotalDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Watch duration must be positive.");
        }
        if (Elapsed < TimeSpan.Zero || Elapsed >= TotalDuration)
        {
            throw new InvalidOperationException("An active non-spatial watch must have elapsed time between zero and its total duration.");
        }
        foreach (var assignment in ActivityAssignments)
        {
            assignment.ValidateStructure();
        }
        if (ActivityAssignments.Select(value => value.Id).Distinct().Count() != ActivityAssignments.Count)
        {
            throw new InvalidOperationException("Active non-spatial participant activity assignment ids must be unique.");
        }
    }
}

public sealed record NonSpatialSessionState : CrawlSessionRuntimeState
{
    public TimeSpan ElapsedTime { get; init; }
    public int CompletedWatches { get; init; }
    public NonSpatialActiveWatchState? ActiveWatch { get; init; }
}
