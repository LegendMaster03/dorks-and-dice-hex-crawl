namespace HexCrawl.Domain.Runtime;

public static partial class CrawlAssistantActions
{
    public static NonSpatialSessionState RecordWatch(
        GenericProcedureRuntime procedure,
        NonSpatialSessionState state,
        NonSpatialWatchAssistantInput input,
        IReadOnlyList<ParticipantActivityAssignment>? activityAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        return RecordWatch(
            procedure.Time.IntervalDuration,
            state,
            input,
            activityAssignments);
    }

    public static NonSpatialSessionState RecordWatch(
        TimeSpan intervalDuration,
        NonSpatialSessionState state,
        NonSpatialWatchAssistantInput input,
        IReadOnlyList<ParticipantActivityAssignment>? activityAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);

        if (intervalDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Configured watch duration must be positive.");
        }
        if (input.ElapsedTime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Elapsed watch time must be positive.");
        }

        var active = state.ActiveWatch;
        var events = new List<CrawlRuntimeEvent>();
        if (active is null)
        {
            var snapshot = (activityAssignments ?? [])
                .Select(value => value with { })
                .ToArray();
            foreach (var assignment in snapshot)
            {
                assignment.ValidateStructure();
            }
            active = new NonSpatialActiveWatchState(
                state.CompletedWatches + 1,
                intervalDuration,
                TimeSpan.Zero)
            {
                ActivityAssignments = snapshot
            };
            events.Add(Event(
                state,
                events,
                active.WatchNumber,
                CrawlRuntimeEventKind.WatchStarted,
                state.ElapsedTime,
                $"Non-spatial watch {active.WatchNumber} started with configured duration {FormatHours(active.TotalDuration)} and {snapshot.Length} participant assignment(s)."));
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
            $"Recorded {FormatHours(input.ElapsedTime)} of non-spatial watch time; {Describe(input.Provenance)}{NoteSuffix(input.Note)}."));

        if (completed)
        {
            events.Add(Event(
                state,
                events,
                active.WatchNumber,
                CrawlRuntimeEventKind.WatchCompleted,
                elapsedAfter,
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
                string.IsNullOrWhiteSpace(input.Note)
                    ? "DM override applied to non-spatial watch bookkeeping."
                    : $"DM override applied to non-spatial watch bookkeeping: {input.Note.Trim()}"));
        }

        events.Add(ProvenanceEvent(
            state,
            events,
            active.WatchNumber,
            elapsedAfter,
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
