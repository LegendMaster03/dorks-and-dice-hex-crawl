using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
    private const double Epsilon = 0.0000001d;

    public WatchAdvanceResult Advance(
        CrawlRuntimeContext context,
        CrawlProcedureProfile profile,
        ExpeditionState expedition,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inputs);

        profile.Validate();
        context.Validate();

        var events = new EventCollector(expedition.History);
        var state = expedition;
        var active = state.ActiveWatch;

        if (active is not null && active.PendingDecision is not null)
        {
            (state, active) = ResumePendingDecision(state, active, inputs, events);
        }

        if (active is null)
        {
            (state, active) = StartWatch(profile, state, plan, inputs, events);
        }

        active = active with { Plan = plan };
        state = state with
        {
            ActiveWatch = active,
            IntendedDirection = plan.IntendedDirection,
            ActualDirection = ResolveActualDirection(state.Navigation, plan.IntendedDirection)
        };

        if (plan.DeliberateDoubleBack)
        {
            ValidateDoubleBack(profile, state, plan);
            state = state with
            {
                Navigation = new NavigationRuntimeState(false, 0),
                ActualDirection = plan.IntendedDirection
            };
        }

        EmitDmOverrideIfNeeded(state, active.WatchNumber, inputs, events);

        if (active.Remaining <= TimeSpan.Zero)
        {
            return CompleteWatch(state, active, events);
        }

        var actualDirection = state.ActualDirection
            ?? throw new InvalidOperationException("Travel requires an actual hex direction.");
        state = ApplyDirectionContext(
            profile,
            context.HexCenterDistance,
            state,
            actualDirection,
            plan.DeliberateDoubleBack,
            active.WatchNumber,
            events);

        ValidateTravelAmount(profile, inputs.Travel);
        EmitTravelResolution(state, active.WatchNumber, inputs.Travel, events);

        if (!active.EncounterHandled && active.Encounter.Kind != EncounterOutcomeKind.None)
        {
            var due = active.Encounter.OccursAt
                ?? throw new InvalidOperationException("A triggered encounter requires a time within the watch.");
            if (due <= active.Elapsed)
            {
                return TriggerEncounter(state, active, events);
            }
        }

        var callRemaining = active.Remaining;
        var segmentDuration = callRemaining;
        var encounterDueAtSegmentEnd = false;
        if (!active.EncounterHandled && active.Encounter.Kind != EncounterOutcomeKind.None)
        {
            var due = active.Encounter.OccursAt!.Value;
            if (due > active.Elapsed && due <= active.TotalDuration)
            {
                var untilEncounter = due - active.Elapsed;
                if (untilEncounter <= segmentDuration)
                {
                    segmentDuration = untilEncounter;
                    encounterDueAtSegmentEnd = true;
                }
            }
        }

        var movement = profile.TravelResolution switch
        {
            TravelResolutionMode.ContinuousDistance => MoveContinuous(
                context,
                profile,
                state,
                active,
                plan,
                inputs.Travel,
                callRemaining,
                segmentDuration,
                events),
            TravelResolutionMode.HexSteps => MoveHexSteps(
                context,
                state,
                active,
                plan,
                inputs.Travel,
                callRemaining,
                segmentDuration,
                events),
            _ => throw new ArgumentOutOfRangeException(nameof(profile.TravelResolution))
        };

        state = movement.State;
        active = movement.Active;
        if (movement.PauseReason is not null)
        {
            return Finish(state, movement.PauseReason, active.Remaining, events);
        }

        if (encounterDueAtSegmentEnd && !active.EncounterHandled && active.Encounter.Kind != EncounterOutcomeKind.None)
        {
            return TriggerEncounter(state, active, events);
        }

        if (active.Remaining <= TimeSpan.Zero)
        {
            return CompleteWatch(state, active, events);
        }

        return Finish(state, null, active.Remaining, events);
    }
}
