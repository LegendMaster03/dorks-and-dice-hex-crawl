using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public static partial class CrawlAssistantActions
{
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


}
