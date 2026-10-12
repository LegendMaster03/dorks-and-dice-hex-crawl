using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Runtime;

public static partial class CrawlAssistantActions
{
    private static CrawlRuntimeEvent Event(
        CrawlSessionRuntimeState state,
        IReadOnlyCollection<CrawlRuntimeEvent> pending,
        int watchNumber,
        CrawlRuntimeEventKind kind,
        TimeSpan elapsed,
        string message,
        double? distanceValue = null,
        string? distanceUnit = null,
        EncounterOutcomeKind? encounterOutcome = null,
        string? encounterNote = null,
        ResolutionProvenance? encounterProvenance = null,
        Guid? encounterOccurrenceId = null) =>
        new(
            NextSequence(state, pending),
            watchNumber,
            kind,
            elapsed,
            null,
            message,
            distanceValue,
            distanceUnit,
            EncounterOutcome: encounterOutcome,
            EncounterNote: encounterNote,
            EncounterProvenance: encounterProvenance,
            EncounterOccurrenceId: encounterOccurrenceId);

    private static CrawlRuntimeEvent ProvenanceEvent(
        CrawlSessionRuntimeState state,
        IReadOnlyCollection<CrawlRuntimeEvent> pending,
        int watchNumber,
        TimeSpan elapsed,
        string message) =>
        Event(state, pending, watchNumber, CrawlRuntimeEventKind.ResolutionProvenanceRecorded,
            elapsed, $"Resolved input provenance: {message}.");

    private static long NextSequence(CrawlSessionRuntimeState state, IReadOnlyCollection<CrawlRuntimeEvent> pending) =>
        (state.History.Count == 0 ? 0 : state.History[^1].Sequence) + pending.Count + 1;

    private static string Describe(ResolutionProvenance provenance) =>
        string.IsNullOrWhiteSpace(provenance.Note)
            ? provenance.Source.ToString()
            : $"{provenance.Source} ({provenance.Note.Trim()})";

    private static string NoteSuffix(string? note) =>
        string.IsNullOrWhiteSpace(note) ? "" : $" · {note.Trim()}";

    private static string FormatHours(TimeSpan value) =>
        $"{value.TotalHours.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}h";
}
