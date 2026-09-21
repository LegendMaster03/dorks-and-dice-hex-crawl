using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public static partial class CrawlAssistantActions
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

    public static NonSpatialSessionState RecordWatch(
        CrawlProcedureProfile profile,
        NonSpatialSessionState state,
        NonSpatialWatchAssistantInput input)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        profile.Validate();

        if (input.ElapsedTime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Elapsed watch time must be positive.");
        }

        var active = state.ActiveWatch;
        var events = new List<CrawlRuntimeEvent>();
        if (active is null)
        {
            active = new NonSpatialActiveWatchState(
                state.CompletedWatches + 1,
                profile.WatchLength,
                TimeSpan.Zero);
            events.Add(Event(
                state,
                events,
                active.WatchNumber,
                CrawlRuntimeEventKind.WatchStarted,
                state.ElapsedTime,
                null,
                $"Non-spatial watch {active.WatchNumber} started with configured duration {FormatHours(active.TotalDuration)}."));
        }
        else
        {
            active.Validate();
        }

        if (input.ElapsedTime > active.Remaining)
        {
            throw new InvalidOperationException(
                $"Elapsed watch time ({FormatHours(input.ElapsedTime)}) exceeds the remaining {FormatHours(active.Remaining)} in watch {active.WatchNumber}.");
        }

        var elapsedAfter = state.ElapsedTime + input.ElapsedTime;
        var activeElapsed = active.Elapsed + input.ElapsedTime;
        var completed = activeElapsed == active.TotalDuration;

        events.Add(Event(
            state,
            events,
            active.WatchNumber,
            CrawlRuntimeEventKind.WatchTimeAdvanced,
            elapsedAfter,
            null,
            $"Recorded {FormatHours(input.ElapsedTime)} of non-spatial watch time; {Describe(input.Provenance)}{NoteSuffix(input.Note)}."));

        if (completed)
        {
            events.Add(Event(
                state,
                events,
                active.WatchNumber,
                CrawlRuntimeEventKind.WatchCompleted,
                elapsedAfter,
                null,
                $"Non-spatial watch {active.WatchNumber} completed after {FormatHours(active.TotalDuration)}."));
        }

        if (input.Provenance.Source == ResolutionSource.DmOverride)
        {
            events.Add(Event(
                state,
                events,
                active.WatchNumber,
                CrawlRuntimeEventKind.DmOverrideApplied,
                elapsedAfter,
                null,
                string.IsNullOrWhiteSpace(input.Note)
                    ? "DM override applied to non-spatial watch bookkeeping."
                    : $"DM override applied to non-spatial watch bookkeeping: {input.Note.Trim()}"));
        }

        events.Add(ProvenanceEvent(
            state,
            events,
            active.WatchNumber,
            elapsedAfter,
            null,
            $"watch-assistant={Describe(input.Provenance)}"));

        return state with
        {
            ElapsedTime = elapsedAfter,
            CompletedWatches = completed ? state.CompletedWatches + 1 : state.CompletedWatches,
            ActiveWatch = completed
                ? null
                : active with { Elapsed = activeElapsed },
            History = [.. state.History, .. events]
        };
    }


}
