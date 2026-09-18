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

public static class CrawlAssistantActions
{
    public static ExpeditionState RecordTravelWatch(
        CrawlRuntimeContext context,
        CrawlProcedureProfile profile,
        ExpeditionState state,
        TravelWatchAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        context.Validate();
        profile.Validate();
        RequireStandaloneState(state);

        if (input.ElapsedTime < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Elapsed travel time can not be negative.");
        }

        var physicalDistance = ResolvePhysicalDistance(context, profile, input.Travel);
        var progress = input.HexProgress is null
            ? state.Traversal.Progress
            : Convert(input.HexProgress.Value, context.HexCenterDistance.Unit);
        if (progress.Value < 0)
        {
            throw new InvalidOperationException("Hex progress can not be negative.");
        }

        var intended = input.IntendedDirection ?? state.IntendedDirection;
        var actual = input.ActualDirection
            ?? (intended is null
                ? state.ActualDirection
                : state.Navigation.IsLost
                    ? intended.Value.Rotate(state.Navigation.VeerSteps)
                    : intended);

        var changedHex = input.ResultingHex != state.CurrentHex;
        var traversal = state.Traversal with
        {
            CurrentHex = input.ResultingHex,
            EntryDirection = changedHex && actual is not null ? actual : state.Traversal.EntryDirection,
            LastTravelDirection = actual ?? state.Traversal.LastTravelDirection,
            Progress = progress,
            CurrentExitRequirement = null
        };

        var events = new List<CrawlRuntimeEvent>();
        var watchNumber = state.CompletedWatches + 1;
        var elapsedAfter = state.ElapsedTravelTime + input.ElapsedTime;
        if (changedHex)
        {
            events.Add(Event(state, events, watchNumber, CrawlRuntimeEventKind.HexExited, state.ElapsedTravelTime, state.CurrentHex,
                $"Manual travel bookkeeping exited hex {state.CurrentHex}."));
            events.Add(Event(state, events, watchNumber, CrawlRuntimeEventKind.HexEntered, elapsedAfter, input.ResultingHex,
                $"Manual travel bookkeeping entered hex {input.ResultingHex}."));
        }

        events.Add(Event(
            state,
            events,
            watchNumber,
            CrawlRuntimeEventKind.TravelResolved,
            elapsedAfter,
            input.ResultingHex,
            $"Travel/watch assistant recorded {DescribeTravel(input.Travel)} from {Describe(input.Travel.Provenance)}{NoteSuffix(input.Note)}.",
            physicalDistance.Value,
            physicalDistance.Unit.Symbol));
        if (physicalDistance.Value > 0)
        {
            events.Add(Event(
                state,
                events,
                watchNumber,
                CrawlRuntimeEventKind.DistanceTraveled,
                elapsedAfter,
                input.ResultingHex,
                $"Recorded {Format(physicalDistance.Value)} {physicalDistance.Unit.Symbol} of travel.",
                physicalDistance.Value,
                physicalDistance.Unit.Symbol));
        }
        if (input.CompleteWatch)
        {
            events.Add(Event(
                state,
                events,
                watchNumber,
                CrawlRuntimeEventKind.WatchCompleted,
                elapsedAfter,
                input.ResultingHex,
                $"Travel/watch assistant marked watch {watchNumber} complete."));
        }
        events.Add(ProvenanceEvent(
            state,
            events,
            watchNumber,
            elapsedAfter,
            input.ResultingHex,
            $"travel-assistant={Describe(input.Travel.Provenance)}"));

        return state with
        {
            Traversal = traversal,
            IntendedDirection = intended,
            ActualDirection = actual,
            DistanceTraveled = Add(state.DistanceTraveled, physicalDistance),
            ElapsedTravelTime = elapsedAfter,
            CompletedWatches = input.CompleteWatch ? state.CompletedWatches + 1 : state.CompletedWatches,
            History = [.. state.History, .. events]
        };
    }

    public static ExpeditionState RecordNavigation(
        ExpeditionState state,
        NavigationAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        RequireStandaloneState(state);

        if (input.IsLost && input.VeerSteps == 0)
        {
            throw new InvalidOperationException("A lost navigation state requires a non-zero veer.");
        }
        if (!input.IsLost && input.VeerSteps != 0)
        {
            throw new InvalidOperationException("An oriented navigation state must use zero veer.");
        }

        var previous = state.Navigation;
        var next = new NavigationRuntimeState(input.IsLost, input.VeerSteps);
        var intended = input.IntendedDirection ?? state.IntendedDirection;
        var actual = intended is null
            ? state.ActualDirection
            : next.IsLost ? intended.Value.Rotate(next.VeerSteps) : intended;

        var events = new List<CrawlRuntimeEvent>();
        var watchNumber = Math.Max(1, state.CompletedWatches + 1);
        events.Add(Event(
            state,
            events,
            watchNumber,
            CrawlRuntimeEventKind.NavigationCheckResolved,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Navigation assistant recorded {(next.IsLost ? $"lost with veer {next.VeerSteps}" : "oriented")}; {Describe(input.Provenance)}{NoteSuffix(input.Note)}."));
        if (!previous.IsLost && next.IsLost)
        {
            events.Add(Event(state, events, watchNumber, CrawlRuntimeEventKind.ExpeditionBecameLost,
                state.ElapsedTravelTime, state.CurrentHex, $"The expedition became lost with a veer of {next.VeerSteps} hex step(s)."));
        }
        if (previous.IsLost && !next.IsLost)
        {
            events.Add(Event(state, events, watchNumber, CrawlRuntimeEventKind.ExpeditionReoriented,
                state.ElapsedTravelTime, state.CurrentHex, "The navigation assistant recorded that the expedition reoriented."));
        }
        if (previous.VeerSteps != next.VeerSteps)
        {
            events.Add(Event(state, events, watchNumber, CrawlRuntimeEventKind.VeerChanged,
                state.ElapsedTravelTime, state.CurrentHex, $"Veer changed from {previous.VeerSteps} to {next.VeerSteps} hex step(s)."));
        }
        events.Add(ProvenanceEvent(
            state,
            events,
            watchNumber,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"navigation-assistant={Describe(input.Provenance)}"));

        return state with
        {
            Navigation = next,
            IntendedDirection = intended,
            ActualDirection = actual,
            History = [.. state.History, .. events]
        };
    }

    public static ExpeditionState RecordEncounterCadence(
        ExpeditionState state,
        EncounterCadenceAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        RequireStandaloneState(state);
        if (input.Outcome == EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            throw new InvalidOperationException("Keyed-location discovery belongs to world/map composition. Use the full crawl workbench or record a manual/custom encounter.");
        }

        var events = new List<CrawlRuntimeEvent>();
        var watchNumber = Math.Max(1, state.CompletedWatches + 1);
        events.Add(Event(
            state,
            events,
            watchNumber,
            CrawlRuntimeEventKind.EncounterCheckPerformed,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Encounter cadence assistant recorded {input.Outcome}; {Describe(input.Provenance)}{NoteSuffix(input.Note)}."));
        if (input.Outcome != EncounterOutcomeKind.None)
        {
            events.Add(Event(
                state,
                events,
                watchNumber,
                CrawlRuntimeEventKind.EncounterTriggered,
                state.ElapsedTravelTime,
                state.CurrentHex,
                string.IsNullOrWhiteSpace(input.Note)
                    ? $"{input.Outcome} triggered."
                    : $"{input.Outcome}: {input.Note.Trim()}"));
        }
        events.Add(ProvenanceEvent(
            state,
            events,
            watchNumber,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"encounter-assistant={Describe(input.Provenance)}"));

        return state with { History = [.. state.History, .. events] };
    }

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
        ExpeditionState state,
        IReadOnlyCollection<CrawlRuntimeEvent> pending,
        int watchNumber,
        CrawlRuntimeEventKind kind,
        TimeSpan elapsed,
        HexCoordinate hex,
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
        ExpeditionState state,
        IReadOnlyCollection<CrawlRuntimeEvent> pending,
        int watchNumber,
        TimeSpan elapsed,
        HexCoordinate hex,
        string message) =>
        Event(state, pending, watchNumber, CrawlRuntimeEventKind.ResolutionProvenanceRecorded, elapsed, hex, $"Resolved input provenance: {message}.");

    private static long NextSequence(ExpeditionState state, IReadOnlyCollection<CrawlRuntimeEvent> pending) =>
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
}
