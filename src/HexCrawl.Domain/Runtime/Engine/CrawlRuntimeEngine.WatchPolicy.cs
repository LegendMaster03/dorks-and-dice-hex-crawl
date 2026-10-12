using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

/// <summary>
/// Shared policy for native cell-watch scheduling and encounter events.
/// No coordinate projection or legacy spatial execution path is involved.
/// </summary>
public sealed partial class CrawlRuntimeEngine
{
    private readonly record struct WatchStartPolicy(
        int WatchNumber,
        TimeSpan Duration,
        ResolvedEncounter Encounter,
        bool EncounterCheckDue);

    private static WatchStartPolicy PrepareWatchStart(
        GenericProcedureRuntime procedure,
        CrawlSessionRuntimeState state,
        int completedWatches,
        ResolvedEncounter? suppliedEncounter)
    {
        if (completedWatches < 0)
            throw new InvalidOperationException("Completed watch count must be nonnegative.");
        var checkDue = GenericProcedureRuntimeRequirements.IsEncounterCheckDue(procedure, state);
        var encounter = ResolveEncounterForNewWatch(
            procedure.Time.IntervalDuration, checkDue, suppliedEncounter);
        return new WatchStartPolicy(
            checked(completedWatches + 1), procedure.Time.IntervalDuration,
            encounter, checkDue);
    }

    private static bool ValidateLostRecognitionDecision(BoundaryNavigationDecision? decision)
    {
        if (decision is null)
            throw new InvalidOperationException(
                "A lost-recognition boundary decision is required before travel can continue.");
        if (decision.Reorient && !decision.RecognizedLost)
            throw new InvalidOperationException(
                "Reorientation requires recognizing that the expedition is lost.");
        return decision.RecognizedLost && decision.Reorient;
    }

    private readonly record struct WatchEncounterWindow(
        TimeSpan Duration, bool EncounterAtEnd, bool EncounterImmediatelyDue);

    private static WatchEncounterWindow ResolveEncounterWindow(
        TimeSpan callRemaining,
        TimeSpan elapsed,
        TimeSpan totalDuration,
        ResolvedEncounter encounter,
        bool alreadyHandled)
    {
        if (alreadyHandled || encounter.Kind == EncounterOutcomeKind.None)
            return new WatchEncounterWindow(callRemaining, false, false);

        var due = encounter.OccursAt
            ?? throw new InvalidOperationException("A triggered encounter needs its scheduled watch time.");
        if (due < TimeSpan.Zero || due > totalDuration)
            throw new InvalidOperationException("The encounter falls outside the active watch.");
        if (due <= elapsed)
            return new WatchEncounterWindow(TimeSpan.Zero, false, true);

        var untilEncounter = due - elapsed;
        if (untilEncounter <= callRemaining)
            return new WatchEncounterWindow(untilEncounter, true, false);
        return new WatchEncounterWindow(callRemaining, false, false);
    }

    private static bool HasEncounterDueAtCurrentTime(
        TimeSpan remaining,
        TimeSpan elapsed,
        TimeSpan duration,
        ResolvedEncounter encounter,
        bool handled) =>
        ResolveEncounterWindow(remaining, elapsed, duration, encounter, handled)
            .EncounterImmediatelyDue;

    private static ResolvedEncounter ResolveEncounterForNewWatch(
        TimeSpan intervalDuration,
        bool encounterCheckDue,
        ResolvedEncounter? supplied)
    {
        if (!encounterCheckDue)
            return ResolvedEncounter.None;
        var encounter = supplied
            ?? throw new InvalidOperationException(
                "This procedure requires an explicit resolved encounter-check outcome.");
        if (encounter.Kind == EncounterOutcomeKind.None)
            return encounter;
        if (encounter.OccursAt is null || encounter.OccursAt < TimeSpan.Zero || encounter.OccursAt > intervalDuration)
            throw new InvalidOperationException("Triggered encounter time must fall within the watch.");
        if (encounter.Kind == EncounterOutcomeKind.KeyedLocationDiscovery && encounter.LocationId is null)
            throw new InvalidOperationException("A keyed-location encounter requires a location id.");
        return encounter;
    }

    private static CrawlRuntimeEvent RecordEncounterTrigger(
        Guid expeditionId,
        int watchNumber,
        TimeSpan elapsed,
        WorldCellId cell,
        ResolvedEncounter encounter,
        EventCollector events,
        string message)
    {
        var occurrenceId = EncounterOccurrenceIdentity.Create(expeditionId, events.NextSequence);
        return events.Add(
            watchNumber, CrawlRuntimeEventKind.EncounterTriggered,
            elapsed, message,
            subjectId: encounter.LocationId,
            encounterOutcome: encounter.Kind,
            encounterNote: encounter.Note,
            encounterProvenance: encounter.Provenance,
            encounterOccurrenceId: occurrenceId,
            cell: cell);
    }

    private static string? ResolveDmOverrideNote(
        ResolutionProvenance travel,
        ResolutionProvenance? navigation,
        ResolutionProvenance? encounter,
        ResolutionProvenance? boundary,
        string? overrideNote)
    {
        if (travel.Source != ResolutionSource.DmOverride
            && navigation?.Source != ResolutionSource.DmOverride
            && encounter?.Source != ResolutionSource.DmOverride
            && boundary?.Source != ResolutionSource.DmOverride
            && string.IsNullOrWhiteSpace(overrideNote))
            return null;
        return overrideNote
            ?? travel.Note
            ?? navigation?.Note
            ?? encounter?.Note
            ?? boundary?.Note
            ?? "DM supplied an authoritative override.";
    }
}
