using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public static partial class CrawlAssistantActions
{
    private static void RequireStandaloneState(ExpeditionState state)
    {
        if (state.ActiveWatch is not null)
        {
            throw new InvalidOperationException("Focused assistant bookkeeping can not mutate an active full-workbench watch. Resume or finish that watch in the expedition tracker first.");
        }
    }

    private static DistanceMeasure ResolvePhysicalDistance(
        CrawlRuntimeContext context,
        CrawlProcedureProfile profile,
        ResolvedTravelAmount travel)
    {
        if (profile.TravelResolution == TravelResolutionMode.HexSteps)
        {
            if (travel.HexSteps is null || travel.ExpectedDistance is not null || travel.ActualDistance is not null)
            {
                throw new InvalidOperationException("This procedure requires a resolved hex-step count.");
            }
            return new DistanceMeasure(context.HexCenterDistance.Value * travel.HexSteps.Value, context.HexCenterDistance.Unit);
        }

        if (travel.ActualDistance is null || travel.HexSteps is not null)
        {
            throw new InvalidOperationException("This procedure requires a resolved travel distance.");
        }
        return Convert(travel.ActualDistance.Value, context.HexCenterDistance.Unit);
    }

    private static CrawlRuntimeEvent Event(
        CrawlSessionRuntimeState state,
        IReadOnlyCollection<CrawlRuntimeEvent> pending,
        int watchNumber,
        CrawlRuntimeEventKind kind,
        TimeSpan elapsed,
        HexCoordinate? hex,
        string message,
        double? distanceValue = null,
        string? distanceUnit = null) =>
        new(
            NextSequence(state, pending),
            watchNumber,
            kind,
            elapsed,
            hex,
            message,
            distanceValue,
            distanceUnit);

    private static CrawlRuntimeEvent ProvenanceEvent(
        CrawlSessionRuntimeState state,
        IReadOnlyCollection<CrawlRuntimeEvent> pending,
        int watchNumber,
        TimeSpan elapsed,
        HexCoordinate? hex,
        string message) =>
        Event(state, pending, watchNumber, CrawlRuntimeEventKind.ResolutionProvenanceRecorded, elapsed, hex, $"Resolved input provenance: {message}.");

    private static long NextSequence(CrawlSessionRuntimeState state, IReadOnlyCollection<CrawlRuntimeEvent> pending) =>
        (state.History.Count == 0 ? 0 : state.History[^1].Sequence) + pending.Count + 1;

    private static DistanceMeasure Add(DistanceMeasure left, DistanceMeasure right)
    {
        var converted = Convert(right, left.Unit);
        return new DistanceMeasure(left.Value + converted.Value, left.Unit);
    }

    private static DistanceMeasure Convert(DistanceMeasure value, DistanceUnit unit) =>
        value.Unit == unit ? value : value.ConvertTo(unit);

    private static string DescribeTravel(ResolvedTravelAmount travel) =>
        travel.HexSteps is { } steps
            ? $"{steps} hex step(s)"
            : $"{Format(travel.ActualDistance!.Value.Value)} {travel.ActualDistance.Value.Unit.Symbol}";

    private static string Describe(ResolutionProvenance provenance) =>
        string.IsNullOrWhiteSpace(provenance.Note)
            ? provenance.Source.ToString()
            : $"{provenance.Source} ({provenance.Note.Trim()})";

    private static string NoteSuffix(string? note) =>
        string.IsNullOrWhiteSpace(note) ? "" : $" · {note.Trim()}";

    private static string Format(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatHours(TimeSpan value) =>
        $"{value.TotalHours.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}h";
}
