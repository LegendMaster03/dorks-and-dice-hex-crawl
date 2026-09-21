using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
    private static void ValidateTravelAmount(
        CrawlProcedureProfile profile,
        ResolvedTravelAmount travel)
    {
        if (profile.TravelResolution == TravelResolutionMode.ContinuousDistance)
        {
            if (travel.ExpectedDistance is null || travel.ActualDistance is null || travel.HexSteps is not null)
            {
                throw new InvalidOperationException("Continuous-distance travel requires expected and actual distance values only.");
            }
            return;
        }

        if (travel.HexSteps is null || travel.HexSteps < 0 || travel.ExpectedDistance is not null || travel.ActualDistance is not null)
        {
            throw new InvalidOperationException("Hex-step travel requires a non-negative resolved step count only.");
        }
    }

    private static void EmitTravelResolution(
        ExpeditionState state,
        int watchNumber,
        ResolvedTravelAmount travel,
        EventCollector events)
    {
        var message = travel.ActualDistance is { } actual && travel.ExpectedDistance is { } expected
            ? $"Travel resolved at {Format(actual.Value)} {actual.Unit.Symbol} actual versus {Format(expected.Value)} {expected.Unit.Symbol} expected."
            : $"Travel resolved at {travel.HexSteps ?? 0} hex step(s).";
        events.Add(
            watchNumber,
            CrawlRuntimeEventKind.TravelResolved,
            state.ElapsedTravelTime,
            state.CurrentHex,
            message,
            travel.ActualDistance?.Value,
            travel.ActualDistance?.Unit.Symbol);
    }

    private static MovementOutcome MoveContinuous(
        CrawlRuntimeContext context,
        CrawlProcedureProfile profile,
        ExpeditionState state,
        ActiveWatchState active,
        WatchTravelPlan plan,
        ResolvedTravelAmount travel,
        TimeSpan callRemaining,
        TimeSpan segmentDuration,
        EventCollector events)
    {
        if (!profile.TracksIntraHexProgress)
        {
            throw new InvalidOperationException("Continuous-distance travel requires intra-hex progress tracking in this runtime engine.");
        }

        var unit = context.HexCenterDistance.Unit;
        var suppliedDistance = Convert(travel.ActualDistance!.Value, unit);
        var segmentFraction = callRemaining <= TimeSpan.Zero
            ? 0d
            : Math.Clamp(segmentDuration.TotalSeconds / callRemaining.TotalSeconds, 0d, 1d);
        var targetDistance = suppliedDistance.Value * segmentFraction;
        var remainingDistance = targetDistance;
        var consumed = 0d;
        var traversal = state.Traversal;
        var direction = state.ActualDirection!.Value;
        RuntimePauseReason? pause = null;

        while (remainingDistance > Epsilon)
        {
            var requirement = DetermineExitRequirement(profile, context.HexCenterDistance, traversal, direction, plan.DeliberateDoubleBack);
            var progress = Convert(traversal.Progress, unit);
            var needed = plan.DeliberateDoubleBack
                ? progress.Value
                : Math.Max(0d, requirement.Value - progress.Value);

            traversal = traversal with
            {
                LastTravelDirection = direction,
                CurrentExitRequirement = requirement
            };

            if (remainingDistance + Epsilon < needed)
            {
                var nextProgress = plan.DeliberateDoubleBack
                    ? Math.Max(0d, progress.Value - remainingDistance)
                    : progress.Value + remainingDistance;
                consumed += remainingDistance;
                remainingDistance = 0d;
                traversal = traversal with
                {
                    Progress = new DistanceMeasure(nextProgress, unit),
                    CurrentExitRequirement = plan.DeliberateDoubleBack
                        ? new DistanceMeasure(nextProgress, unit)
                        : requirement
                };
                break;
            }

            consumed += needed;
            remainingDistance = Math.Max(0d, remainingDistance - needed);
            var elapsedForCrossing = suppliedDistance.Value <= Epsilon
                ? TimeSpan.Zero
                : ScaleTime(callRemaining, Math.Clamp(consumed / suppliedDistance.Value, 0d, 1d));
            var eventTime = state.ElapsedTravelTime + elapsedForCrossing;
            var exited = traversal.CurrentHex;
            var entered = exited.Neighbor(direction.Value);

            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.HexExited,
                eventTime,
                exited,
                $"Exited hex {exited} toward direction {direction.Value}.");
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.HexEntered,
                eventTime,
                entered,
                $"Entered hex {entered}; keyed contents remain undiscovered until separately encountered.");

            traversal = new HexTraversalState
            {
                CurrentHex = entered,
                EntryDirection = direction,
                LastTravelDirection = direction,
                Progress = new DistanceMeasure(0, unit),
                CurrentExitRequirement = new DistanceMeasure(
                    context.HexCenterDistance.Value * profile.FarExitProgressFactor,
                    unit)
            };
            state = state with { Traversal = traversal };

            if (plan.DeliberateDoubleBack)
            {
                pause = RuntimePauseReason.BacktrackBoundaryReached;
                break;
            }

            if (state.Navigation.IsLost)
            {
                if (plan.NavigationAid.ResetsVeerAtBoundary && state.Navigation.VeerSteps != 0)
                {
                    state = state with { Navigation = new NavigationRuntimeState(true, 0) };
                    events.Add(
                        active.WatchNumber,
                        CrawlRuntimeEventKind.VeerReset,
                        eventTime,
                        entered,
                        $"{plan.NavigationAid.Key} reset veer at the hex boundary; the expedition has not necessarily recognized that it was lost.");
                }

                events.Add(
                    active.WatchNumber,
                    CrawlRuntimeEventKind.NavigationDecisionRequired,
                    eventTime,
                    entered,
                    "The expedition crossed a boundary while lost; recognition/reorientation input is required before continuing.");
                pause = RuntimePauseReason.LostRecognitionRequired;
                break;
            }

            if (!plan.ContinueAcrossBoundaries)
            {
                events.Add(
                    active.WatchNumber,
                    CrawlRuntimeEventKind.ConditionsReviewRequired,
                    eventTime,
                    entered,
                    "Hex boundary crossed; review terrain/travel conditions before continuing the remaining watch.");
                pause = RuntimePauseReason.ConditionsReviewRequired;
                break;
            }

            if (needed <= Epsilon && remainingDistance <= Epsilon)
            {
                break;
            }
        }

        var consumedTime = pause is null
            ? segmentDuration
            : suppliedDistance.Value <= Epsilon
                ? TimeSpan.Zero
                : ScaleTime(callRemaining, Math.Clamp(consumed / suppliedDistance.Value, 0d, 1d));
        consumedTime = ClampTime(consumedTime, TimeSpan.Zero, segmentDuration);
        state = state with
        {
            Traversal = traversal,
            DistanceTraveled = Add(state.DistanceTraveled, new DistanceMeasure(consumed, unit)),
            ElapsedTravelTime = state.ElapsedTravelTime + consumedTime
        };
        active = active with
        {
            Elapsed = ClampTime(active.Elapsed + consumedTime, TimeSpan.Zero, active.TotalDuration),
            PendingDecision = pause
        };
        state = state with { ActiveWatch = active };

        if (consumed > Epsilon)
        {
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.DistanceTraveled,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Traveled {Format(consumed)} {unit.Symbol} during this watch segment.",
                consumed,
                unit.Symbol);
        }

        return new MovementOutcome(state, active, pause);
    }

    private static MovementOutcome MoveHexSteps(
        CrawlRuntimeContext context,
        ExpeditionState state,
        ActiveWatchState active,
        WatchTravelPlan plan,
        ResolvedTravelAmount travel,
        TimeSpan callRemaining,
        TimeSpan segmentDuration,
        EventCollector events)
    {
        var suppliedSteps = travel.HexSteps!.Value;
        var segmentFraction = callRemaining <= TimeSpan.Zero
            ? 0d
            : Math.Clamp(segmentDuration.TotalSeconds / callRemaining.TotalSeconds, 0d, 1d);
        var targetSteps = segmentDuration == callRemaining
            ? suppliedSteps
            : (int)Math.Floor(suppliedSteps * segmentFraction + Epsilon);
        var direction = state.ActualDirection!.Value;
        var completed = 0;
        RuntimePauseReason? pause = null;

        for (var index = 0; index < targetSteps; index++)
        {
            completed++;
            var elapsedForCrossing = suppliedSteps == 0
                ? TimeSpan.Zero
                : ScaleTime(callRemaining, (double)completed / suppliedSteps);
            var eventTime = state.ElapsedTravelTime + elapsedForCrossing;
            var exited = state.CurrentHex;
            var entered = exited.Neighbor(direction.Value);

            events.Add(active.WatchNumber, CrawlRuntimeEventKind.HexExited, eventTime, exited, $"Exited hex {exited}.");
            events.Add(active.WatchNumber, CrawlRuntimeEventKind.HexEntered, eventTime, entered, $"Entered hex {entered}; keyed contents remain undiscovered until separately encountered.");

            state = state with
            {
                Traversal = new HexTraversalState
                {
                    CurrentHex = entered,
                    EntryDirection = direction,
                    LastTravelDirection = direction,
                    Progress = new DistanceMeasure(0, context.HexCenterDistance.Unit)
                }
            };

            if (state.Navigation.IsLost)
            {
                if (plan.NavigationAid.ResetsVeerAtBoundary && state.Navigation.VeerSteps != 0)
                {
                    state = state with { Navigation = new NavigationRuntimeState(true, 0) };
                    events.Add(active.WatchNumber, CrawlRuntimeEventKind.VeerReset, eventTime, entered, $"{plan.NavigationAid.Key} reset veer at the boundary.");
                }
                events.Add(active.WatchNumber, CrawlRuntimeEventKind.NavigationDecisionRequired, eventTime, entered, "Lost-recognition input is required before continuing.");
                pause = RuntimePauseReason.LostRecognitionRequired;
                break;
            }

            if (!plan.ContinueAcrossBoundaries)
            {
                events.Add(active.WatchNumber, CrawlRuntimeEventKind.ConditionsReviewRequired, eventTime, entered, "Review travel conditions before continuing.");
                pause = RuntimePauseReason.ConditionsReviewRequired;
                break;
            }
        }

        var consumedTime = pause is null
            ? segmentDuration
            : suppliedSteps == 0
                ? TimeSpan.Zero
                : ScaleTime(callRemaining, (double)completed / suppliedSteps);
        consumedTime = ClampTime(consumedTime, TimeSpan.Zero, segmentDuration);
        var physicalDistance = new DistanceMeasure(
            context.HexCenterDistance.Value * completed,
            context.HexCenterDistance.Unit);
        state = state with
        {
            DistanceTraveled = Add(state.DistanceTraveled, physicalDistance),
            ElapsedTravelTime = state.ElapsedTravelTime + consumedTime
        };
        active = active with
        {
            Elapsed = ClampTime(active.Elapsed + consumedTime, TimeSpan.Zero, active.TotalDuration),
            PendingDecision = pause
        };
        state = state with { ActiveWatch = active };

        if (completed > 0)
        {
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.DistanceTraveled,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Completed {completed} hex step(s), equivalent to {Format(physicalDistance.Value)} {physicalDistance.Unit.Symbol} at this grid scale.",
                physicalDistance.Value,
                physicalDistance.Unit.Symbol);
        }

        return new MovementOutcome(state, active, pause);
    }


}
