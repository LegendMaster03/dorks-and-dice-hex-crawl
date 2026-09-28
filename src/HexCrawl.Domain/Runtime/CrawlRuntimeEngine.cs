using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
    private const double Epsilon = 0.0000001d;

    /// <summary>
    /// Native generic execution path. The pinned CampaignProcedure is the runtime authority.
    /// </summary>
    public WatchAdvanceResult Advance(
        CrawlRuntimeContext context,
        CampaignProcedure procedure,
        ExpeditionState expedition,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var runtime = GenericProcedureRuntime.Bind(procedure);
        ValidateNativeTravelAmountPolicy(runtime.Movement, inputs.Travel);
        return AdvanceCore(context, runtime, expedition, plan, inputs);
    }

    /// <summary>
    /// Historical compatibility path for profile-only persisted sessions.
    /// </summary>
    public WatchAdvanceResult Advance(
        CrawlRuntimeContext context,
        CrawlProcedureProfile profile,
        ExpeditionState expedition,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return AdvanceCore(context, GenericProcedureRuntime.FromLegacyProfile(profile), expedition, plan, inputs);
    }

    private static WatchAdvanceResult AdvanceCore(
        CrawlRuntimeContext context,
        GenericProcedureRuntime procedure,
        ExpeditionState expedition,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inputs);

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
            (state, active) = StartWatch(procedure, state, plan, inputs, events);
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
            ValidateDoubleBack(procedure.HexProgress, state, plan);
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
            procedure.Movement,
            procedure.HexProgress,
            context.HexCenterDistance,
            state,
            actualDirection,
            plan.DeliberateDoubleBack,
            active.WatchNumber,
            events);

        ValidateTravelAmountShape(procedure.Movement, inputs.Travel);
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

        var movement = procedure.Movement.TravelResolution switch
        {
            TravelResolutionMode.ContinuousDistance => MoveContinuous(
                context,
                procedure.Movement,
                procedure.HexProgress,
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
            _ => throw new ArgumentOutOfRangeException(nameof(procedure.Movement.TravelResolution))
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