using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Runtime;

public static partial class CrawlAssistantActions
{
    public static NonSpatialSessionState RecordEncounterCadence(
        NonSpatialSessionState state,
        EncounterCadenceAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        if (state.PendingEncounter is not null)
        {
            throw new InvalidOperationException("Resolve the pending encounter before recording another encounter cadence result.");
        }
        if (input.Outcome == EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            throw new InvalidOperationException("Keyed-location discovery requires a world-bound crawl session.");
        }

        var events = new List<CrawlRuntimeEvent>();
        var watchNumber = state.ActiveWatch?.WatchNumber ?? Math.Max(1, state.CompletedWatches + 1);
        events.Add(Event(
            state,
            events,
            watchNumber,
            CrawlRuntimeEventKind.EncounterCheckPerformed,
            state.ElapsedTime,
            $"Encounter cadence assistant recorded {input.Outcome}; {Describe(input.Provenance)}{NoteSuffix(input.Note)}."));
        PendingEncounterOccurrence? pendingEncounter = null;
        if (input.Outcome != EncounterOutcomeKind.None)
        {
            var occurrenceId = Guid.NewGuid();
            var triggered = Event(
                state,
                events,
                watchNumber,
                CrawlRuntimeEventKind.EncounterTriggered,
                state.ElapsedTime,
                string.IsNullOrWhiteSpace(input.Note)
                    ? $"{input.Outcome} triggered."
                    : $"{input.Outcome}: {input.Note.Trim()}",
                encounterOutcome: input.Outcome,
                encounterNote: input.Note,
                encounterProvenance: input.Provenance,
                encounterOccurrenceId: occurrenceId);
            events.Add(triggered);
            pendingEncounter = new PendingEncounterOccurrence(
                occurrenceId, triggered.Sequence, watchNumber, input.Outcome,
                state.ElapsedTime, null, null, input.Note, input.Provenance);
        }
        events.Add(ProvenanceEvent(
            state,
            events,
            watchNumber,
            state.ElapsedTime,
            $"encounter-assistant={Describe(input.Provenance)}"));

        return state with
        {
            PendingEncounter = pendingEncounter,
            History = [.. state.History, .. events]
        };
    }
}
