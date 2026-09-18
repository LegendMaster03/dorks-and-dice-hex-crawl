using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed class CrawlRuntimeEngine
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

    private static (ExpeditionState State, ActiveWatchState Active) StartWatch(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        var encounter = ResolveEncounterForNewWatch(profile, inputs.Encounter);
        var active = new ActiveWatchState(
            state.CompletedWatches + 1,
            profile.WatchLength,
            TimeSpan.Zero,
            plan,
            encounter,
            encounter.Kind == EncounterOutcomeKind.None,
            null);
        var activities = plan.Mode.Activities.Count == 0
            ? "none"
            : string.Join(", ", plan.Mode.Activities);

        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.WatchStarted,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Watch {active.WatchNumber} started ({profile.Name}); pace {plan.Mode.PaceKey}; activities {activities}.");

        state = state with { ActiveWatch = active };
        state = ResolveNavigationAtWatchStart(
            profile,
            state,
            plan,
            inputs.Navigation,
            events,
            active.WatchNumber);

        if (profile.EncounterCadence != EncounterCheckCadence.None)
        {
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.EncounterCheckPerformed,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Encounter check resolved as {encounter.Kind}.");
        }

        return (state, active);
    }

    private static (ExpeditionState State, ActiveWatchState Active) ResumePendingDecision(
        ExpeditionState state,
        ActiveWatchState active,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        if (active.PendingDecision == RuntimePauseReason.LostRecognitionRequired)
        {
            var decision = inputs.BoundaryDecision
                ?? throw new InvalidOperationException("A lost-recognition boundary decision is required before travel can continue.");
            if (decision.RecognizedLost && decision.Reorient)
            {
                state = state with { Navigation = new NavigationRuntimeState(false, 0) };
                events.Add(
                    active.WatchNumber,
                    CrawlRuntimeEventKind.ExpeditionReoriented,
                    state.ElapsedTravelTime,
                    state.CurrentHex,
                    "The expedition recognized that it was lost and reoriented.");
            }
        }

        active = active with { PendingDecision = null };
        state = state with { ActiveWatch = active };
        return (state, active);
    }

    private static ResolvedEncounter ResolveEncounterForNewWatch(
        CrawlProcedureProfile profile,
        ResolvedEncounter? supplied)
    {
        if (profile.EncounterCadence == EncounterCheckCadence.None)
        {
            return ResolvedEncounter.None;
        }

        var encounter = supplied
            ?? throw new InvalidOperationException("This procedure requires an explicit resolved encounter-check outcome.");
        if (encounter.Kind == EncounterOutcomeKind.None)
        {
            return encounter;
        }

        if (encounter.OccursAt is null || encounter.OccursAt < TimeSpan.Zero || encounter.OccursAt > profile.WatchLength)
        {
            throw new InvalidOperationException("Triggered encounter time must fall within the watch.");
        }

        if (encounter.Kind == EncounterOutcomeKind.KeyedLocationDiscovery && encounter.LocationId is null)
        {
            throw new InvalidOperationException("A keyed-location encounter requires a location id.");
        }

        return encounter;
    }

    private static ExpeditionState ResolveNavigationAtWatchStart(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        WatchTravelPlan plan,
        ResolvedNavigation? resolved,
        EventCollector events,
        int watchNumber)
    {
        var checkRequired = profile.UsesNavigationChecks
            && !plan.NavigationAid.SuppressesNavigationCheck
            && !plan.DeliberateDoubleBack;
        if (!checkRequired)
        {
            state = state with { Navigation = new NavigationRuntimeState(false, 0) };
            events.Add(
                watchNumber,
                CrawlRuntimeEventKind.NavigationCheckResolved,
                state.ElapsedTravelTime,
                state.CurrentHex,
                "Navigation check was not required for this watch.");
            return state;
        }

        var navigation = resolved
            ?? throw new InvalidOperationException("This watch requires an explicit resolved navigation outcome.");
        if (navigation.Outcome == NavigationCheckOutcome.NotRequired)
        {
            throw new InvalidOperationException("A required navigation check can not be resolved as NotRequired.");
        }

        var previous = state.Navigation;
        var next = previous;
        if (navigation.Outcome == NavigationCheckOutcome.Succeeded)
        {
            if (!previous.IsLost)
            {
                next = new NavigationRuntimeState(false, 0);
            }
        }
        else
        {
            var candidate = navigation.VeerStepsOnFailure
                ?? throw new InvalidOperationException("A failed navigation check requires a resolved veer.");
            if (candidate == 0)
            {
                throw new InvalidOperationException("A failed navigation check must produce a non-zero veer.");
            }

            if (!previous.IsLost)
            {
                next = new NavigationRuntimeState(true, candidate);
            }
            else if (!profile.UsesPersistentVeer || Math.Abs(candidate) > Math.Abs(previous.VeerSteps))
            {
                next = new NavigationRuntimeState(true, candidate);
            }
        }

        events.Add(
            watchNumber,
            CrawlRuntimeEventKind.NavigationCheckResolved,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Navigation check {navigation.Outcome.ToString().ToLowerInvariant()}.");
        if (!previous.IsLost && next.IsLost)
        {
            events.Add(
                watchNumber,
                CrawlRuntimeEventKind.ExpeditionBecameLost,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"The expedition became lost with a veer of {next.VeerSteps} hex step(s).");
        }
        if (previous.VeerSteps != next.VeerSteps)
        {
            events.Add(
                watchNumber,
                CrawlRuntimeEventKind.VeerChanged,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Veer changed from {previous.VeerSteps} to {next.VeerSteps} hex step(s).");
        }

        return state with { Navigation = next };
    }

    private static HexDirection ResolveActualDirection(
        NavigationRuntimeState navigation,
        HexDirection intended) =>
        navigation.IsLost ? intended.Rotate(navigation.VeerSteps) : intended;

    private static void ValidateDoubleBack(
        CrawlProcedureProfile profile,
        ExpeditionState state,
        WatchTravelPlan plan)
    {
        if (!profile.SupportsDeliberateDoubleBack)
        {
            throw new InvalidOperationException("The active procedure does not enable deliberate double-back handling.");
        }

        var entryDirection = state.Traversal.EntryDirection
            ?? throw new InvalidOperationException("The expedition can only deliberately double back after entering through a known hex face.");
        if (plan.IntendedDirection != entryDirection.Opposite)
        {
            throw new InvalidOperationException("A deliberate double back must target the face through which the expedition entered.");
        }
    }

    private static ExpeditionState ApplyDirectionContext(
        CrawlProcedureProfile profile,
        DistanceMeasure hexCenterDistance,
        ExpeditionState state,
        HexDirection actualDirection,
        bool deliberateDoubleBack,
        int watchNumber,
        EventCollector events)
    {
        var traversal = state.Traversal;
        var changed = traversal.LastTravelDirection is { } previous && previous != actualDirection;
        var progress = Convert(traversal.Progress, hexCenterDistance.Unit);

        if (changed && profile.DirectionChangesCostProgress && !deliberateDoubleBack && profile.TracksIntraHexProgress)
        {
            var cost = hexCenterDistance.Value * profile.DirectionChangeProgressCostFactor;
            progress = new DistanceMeasure(Math.Max(0d, progress.Value - cost), progress.Unit);
        }

        var updated = traversal with
        {
            Progress = progress,
            LastTravelDirection = actualDirection
        };
        updated = updated with
        {
            CurrentExitRequirement = profile.TracksIntraHexProgress
                ? DetermineExitRequirement(profile, hexCenterDistance, updated, actualDirection, deliberateDoubleBack)
                : null
        };

        if (changed)
        {
            events.Add(
                watchNumber,
                CrawlRuntimeEventKind.DirectionChanged,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Travel direction changed; abstract progress is now {Format(progress.Value)} {progress.Unit.Symbol}.");
        }

        return state with { Traversal = updated };
    }

    private static DistanceMeasure DetermineExitRequirement(
        CrawlProcedureProfile profile,
        DistanceMeasure hexCenterDistance,
        HexTraversalState traversal,
        HexDirection direction,
        bool deliberateDoubleBack)
    {
        var unit = hexCenterDistance.Unit;
        var progress = Convert(traversal.Progress, unit);
        if (deliberateDoubleBack)
        {
            return new DistanceMeasure(progress.Value, unit);
        }

        var factor = profile.StartingExitProgressFactor;
        if (traversal.EntryDirection is { } entry)
        {
            factor = direction.SeparationFrom(entry) switch
            {
                0 or 1 => profile.FarExitProgressFactor,
                2 => profile.NearExitProgressFactor,
                3 => profile.BackExitProgressFactor,
                _ => throw new InvalidOperationException("Unexpected hex direction separation.")
            };
        }

        return new DistanceMeasure(hexCenterDistance.Value * factor, unit);
    }

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

    private static WatchAdvanceResult TriggerEncounter(
        ExpeditionState state,
        ActiveWatchState active,
        EventCollector events)
    {
        var encounter = active.Encounter;
        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.EncounterTriggered,
            state.ElapsedTravelTime,
            state.CurrentHex,
            string.IsNullOrWhiteSpace(encounter.Note)
                ? $"{encounter.Kind} triggered."
                : $"{encounter.Kind}: {encounter.Note}",
            subjectId: encounter.LocationId);

        active = active with
        {
            EncounterHandled = true,
            PendingDecision = RuntimePauseReason.EncounterTriggered
        };
        state = state with { ActiveWatch = active };
        return Finish(state, RuntimePauseReason.EncounterTriggered, active.Remaining, events);
    }

    private static WatchAdvanceResult CompleteWatch(
        ExpeditionState state,
        ActiveWatchState active,
        EventCollector events)
    {
        state = state with
        {
            CompletedWatches = Math.Max(state.CompletedWatches, active.WatchNumber),
            ActiveWatch = null
        };
        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.WatchCompleted,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Watch {active.WatchNumber} completed.");
        return Finish(state, null, TimeSpan.Zero, events);
    }

    private static void EmitDmOverrideIfNeeded(
        ExpeditionState state,
        int watchNumber,
        WatchAdvanceInputs inputs,
        EventCollector events)
    {
        var overrideApplied = inputs.Travel.Provenance.Source == ResolutionSource.DmOverride
            || inputs.Navigation?.Provenance.Source == ResolutionSource.DmOverride
            || inputs.Encounter?.Provenance.Source == ResolutionSource.DmOverride
            || inputs.BoundaryDecision?.Provenance.Source == ResolutionSource.DmOverride
            || !string.IsNullOrWhiteSpace(inputs.DmOverrideNote);
        if (!overrideApplied)
        {
            return;
        }

        var note = inputs.DmOverrideNote
            ?? inputs.Travel.Provenance.Note
            ?? inputs.Navigation?.Provenance.Note
            ?? inputs.Encounter?.Provenance.Note
            ?? inputs.BoundaryDecision?.Provenance.Note
            ?? "DM supplied an authoritative override.";
        events.Add(
            watchNumber,
            CrawlRuntimeEventKind.DmOverrideApplied,
            state.ElapsedTravelTime,
            state.CurrentHex,
            note);
    }

    private static WatchAdvanceResult Finish(
        ExpeditionState state,
        RuntimePauseReason? pause,
        TimeSpan remaining,
        EventCollector events)
    {
        var newEvents = events.NewEvents.ToArray();
        if (newEvents.Length > 0)
        {
            state = state with { History = state.History.Concat(newEvents).ToArray() };
        }
        return new WatchAdvanceResult(state, pause, remaining, newEvents);
    }

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
