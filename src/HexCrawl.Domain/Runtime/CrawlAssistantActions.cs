using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed record TravelWatchAssistantInput(
    TimeSpan ElapsedTime,
    ResolvedTravelAmount Travel,
    HexCoordinate ResultingHex,
    DistanceMeasure? HexProgress,
    HexDirection? IntendedDirection,
    HexDirection? ActualDirection,
    bool CompleteWatch,
    string? Note = null);

public sealed record NonSpatialWatchAssistantInput(
    TimeSpan ElapsedTime,
    ResolutionProvenance Provenance,
    string? Note = null);

public sealed record NavigationAssistantInput(
    bool IsLost,
    int VeerSteps,
    HexDirection? IntendedDirection,
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
