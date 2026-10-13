using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Runtime;

public sealed record NonSpatialWatchAssistantInput(
    TimeSpan ElapsedTime,
    ResolutionProvenance Provenance,
    string? Note = null);

public sealed record EncounterCadenceAssistantInput(
    EncounterOutcomeKind Outcome,
    ResolutionProvenance Provenance,
    string? Note = null);


/// <summary>
/// Stable facade for focused crawl assistant actions. Implementations are separated by
/// assistant so travel/watch, navigation, and encounter work stays locally navigable.
/// </summary>
public static partial class CrawlAssistantActions
{
}
