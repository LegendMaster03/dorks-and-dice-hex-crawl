using System.Globalization;
using HexCrawl.Domain.Knowledge;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.Runtime;

public sealed partial class CrawlRuntimeEngine
{
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


}
