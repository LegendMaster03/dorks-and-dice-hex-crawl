using HexCrawl.Domain.Procedure;

namespace HexCrawl.Domain.Runtime;

public static class NavigationResolutionTransition
{
    public static NavigationRuntimeState Apply(
        CrawlProcedureProfile profile,
        NavigationRuntimeState previous,
        ResolvedNavigation navigation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(navigation);

        if (navigation.Outcome == NavigationCheckOutcome.Succeeded)
        {
            return previous.IsLost
                ? previous
                : new NavigationRuntimeState(false, 0);
        }

        if (navigation.Outcome != NavigationCheckOutcome.Failed)
        {
            throw new InvalidOperationException("A required navigation check must resolve as Succeeded or Failed.");
        }

        var candidate = navigation.VeerStepsOnFailure
            ?? throw new InvalidOperationException("A failed navigation check requires a resolved veer.");

        if (!previous.IsLost)
        {
            return new NavigationRuntimeState(true, candidate);
        }

        if (!profile.UsesPersistentVeer || Math.Abs(candidate) > Math.Abs(previous.VeerSteps))
        {
            return new NavigationRuntimeState(true, candidate);
        }

        return previous;
    }
}
