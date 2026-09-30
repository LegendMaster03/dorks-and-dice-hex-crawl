using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class ParticipantActivityPolicyTests
{
    [Fact]
    public void PolicyUsesExactStoredProcedureValuesInsteadOfCurrentCatalogRecipe()
    {
        var procedure = WithActivityParameters(
            Materialize(CrawlProcedureCatalog.Dnd2024PresetKey),
            ("assignmentScope", "participant"),
            ("activityBudgetModel", "custom-budget"),
            ("activityKeys", "custom-a; custom-b;custom-a"),
            ("roleKeys", "custom-role; second-role"));

        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);

        Assert.Equal(ParticipantActivityPolicySupport.Supported, policy.Support);
        Assert.Equal(ParticipantActivityAssignmentScope.Participant, policy.AssignmentScope);
        Assert.Equal("custom-budget", policy.ActivityBudgetModel);
        Assert.Equal(["custom-a", "custom-b"], policy.ActivityKeys);
        Assert.Equal(["custom-role", "second-role"], policy.RoleKeys);
    }

    [Fact]
    public void ProcedureWithoutPartyActivitiesProducesNoInventedPolicy()
    {
        var procedure = Materialize(CrawlProcedureCatalog.BxPresetKey);

        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);

        Assert.Equal(ParticipantActivityPolicySupport.None, policy.Support);
        Assert.Null(policy.AssignmentScope);
        Assert.Empty(policy.ActivityKeys);
        Assert.Empty(policy.RoleKeys);
    }

    [Fact]
    public void FutureActivityMechanicIsReportedUnsupportedWithoutReinterpretation()
    {
        var procedure = Materialize(CrawlProcedureCatalog.Dnd2024PresetKey);
        var activity = ActivityModule(procedure);
        var future = activity with
        {
            Mechanic = activity.Mechanic with
            {
                Key = "future-participant-activity-policy",
                Version = 2
            }
        };
        procedure = procedure with
        {
            Modules = procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.PartyActivitiesModule ? future : module)
                .ToArray()
        };

        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);

        Assert.Equal(ParticipantActivityPolicySupport.Unsupported, policy.Support);
        Assert.Equal("future-participant-activity-policy", policy.MechanicKey);
        Assert.Equal(2, policy.MechanicVersion);
        Assert.NotNull(policy.UnsupportedReason);
    }

    [Fact]
    public void ParticipantScopeAcceptsMemberActivityAndDoesNotInventRoleCardinality()
    {
        var procedure = Materialize(CrawlProcedureCatalog.Dnd2024PresetKey);
        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var party = new CrawlPartySheet
        {
            Members =
            [
                new CrawlPartyMember(first, "First"),
                new CrawlPartyMember(second, "Second")
            ],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Participant,
                    first,
                    "navigate",
                    "navigator"),
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Participant,
                    second,
                    "search",
                    "navigator")
            ]
        };

        ParticipantActivityPolicyResolver.ValidateAssignments(party, policy);

        Assert.Equal(2, party.ActivityAssignments.Count);
    }

    [Fact]
    public void PartyScopeAcceptsPartyWideActivityWithoutParticipant()
    {
        var procedure = WithActivityParameters(
            Materialize(CrawlProcedureCatalog.Dnd2024PresetKey),
            ("assignmentScope", "party"),
            ("activityBudgetModel", "party-wide"),
            ("activityKeys", "custom-party-action"),
            ("roleKeys", "none"));
        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);
        var party = new CrawlPartySheet
        {
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Party,
                    null,
                    "custom-party-action",
                    null)
            ]
        };

        ParticipantActivityPolicyResolver.ValidateAssignments(party, policy);
    }

    [Fact]
    public void RoleScopeAcceptsMemberRoleWithoutFabricatedActivityMapping()
    {
        var procedure = Materialize(CrawlProcedureCatalog.OneRing2ePresetKey);
        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);
        var member = Guid.NewGuid();
        var party = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Guide")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Role,
                    member,
                    null,
                    "guide")
            ]
        };

        ParticipantActivityPolicyResolver.ValidateAssignments(party, policy);

        Assert.Null(party.ActivityAssignments.Single().ActivityKey);
    }

    [Fact]
    public void Dnd2024ParticipantActivityProofRemainsGeneric()
    {
        var policy = ParticipantActivityPolicyResolver.Resolve(Materialize(CrawlProcedureCatalog.Dnd2024PresetKey));

        Assert.Equal(ParticipantActivityAssignmentScope.Participant, policy.AssignmentScope);
        Assert.Equal("travel-compatible", policy.ActivityBudgetModel);
        Assert.Equal(["navigate", "forage", "search", "watch", "stealth"], policy.ActivityKeys);
        Assert.Equal(["navigator", "lookout"], policy.RoleKeys);
    }

    [Fact]
    public void PathfinderHexplorationTreatsNoneRoleListAsNoRoles()
    {
        var policy = ParticipantActivityPolicyResolver.Resolve(Materialize(CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey));

        Assert.Equal(ParticipantActivityAssignmentScope.Participant, policy.AssignmentScope);
        Assert.Equal("daily-activity-budget", policy.ActivityBudgetModel);
        Assert.Equal(["travel", "reconnoiter", "fortify-camp", "map-area", "subsist"], policy.ActivityKeys);
        Assert.Empty(policy.RoleKeys);
        Assert.DoesNotContain("none", policy.RoleKeys);
    }

    [Fact]
    public void ForbiddenLandsQuarterDayActivitiesAndRolesRemainData()
    {
        var policy = ParticipantActivityPolicyResolver.Resolve(Materialize(CrawlProcedureCatalog.ForbiddenLandsPresetKey));

        Assert.Equal("quarter-day", policy.ActivityBudgetModel);
        Assert.Equal(
            ["hike", "lead-way", "keep-watch", "forage", "hunt", "fish", "make-camp", "rest", "sleep"],
            policy.ActivityKeys);
        Assert.Equal(["leader", "lookout"], policy.RoleKeys);
    }

    [Fact]
    public void OneRingRolesResolveWithoutBindingStructuralProcedureOrInventingIntervalDependency()
    {
        var procedure = Materialize(CrawlProcedureCatalog.OneRing2ePresetKey);
        var activity = ActivityModule(procedure);

        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);

        Assert.Equal(ParticipantActivityPolicySupport.Supported, policy.Support);
        Assert.Equal(ParticipantActivityAssignmentScope.Role, policy.AssignmentScope);
        Assert.Equal("journey-role", policy.ActivityBudgetModel);
        Assert.Equal(["guide", "hunter", "lookout", "scout"], policy.RoleKeys);
        Assert.DoesNotContain("time.interval-duration", activity.Mechanic.InputContract);
        Assert.DoesNotContain("movement.budget", activity.Mechanic.InputContract);
        Assert.Contains("participant.activity-state", activity.Mechanic.OutputContract);
        Assert.DoesNotContain(
            procedure.Modules,
            module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
    }

    [Fact]
    public void ArbitraryStoredKeysCanBeAssignedWithoutCodeChanges()
    {
        var procedure = WithActivityParameters(
            Materialize(CrawlProcedureCatalog.Dnd2024PresetKey),
            ("assignmentScope", "participant"),
            ("activityBudgetModel", "future-model"),
            ("activityKeys", "future-a;future-b"),
            ("roleKeys", "future-role"));
        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);
        var member = Guid.NewGuid();
        var party = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Future")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Participant,
                    member,
                    "future-b",
                    "future-role")
            ]
        };

        ParticipantActivityPolicyResolver.ValidateAssignments(party, policy);
    }

    [Fact]
    public void UnsupportedPolicyPreservesStructurallyValidAssignmentState()
    {
        var procedure = Materialize(CrawlProcedureCatalog.Dnd2024PresetKey);
        var activity = ActivityModule(procedure);
        procedure = procedure with
        {
            Modules = procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.PartyActivitiesModule
                    ? module with { Mechanic = activity.Mechanic with { Version = 99 } }
                    : module)
                .ToArray()
        };
        var policy = ParticipantActivityPolicyResolver.Resolve(procedure);
        var member = Guid.NewGuid();
        var party = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(member, "Future")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(),
                    ParticipantActivityAssignmentScope.Participant,
                    member,
                    "future-key",
                    null)
            ]
        };

        ParticipantActivityPolicyResolver.ValidateAssignments(party, policy);

        Assert.Equal(ParticipantActivityPolicySupport.Unsupported, policy.Support);
        Assert.Single(party.ActivityAssignments);
    }

    private static CampaignProcedure Materialize(string presetKey) =>
        CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;

    private static MaterializedProcedureModule ActivityModule(CampaignProcedure procedure) =>
        procedure.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.PartyActivitiesModule);

    private static CampaignProcedure WithActivityParameters(
        CampaignProcedure procedure,
        params (string Key, string Value)[] parameters)
    {
        var activity = ActivityModule(procedure) with
        {
            Parameters = parameters.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal)
        };
        return procedure with
        {
            Modules = procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.PartyActivitiesModule ? activity : module)
                .ToArray()
        };
    }
}
