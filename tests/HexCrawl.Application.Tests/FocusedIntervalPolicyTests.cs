using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class FocusedIntervalPolicyTests
{
    [Fact]
    public void SupportedIntervalUsesExactStoredSnapshotValue()
    {
        var catalogProcedure = Materialize(CrawlProcedureCatalog.Dnd2024PresetKey);
        var procedure = WithIntervalDuration(catalogProcedure, TimeSpan.FromHours(7));

        var policy = FocusedIntervalPolicyResolver.Resolve(procedure);

        Assert.Equal(FocusedIntervalPolicySupport.Supported, policy.Support);
        Assert.Equal(TimeSpan.FromHours(7), policy.IntervalDuration);
        Assert.Equal(GenericProcedureCatalog.FixedIntervalDurationMechanic, policy.MechanicKey);
        Assert.Equal(1, policy.MechanicVersion);
        Assert.Equal(GenericProcedureExecutionHandlers.FixedIntervalDuration, policy.ExecutionHandler);
        Assert.Equal(TimeSpan.FromHours(1), FocusedIntervalPolicyResolver.Resolve(catalogProcedure).IntervalDuration);
        Assert.Equal(TimeSpan.FromHours(7), ExpeditionProcedureRequirements.ResolveIntervalDuration(procedure));
    }

    [Fact]
    public void ProcedureWithoutTimeIntervalReturnsNone()
    {
        var procedure = Materialize(CrawlProcedureCatalog.OneRing2ePresetKey);

        var policy = FocusedIntervalPolicyResolver.Resolve(procedure);

        Assert.Equal(FocusedIntervalPolicySupport.None, policy.Support);
        Assert.Null(policy.IntervalDuration);
        Assert.Null(policy.MechanicKey);
        Assert.DoesNotContain(
            procedure.Modules,
            module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
    }

    [Fact]
    public void UnsupportedFutureIntervalPreservesStoredIdentityWithoutInventingDuration()
    {
        var procedure = Materialize(CrawlProcedureCatalog.Dnd2024PresetKey);
        var interval = IntervalModule(procedure);
        var future = interval with
        {
            Mechanic = interval.Mechanic with
            {
                Key = "future-interval-policy",
                Version = 99,
                ExecutionHandler = "future.interval.handler"
            },
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["durationTicks"] = TimeSpan.FromHours(7).Ticks.ToString(CultureInfo.InvariantCulture)
            }
        };
        procedure = ReplaceInterval(procedure, future);

        var policy = FocusedIntervalPolicyResolver.Resolve(procedure);

        Assert.Equal(FocusedIntervalPolicySupport.Unsupported, policy.Support);
        Assert.Null(policy.IntervalDuration);
        Assert.Equal("future-interval-policy", policy.MechanicKey);
        Assert.Equal(99, policy.MechanicVersion);
        Assert.Equal("future.interval.handler", policy.ExecutionHandler);
        Assert.NotNull(policy.UnsupportedReason);
        Assert.Throws<InvalidOperationException>(() =>
            ExpeditionProcedureRequirements.ResolveIntervalDuration(procedure));
    }

    [Fact]
    public void InvalidStoredFixedIntervalParametersAreUnsupported()
    {
        var procedure = Materialize(CrawlProcedureCatalog.Dnd2024PresetKey);
        var interval = IntervalModule(procedure) with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["durationTicks"] = "0"
            }
        };

        var policy = FocusedIntervalPolicyResolver.Resolve(ReplaceInterval(procedure, interval));

        Assert.Equal(FocusedIntervalPolicySupport.Unsupported, policy.Support);
        Assert.Null(policy.IntervalDuration);
        Assert.Equal(GenericProcedureCatalog.FixedIntervalDurationMechanic, policy.MechanicKey);
    }

    [Fact]
    public void Dnd2024RemainsStructuralButExposesOneHourFocusedInterval()
    {
        var procedure = Materialize(CrawlProcedureCatalog.Dnd2024PresetKey);

        var policy = FocusedIntervalPolicyResolver.Resolve(procedure);

        Assert.Throws<InvalidOperationException>(() => GenericProcedureRuntime.Bind(procedure));
        Assert.Equal(FocusedIntervalPolicySupport.Supported, policy.Support);
        Assert.Equal(TimeSpan.FromHours(1), policy.IntervalDuration);
    }

    [Fact]
    public void ForbiddenLandsRemainsStructuralButExposesSixHourFocusedInterval()
    {
        var procedure = Materialize(CrawlProcedureCatalog.ForbiddenLandsPresetKey);

        var policy = FocusedIntervalPolicyResolver.Resolve(procedure);
        var activities = ParticipantActivityPolicyResolver.Resolve(procedure);

        Assert.Throws<InvalidOperationException>(() => GenericProcedureRuntime.Bind(procedure));
        Assert.Equal(FocusedIntervalPolicySupport.Supported, policy.Support);
        Assert.Equal(TimeSpan.FromHours(6), policy.IntervalDuration);
        Assert.Equal("quarter-day", activities.ActivityBudgetModel);
    }

    [Fact]
    public void OneRingRemainsStructuralAndDoesNotFabricateFocusedInterval()
    {
        var procedure = Materialize(CrawlProcedureCatalog.OneRing2ePresetKey);

        var policy = FocusedIntervalPolicyResolver.Resolve(procedure);
        var activities = ParticipantActivityPolicyResolver.Resolve(procedure);

        Assert.Throws<InvalidOperationException>(() => GenericProcedureRuntime.Bind(procedure));
        Assert.Equal(FocusedIntervalPolicySupport.None, policy.Support);
        Assert.Null(policy.IntervalDuration);
        Assert.Equal(ParticipantActivityAssignmentScope.Role, activities.AssignmentScope);
        Assert.Equal(["guide", "hunter", "lookout", "scout"], activities.RoleKeys);
    }

    private static CampaignProcedure Materialize(string presetKey) =>
        CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;

    private static MaterializedProcedureModule IntervalModule(CampaignProcedure procedure) =>
        procedure.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);

    private static CampaignProcedure WithIntervalDuration(CampaignProcedure procedure, TimeSpan duration)
    {
        var interval = IntervalModule(procedure) with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["durationTicks"] = duration.Ticks.ToString(CultureInfo.InvariantCulture)
            }
        };
        return ReplaceInterval(procedure, interval);
    }

    private static CampaignProcedure ReplaceInterval(
        CampaignProcedure procedure,
        MaterializedProcedureModule interval) =>
        procedure with
        {
            Modules = procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule ? interval : module)
                .ToArray()
        };
}
