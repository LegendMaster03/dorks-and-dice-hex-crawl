using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
    private static DistanceMeasure Add(DistanceMeasure left, DistanceMeasure right)
    {
        var converted = Convert(right, left.Unit);
        return new DistanceMeasure(left.Value + converted.Value, left.Unit);
    }

    private static DistanceMeasure Convert(DistanceMeasure distance, DistanceUnit unit) =>
        distance.Unit == unit ? distance : distance.ConvertTo(unit);

    private static TimeSpan ScaleTime(TimeSpan value, double factor)
    {
        if (factor <= 0d)
        {
            return TimeSpan.Zero;
        }
        if (factor >= 1d)
        {
            return value;
        }
        return TimeSpan.FromTicks((long)Math.Round(value.Ticks * factor, MidpointRounding.AwayFromZero));
    }

    private static TimeSpan ClampTime(TimeSpan value, TimeSpan minimum, TimeSpan maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record MovementOutcome(
        ExpeditionState State,
        ActiveWatchState Active,
        RuntimePauseReason? PauseReason);

    private sealed class EventCollector
    {
        private long _nextSequence;

        public EventCollector(IReadOnlyList<CrawlRuntimeEvent> history)
        {
            _nextSequence = history.Count == 0 ? 1 : history[^1].Sequence + 1;
        }

        public List<CrawlRuntimeEvent> NewEvents { get; } = [];

        public void Add(
            int watchNumber,
            CrawlRuntimeEventKind kind,
            TimeSpan elapsed,
            HexCoordinate hex,
            string message,
            double? distanceValue = null,
            string? distanceUnit = null,
            Guid? subjectId = null,
            KnowledgeSubjectType? subjectType = null)
        {
            NewEvents.Add(new CrawlRuntimeEvent(
                _nextSequence++,
                watchNumber,
                kind,
                elapsed,
                hex,
                message,
                distanceValue,
                distanceUnit,
                subjectId,
                subjectType));
        }
    }
}
