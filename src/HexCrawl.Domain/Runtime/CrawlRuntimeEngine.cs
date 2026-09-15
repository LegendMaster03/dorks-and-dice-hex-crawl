using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;

namespace HexCrawl.Domain.Runtime;

public sealed class CrawlRuntimeEngine
{
    private const double Epsilon = 0.0000001d;

    public WatchAdvanceResult Advance(
        OverworldDefinition world,
        CrawlProcedureProfile profile,
        ExpeditionState expedition,
        PlayerKnowledgeState knowledge,
        WatchTravelPlan plan,
        WatchAdvanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(expedition);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inputs);

        profile.Validate();
        world.Grid.Validate();

        if (expedition.OverworldId != world.Id || knowledge.OverworldId != world.Id)
        {
            throw new InvalidOperationException("Runtime state and player knowledge must belong to the supplied overworld.");
        }

        if (expedition.Traversal.CurrentHex != expedition.CurrentHex)
        {
            throw new InvalidOperationException("Expedition traversal and current hex are inconsistent.");
        }

        var events = new EventCollector(expedition.History);
        var state = expedition;
        var currentKnowledge = knowledge;
        var active = state.ActiveWatch;
        var startingNewWatch = active is null;

        if (active is not null)
        {
            (state, active) = ResolvePendingDecision(state, active, inputs, events);
        }

        if (startingNewWatch)
        {
            var encounter = ResolveEncounterForNewWatch(profile, inputs.Encounter);
            active = new ActiveWatchState(
                state.CompletedWatches + 1,
                profile.WatchLength,
                TimeSpan.Zero,
                encounter,
                encounter.Kind == EncounterOutcomeKind.None,
                null);

            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.WatchStarted,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Watch {active.WatchNumber} started ({profile.Name}).");

            state = state with { ActiveWatch = active };
            state = ResolveNavigationAtWatchStart(profile, state, plan, inputs.Navigation, events, active.WatchNumber);

            if (profile.EncounterCadence != EncounterCheckCadence.None)
            {
                events.Add(
                    active.WatchNumber,
                    CrawlRuntimeEventKind.EncounterCheckPerformed,
                    state.ElapsedTravelTime,
                    state.CurrentHex,
                    $"Encounter check resolved as {encounter.Kind}.");
            }
        }

        active ??= throw new InvalidOperationException("An active watch is required after initialization.");

        state = state with
        {
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
            return CompleteWatch(state, currentKnowledge, active, events);
        }

        var actualDirection = state.ActualDirection
            ?? throw new InvalidOperationException("Travel requires an actual hex direction.");

        state = ApplyDirectionChange(profile, world.Grid, state, actualDirection, plan.DeliberateDoubleBack, active.WatchNumber, events);
        ValidateTravelAmount(profile, inputs.Travel);
        EmitTravelResolution(state, active.WatchNumber, inputs.Travel, events);

        if (!active.EncounterHandled && active.Encounter.Kind != EncounterOutcomeKind.None)
        {
            var dueAt = active.Encounter.OccursAt
                ?? throw new InvalidOperationException("A triggered encounter requires a time within the watch.");

            if (dueAt <= active.Elapsed)
            {
                return TriggerEncounter(world, state, currentKnowledge, active, events);
            }
        }

        var remainingAtCallStart = active.Remaining;
        var targetDuration = remainingAtCallStart;
        var encounterDueThisSegment = false;

        if (!active.EncounterHandled && active.Encounter.Kind != EncounterOutcomeKind.None)
        {
            var dueAt = active.Encounter.OccursAt!.Value;
            if (dueAt < active.TotalDuration && dueAt > active.Elapsed)
            {
                targetDuration = dueAt - active.Elapsed;
                encounterDueThisSegment = true;
            }
            else if (dueAt == active.TotalDuration)
            {
                encounterDueThisSegment = true;
            }
        }

        WatchAdvanceResult movementResult = profile.TravelResolution switch
        {
            TravelResolutionMode.ContinuousDistance => AdvanceDistance(
                world,
                profile,
                state,
                currentKnowledge,
                plan,
                inputs.Travel,
                active,
                remainingAtCallStart,
                targetDuration,
                events),
            TravelResolutionMode.HexSteps => AdvanceHexSteps(
                world,
                state,
                currentKnowledge,
                plan,
                inputs.Travel,
                active,
                remainingAtCallStart,
                targetDuration,
                events),
            _ => throw new ArgumentOutOfRangeException(nameof(profile.TravelResolution))
        };

        if (movementResult.PauseReason is not null)
        {
            return movementResult;
        }

        state = movementResult.Expedition;
        currentKnowledge = movementResult.Knowledge;
        active = state.ActiveWatch ?? active;

        if (encounterDueThisSegment && !active.EncounterHandled && active.Encounter.Kind != EncounterOutcomeKind.None)
        {
            return TriggerEncounter(world, state, currentKnowledge, active, events);
        }

        if (active.Remaining <= TimeSpan.Zero)
        {
            return CompleteWatch(state, currentKnowledge, active, events);
        }

        return Finish(state, currentKnowledge, null, active.Remaining, events);
    }

    private static (ExpeditionState State, ActiveWatchState Active) ResolvePendingDecision(
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

            active = active with { PendingDecision = null };
            state = state with { ActiveWatch = active };
            return (state, active);
        }

        if (active.PendingDecision is not null)
        {
            active = active with { PendingDecision = null };
            state = state with { ActiveWatch = active };
        }

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
        var navigationRequired = profile.UsesNavigationChecks
            && !plan.NavigationAid.SuppressesNavigationCheck
            && !plan.DeliberateDoubleBack;

        if (!navigationRequired)
        {
            if (!profile.UsesNavigationChecks || plan.NavigationAid.SuppressesNavigationCheck || plan.DeliberateDoubleBack)
            {
                state = state with { Navigation = new NavigationRuntimeState(false, 0) };
            }

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
            else if (profile.UsesPersistentVeer && Math.Abs(candidate) <= Math.Abs(previous.VeerSteps))
            {
                next = previous;
            }
            else
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

    private static HexDirection ResolveActualDirection(NavigationRuntimeState navigation, HexDirection intended) =>
        navigation.IsLost ? intended.Rotate(navigation.VeerSteps) : intended;

    private static void ValidateDoubleBack(CrawlProcedureProfile profile, ExpeditionState state, WatchTravelPlan plan)
    {
        if (!profile.SupportsDeliberateDoubleBack)
        {
            throw new InvalidOperationException("The active procedure does not enable deliberate double-back handling.");
        }

        var entryDirection = state.Traversal.EntryDirection
            ?? throw new InvalidOperationException("The expedition can only deliberately double back after entering through a known hex face.");

        if (plan.IntendedDirection != entryDirection.Value.Opposite)
        {
            throw new InvalidOperationException("A deliberate double back must target the face through which the expedition entered.");
        }
    }

    private static ExpeditionState ApplyDirectionChange(
        CrawlProcedureProfile profile,
        HexGridDefinition grid,
        ExpeditionState state,
        HexDirection actualDirection,
        bool deliberateDoubleBack,
        int watchNumber,
        EventCollector events)
    {
        var traversal = state.Traversal;
        if (traversal.LastTravelDirection is not { } previous || previous == actualDirection)
        {
            return state with
            {
                Traversal = traversal with
                {
                    CurrentExitRequirement = DetermineExitRequirement(profile, grid, traversal, actualDirection, deliberateDoubleBack)
                }
            };
        }

        var progress = Convert(traversal.Progress, grid.NeighborCenterDistance.Unit);
        if (profile.DirectionChangesCostProgress && !deliberateDoubleBack && profile.TracksIntraHexProgress)
        {
            var cost = grid.NeighborCenterDistance.Value * profile.DirectionChangeProgressCostFactor;
            progress = new DistanceMeasure(Math.Max(0, progress.Value - cost), grid.NeighborCenterDistance.Unit);
        }

        var changedTraversal = traversal with
        {
            Progress = progress,
            LastTravelDirection = actualDirection
        };
        changedTraversal = changedTraversal with
        {
            CurrentExitRequirement = DetermineExitRequirement(profile, grid, changedTraversal, actualDirection, deliberateDoubleBack)
        };

        events.Add(
            watchNumber,
            CrawlRuntimeEventKind.DirectionChanged,
            state.ElapsedTravelTime,
            state.CurrentHex,
            $"Travel direction changed from {previous.Value} to {actualDirection.Value}; abstract progress is now {Format(progress.Value)} {progress.Unit.Symbol}.");

        return state with { Traversal = changedTraversal };
    }

    private static DistanceMeasure DetermineExitRequirement(
        CrawlProcedureProfile profile,
        HexGridDefinition grid,
        HexTraversalState traversal,
        HexDirection direction,
        bool deliberateDoubleBack)
    {
        var unit = grid.NeighborCenterDistance.Unit;
        var progress = Convert(traversal.Progress, unit);

        if (deliberateDoubleBack)
        {
            return new DistanceMeasure(progress.Value, unit);
        }

        double factor;
        if (traversal.EntryDirection is null)
        {
            factor = profile.StartingExitProgressFactor;
        }
        else
        {
            var separation = direction.SeparationFrom(traversal.EntryDirection.Value);
            factor = separation switch
            {
                0 or 1 => profile.FarExitProgressFactor,
                2 => profile.NearExitProgressFactor,
                3 => profile.BackExitProgressFactor,
                _ => throw new InvalidOperationException("Unexpected hex direction separation.")
            };
        }

        return new DistanceMeasure(grid.NeighborCenterDistance.Value * factor, unit);
    }

    private static void ValidateTravelAmount(CrawlProcedureProfile profile, ResolvedTravelAmount travel)
    {
        if (profile.TravelResolution == TravelResolutionMode.ContinuousDistance)
        {
            if (travel.ExpectedDistance is null || travel.ActualDistance is null || travel.HexSteps is not null)
            {
                throw new InvalidOperationException("Continuous-distance travel requires expected and actual distance values only.");
            }
        }
        else if (travel.HexSteps is null || travel.HexSteps < 0 || travel.ExpectedDistance is not null || travel.ActualDistance is not null)
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

    private static WatchAdvanceResult AdvanceDistance(
        OverworldDefinition world,
        CrawlProcedureProfile profile,
        ExpeditionState state,
        PlayerKnowledgeState knowledge,
        WatchTravelPlan plan,
        ResolvedTravelAmount travel,
        ActiveWatchState active,
        TimeSpan remainingAtCallStart,
        TimeSpan targetDuration,
        EventCollector events)
    {
        var gridUnit = world.Grid.NeighborCenterDistance.Unit;
        var fullDistance = Convert(travel.ActualDistance!.Value, gridUnit);
        var targetFraction = remainingAtCallStart <= TimeSpan.Zero
            ? 0d
            : targetDuration.TotalSeconds / remainingAtCallStart.TotalSeconds;
        var targetDistance = fullDistance.Value * Math.Clamp(targetFraction, 0d, 1d);
        var consumed = 0d;
        var traversal = state.Traversal;
        var actualDirection = state.ActualDirection!.Value;
        RuntimePauseReason? pause = null;

        while (consumed + Epsilon < targetDistance || IsImmediateExit(profile, world.Grid, traversal, actualDirection, plan.DeliberateDoubleBack))
        {
            if (!profile.TracksIntraHexProgress)
            {
                throw new InvalidOperationException("Continuous-distance travel requires intra-hex progress tracking in this runtime engine.");
            }

            var requirement = DetermineExitRequirement(profile, world.Grid, traversal, actualDirection, plan.DeliberateDoubleBack);
            var progress = Convert(traversal.Progress, gridUnit);
            traversal = traversal with { CurrentExitRequirement = requirement, LastTravelDirection = actualDirection };

            var distanceNeeded = plan.DeliberateDoubleBack
                ? progress.Value
                : Math.Max(0d, requirement.Value - progress.Value);
            var available = Math.Max(0d, targetDistance - consumed);

            if (available + Epsilon < distanceNeeded)
            {
                var nextProgressValue = plan.DeliberateDoubleBack
                    ? Math.Max(0d, progress.Value - available)
                    : progress.Value + available;
                traversal = traversal with
                {
                    Progress = new DistanceMeasure(nextProgressValue, gridUnit),
                    CurrentExitRequirement = plan.DeliberateDoubleBack
                        ? new DistanceMeasure(nextProgressValue, gridUnit)
                        : requirement,
                    LastTravelDirection = actualDirection
                };
                consumed += available;
                break;
            }

            consumed += distanceNeeded;
            var eventTime = state.ElapsedTravelTime + ScaleTime(
                remainingAtCallStart,
                fullDistance.Value <= Epsilon ? 0d : Math.Clamp(consumed / fullDistance.Value, 0d, 1d));
            var exitedHex = traversal.CurrentHex;
            var enteredHex = exitedHex.Neighbor(actualDirection.Value);

            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.HexExited,
                eventTime,
                exitedHex,
                $"Exited hex {exitedHex} toward direction {actualDirection.Value}.");
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.HexEntered,
                eventTime,
                enteredHex,
                $"Entered hex {enteredHex}; keyed contents remain undiscovered until separately encountered.");

            traversal = new HexTraversalState
            {
                CurrentHex = enteredHex,
                EntryDirection = actualDirection,
                LastTravelDirection = actualDirection,
                Progress = new DistanceMeasure(0, gridUnit),
                CurrentExitRequirement = new DistanceMeasure(
                    world.Grid.NeighborCenterDistance.Value * profile.FarExitProgressFactor,
                    gridUnit)
            };
            state = state with
            {
                Traversal = traversal,
                Position = HexGeometry.HexToWorld(world.Grid, enteredHex),
                PositionPrecision = WorldPositionPrecision.HexAnchor
            };

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
                        enteredHex,
                        $"{plan.NavigationAid.Key} reset veer at the hex boundary; the expedition has not necessarily recognized that it was lost.");
                }

                events.Add(
                    active.WatchNumber,
                    CrawlRuntimeEventKind.NavigationDecisionRequired,
                    eventTime,
                    enteredHex,
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
                    enteredHex,
                    "Hex boundary crossed; review terrain/travel conditions before continuing the remaining watch.");
                pause = RuntimePauseReason.ConditionsReviewRequired;
                break;
            }

            if (distanceNeeded <= Epsilon && targetDistance - consumed <= Epsilon)
            {
                break;
            }
        }

        var consumedTime = pause is null
            ? targetDuration
            : ScaleTime(
                remainingAtCallStart,
                fullDistance.Value <= Epsilon ? 0d : Math.Clamp(consumed / fullDistance.Value, 0d, 1d));
        consumedTime = ClampTime(consumedTime, TimeSpan.Zero, targetDuration);

        var distanceTraveled = Add(state.DistanceTraveled, new DistanceMeasure(consumed, gridUnit));
        state = state with
        {
            Traversal = traversal,
            DistanceTraveled = distanceTraveled,
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
                $"Traveled {Format(consumed)} {gridUnit.Symbol} during this watch segment.",
                consumed,
                gridUnit.Symbol);
        }

        return Finish(state, knowledge, pause, active.Remaining, events);
    }

    private static WatchAdvanceResult AdvanceHexSteps(
        OverworldDefinition world,
        ExpeditionState state,
        PlayerKnowledgeState knowledge,
        WatchTravelPlan plan,
        ResolvedTravelAmount travel,
        ActiveWatchState active,
        TimeSpan remainingAtCallStart,
        TimeSpan targetDuration,
        EventCollector events)
    {
        var totalSteps = travel.HexSteps!.Value;
        var targetFraction = remainingAtCallStart <= TimeSpan.Zero
            ? 0d
            : targetDuration.TotalSeconds / remainingAtCallStart.TotalSeconds;
        var stepsBeforeTarget = targetDuration == remainingAtCallStart
            ? totalSteps
            : (int)Math.Floor(totalSteps * Math.Clamp(targetFraction, 0d, 1d) + Epsilon);
        var stepDuration = totalSteps == 0 ? TimeSpan.Zero : ScaleTime(remainingAtCallStart, 1d / totalSteps);
        var actualDirection = state.ActualDirection!.Value;
        var completed = 0;
        RuntimePauseReason? pause = null;

        for (var index = 0; index < stepsBeforeTarget; index++)
        {
            var exitedHex = state.CurrentHex;
            var enteredHex = exitedHex.Neighbor(actualDirection.Value);
            completed++;
            var eventTime = state.ElapsedTravelTime + ScaleTime(stepDuration, completed);

            events.Add(active.WatchNumber, CrawlRuntimeEventKind.HexExited, eventTime, exitedHex, $"Exited hex {exitedHex}.");
            events.Add(active.WatchNumber, CrawlRuntimeEventKind.HexEntered, eventTime, enteredHex, $"Entered hex {enteredHex}; keyed contents remain undiscovered until separately encountered.");

            state = state with
            {
                Traversal = new HexTraversalState
                {
                    CurrentHex = enteredHex,
                    EntryDirection = actualDirection,
                    LastTravelDirection = actualDirection,
                    Progress = new DistanceMeasure(0, world.Grid.NeighborCenterDistance.Unit)
                },
                Position = HexGeometry.HexToWorld(world.Grid, enteredHex),
                PositionPrecision = WorldPositionPrecision.HexAnchor
            };

            if (state.Navigation.IsLost)
            {
                if (plan.NavigationAid.ResetsVeerAtBoundary && state.Navigation.VeerSteps != 0)
                {
                    state = state with { Navigation = new NavigationRuntimeState(true, 0) };
                    events.Add(active.WatchNumber, CrawlRuntimeEventKind.VeerReset, eventTime, enteredHex, $"{plan.NavigationAid.Key} reset veer at the boundary.");
                }

                events.Add(active.WatchNumber, CrawlRuntimeEventKind.NavigationDecisionRequired, eventTime, enteredHex, "Lost-recognition input is required before continuing.");
                pause = RuntimePauseReason.LostRecognitionRequired;
                break;
            }

            if (!plan.ContinueAcrossBoundaries)
            {
                events.Add(active.WatchNumber, CrawlRuntimeEventKind.ConditionsReviewRequired, eventTime, enteredHex, "Review travel conditions before continuing.");
                pause = RuntimePauseReason.ConditionsReviewRequired;
                break;
            }
        }

        var consumedTime = pause is null ? targetDuration : ScaleTime(stepDuration, completed);
        consumedTime = ClampTime(consumedTime, TimeSpan.Zero, targetDuration);
        var physicalDistance = new DistanceMeasure(
            world.Grid.NeighborCenterDistance.Value * completed,
            world.Grid.NeighborCenterDistance.Unit);

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

        return Finish(state, knowledge, pause, active.Remaining, events);
    }

    private static bool IsImmediateExit(
        CrawlProcedureProfile profile,
        HexGridDefinition grid,
        HexTraversalState traversal,
        HexDirection direction,
        bool deliberateDoubleBack)
    {
        if (!profile.TracksIntraHexProgress)
        {
            return false;
        }

        var requirement = DetermineExitRequirement(profile, grid, traversal, direction, deliberateDoubleBack);
        var progress = Convert(traversal.Progress, requirement.Unit);
        return deliberateDoubleBack
            ? progress.Value <= Epsilon
            : progress.Value + Epsilon >= requirement.Value;
    }

    private static WatchAdvanceResult TriggerEncounter(
        OverworldDefinition world,
        ExpeditionState state,
        PlayerKnowledgeState knowledge,
        ActiveWatchState active,
        EventCollector events)
    {
        var encounter = active.Encounter;
        events.Add(
            active.WatchNumber,
            CrawlRuntimeEventKind.EncounterTriggered,
            state.ElapsedTravelTime,
            state.CurrentHex,
            encounter.Note is { Length: > 0 }
                ? $"{encounter.Kind}: {encounter.Note}"
                : $"{encounter.Kind} triggered.",
            subjectId: encounter.LocationId);

        if (encounter.Kind == EncounterOutcomeKind.KeyedLocationDiscovery)
        {
            var locationId = encounter.LocationId!.Value;
            var location = world.Locations.SingleOrDefault(candidate => candidate.Id == locationId)
                ?? throw new InvalidOperationException("The resolved keyed location does not exist in this overworld.");
            var locationHex = HexGeometry.WorldToHex(world.Grid, location.Position);
            if (locationHex != state.CurrentHex)
            {
                throw new InvalidOperationException("A keyed-location encounter can only discover a location in the expedition's current hex.");
            }

            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.KeyedLocationEncountered,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Encountered keyed location {location.Name}.",
                subjectId: location.Id,
                subjectType: KnowledgeSubjectType.Location);

            knowledge = KnowledgeDiscovery.Discover(
                knowledge,
                location.Id,
                KnowledgeSubjectType.Location,
                "runtime:keyed-location");
            events.Add(
                active.WatchNumber,
                CrawlRuntimeEventKind.LocationDiscovered,
                state.ElapsedTravelTime,
                state.CurrentHex,
                $"Discovered location {location.Name}; no other contents of the hex were revealed.",
                subjectId: location.Id,
                subjectType: KnowledgeSubjectType.Location);
        }

        active = active with
        {
            EncounterHandled = true,
            PendingDecision = RuntimePauseReason.EncounterTriggered
        };
        state = state with { ActiveWatch = active };
        return Finish(state, knowledge, RuntimePauseReason.EncounterTriggered, active.Remaining, events);
    }

    private static WatchAdvanceResult CompleteWatch(
        ExpeditionState state,
        PlayerKnowledgeState knowledge,
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
        return Finish(state, knowledge, null, TimeSpan.Zero, events);
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
        PlayerKnowledgeState knowledge,
        RuntimePauseReason? pause,
        TimeSpan remaining,
        EventCollector events)
    {
        var newEvents = events.NewEvents.ToArray();
        if (newEvents.Length > 0)
        {
            state = state with { History = state.History.Concat(newEvents).ToArray() };
        }

        return new WatchAdvanceResult(state, knowledge, pause, remaining, newEvents);
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

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

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
