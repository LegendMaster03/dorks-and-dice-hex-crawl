using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class Phase12JourneyProcessTests
{
    private static readonly ExpeditionConsequenceProvenance Dm = new(
        ExpeditionConsequenceSourceKind.Dm,
        "phase-12-test");

    [Fact]
    public void OneRingMaterializesExecutableGenericJourneyPolicyWithoutIntervalOrPublishedFormula()
    {
        var procedure = Materialize(CrawlProcedureCatalog.OneRing2ePresetKey);
        var process = JourneyProcedurePolicyResolver.ResolveProcess(procedure);
        var events = JourneyProcedurePolicyResolver.ResolveEvents(procedure);

        Assert.Equal(JourneyPolicySupport.Supported, process.Support);
        Assert.Equal(["route", "events", "arrival"], process.StageKeys);
        Assert.Equal(JourneyStageTransitionModel.Sequential, process.StageTransitionModel);
        Assert.Equal(JourneyProgressValueKind.Numeric, process.ProgressKind);
        Assert.Equal("journey-progress", process.ProgressUnit);
        Assert.Equal(JourneyIntervalIntegrationModel.None, process.IntervalIntegrationModel);
        Assert.True(process.RoleDriven);
        Assert.Equal("final-stage-completion", process.CompletionModel);
        Assert.Equal(JourneyPolicySupport.Supported, events.Support);
        Assert.Equal([JourneyEventTriggerKind.ProcessProgress], events.TriggerSources);
        Assert.Equal(JourneyEventLinkMode.ProcessLinked, events.LinkMode);
        Assert.DoesNotContain(procedure.Modules, value => value.Module.Key == GenericProcedureCatalog.TimeIntervalModule);

        var parameters = procedure.Modules
            .Single(value => value.Module.Key == GenericProcedureCatalog.JourneyProcessModule)
            .Parameters;
        Assert.Equal("journey-leg", procedure.Modules
            .Single(value => value.Module.Key == GenericProcedureCatalog.MovementBudgetModule)
            .Parameters["budgetUnit"]);
        Assert.DoesNotContain(parameters.Keys, key => key.Contains("dice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameters.Keys, key => key.Contains("fatigueAmount", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(procedure.Modules
            .Single(value => value.Module.Key == GenericProcedureCatalog.JourneyEventsModule)
            .Parameters.Keys, key => key.Contains("table", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FocusedPoliciesUseExactPinnedSnapshotAndRejectUnknownVersionOrHandler()
    {
        var procedure = Materialize(CrawlProcedureCatalog.OneRing2ePresetKey) with
        {
            Key = "renamed-after-materialization",
            Name = "Renamed procedure"
        };
        var pinned = JourneyProcedurePolicyResolver.ResolveProcess(procedure);
        Assert.Equal(JourneyPolicySupport.Supported, pinned.Support);
        Assert.Equal(["route", "events", "arrival"], pinned.StageKeys);

        var selected = procedure.Modules.Single(value => value.Module.Key == GenericProcedureCatalog.JourneyProcessModule);
        var futureVersion = procedure with
        {
            Modules = procedure.Modules.Select(value => ReferenceEquals(value, selected)
                ? value with { Mechanic = value.Mechanic with { Version = 99 } }
                : value).ToArray()
        };
        Assert.Equal(JourneyPolicySupport.Unsupported, JourneyProcedurePolicyResolver.ResolveProcess(futureVersion).Support);

        var futureHandler = procedure with
        {
            Modules = procedure.Modules.Select(value => ReferenceEquals(value, selected)
                ? value with { Mechanic = value.Mechanic with { ExecutionHandler = "procedure.future-journey" } }
                : value).ToArray()
        };
        Assert.Equal(JourneyPolicySupport.Unsupported, JourneyProcedurePolicyResolver.ResolveProcess(futureHandler).Support);

        var simple = Materialize("simple-fixed-distance");
        Assert.Equal(JourneyPolicySupport.None, JourneyProcedurePolicyResolver.ResolveProcess(simple).Support);
        Assert.Equal(JourneyPolicySupport.None, JourneyProcedurePolicyResolver.ResolveEvents(simple).Support);
        Assert.ThrowsAny<InvalidOperationException>(() => GenericProcedureRuntime.Bind(procedure));
    }

    [Fact]
    public void SyntheticMixedProcedureMaterializesStandaloneWatchAndLandmarkEventPolicy()
    {
        var procedure = SyntheticProcedureFixtures.MixedProcedure();
        var policy = JourneyProcedurePolicyResolver.ResolveEvents(procedure);

        Assert.Equal(JourneyPolicySupport.Supported, policy.Support);
        Assert.Equal(JourneyEventLinkMode.Standalone, policy.LinkMode);
        Assert.Contains(JourneyEventTriggerKind.WatchCompleted, policy.TriggerSources);
        Assert.Contains(JourneyEventTriggerKind.Landmark, policy.TriggerSources);
        Assert.Contains(JourneyEventTriggerKind.Explicit, policy.TriggerSources);
        Assert.Equal(JourneyPolicySupport.None, JourneyProcedurePolicyResolver.ResolveProcess(procedure).Support);
    }

    [Fact]
    public void GenericThreeStageChallengeSupportsApproachesProgressComplicationTransitionAndCompletion()
    {
        var execution = NumericExecution(JourneyStageTransitionModel.Sequential, "final-stage-completion");
        var definition = ComplexHazardDefinition();
        var party = CrawlPartySheet.Empty;
        var processId = Guid.NewGuid();
        var state = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            definition,
            execution,
            new JourneyClockReference(TimeSpan.Zero, 0),
            party,
            Dm).State;

        var firstId = Guid.NewGuid();
        var first = JourneyProcessEngine.Resolve(
            state,
            new JourneyProcessResolutionInput
            {
                ResolutionId = firstId,
                ProcessId = processId,
                StageKey = "approach",
                ApproachKey = "find-crossing",
                ProgressDelta = 2,
                ComplicationDelta = 1,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            party);

        Assert.True(first.StateChanged);
        Assert.True(first.ProgressChanged);
        Assert.True(first.StageChanged);
        Assert.Equal("cross", first.Process.CurrentStageKey);
        Assert.Equal(1, first.Process.StageStates.Single(value => value.StageKey == "approach").Complications);
        Assert.Equal(2, first.Process.StageStates.Single(value => value.StageKey == "approach").NumericProgress);
        Assert.Equal("rules-core:competency/climb", definition.Stages[0].Approaches[0].CapabilityReference!.CapabilityKey);

        var retry = JourneyProcessEngine.Resolve(
            first.State,
            new JourneyProcessResolutionInput
            {
                ResolutionId = firstId,
                ProcessId = processId,
                StageKey = "approach",
                ApproachKey = "find-crossing",
                ProgressDelta = 2,
                ComplicationDelta = 1,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            party);
        Assert.False(retry.StateChanged);
        Assert.Single(retry.State.Resolutions);

        var second = JourneyProcessEngine.Resolve(
            first.State,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(),
                ProcessId = processId,
                StageKey = "cross",
                ApproachKey = "construct-bridge",
                CompleteStage = true,
                FailureDelta = 1,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(2), 0),
            party);
        Assert.Equal(JourneyProcessStatus.Active, second.Process.Status);
        Assert.Equal("secure", second.Process.CurrentStageKey);
        Assert.Equal(1, second.Process.StageStates.Single(value => value.StageKey == "cross").Failures);

        var third = JourneyProcessEngine.Resolve(
            second.State,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(),
                ProcessId = processId,
                StageKey = "secure",
                ApproachKey = "magic",
                CompleteStage = true,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(3), 0),
            party);
        Assert.Equal(JourneyProcessStatus.Completed, third.Process.Status);
        Assert.Empty(third.State.ActiveProcesses);
        Assert.Single(third.State.ClosedProcesses);
        Assert.Contains(third.State.History, value => value.Kind == JourneyHistoryKind.ProcessCompleted);
    }

    [Fact]
    public void FailedAttemptDoesNotFailProcessButExplicitFailureDoes()
    {
        var party = CrawlPartySheet.Empty;
        var processId = Guid.NewGuid();
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "storm-crossing",
            DisplayName = "Storm crossing",
            InitialStageKey = "hold-course",
            StageOrder = ["hold-course"],
            Stages =
            [
                new JourneyStageDefinition
                {
                    StageKey = "hold-course",
                    DisplayName = "Hold course",
                    CompletionModel = JourneyStageCompletionModel.Explicit
                }
            ]
        };
        var started = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            definition,
            NumericExecution(JourneyStageTransitionModel.Explicit, "explicit-completion"),
            new JourneyClockReference(TimeSpan.Zero, 0),
            party,
            Dm).State;

        var failedAttempt = JourneyProcessEngine.Resolve(
            started,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(), ProcessId = processId, StageKey = "hold-course",
                FailureDelta = 1, ComplicationDelta = 1, Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0), party);
        Assert.Equal(JourneyProcessStatus.Active, failedAttempt.Process.Status);
        Assert.Equal(1, failedAttempt.Process.StageStates.Single().Failures);

        var explicitFailure = JourneyProcessEngine.Resolve(
            failedAttempt.State,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(), ProcessId = processId, StageKey = "hold-course",
                FailProcess = true, Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0), party);
        Assert.Equal(JourneyProcessStatus.Failed, explicitFailure.Process.Status);
        Assert.Single(explicitFailure.State.ClosedProcesses);
    }

    [Fact]
    public void ExplicitStateProgressAndArbitraryUnitsArePreserved()
    {
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "haunted-region",
            DisplayName = "Haunted region",
            InitialStageKey = "ward",
            StageOrder = ["ward"],
            Stages =
            [
                new JourneyStageDefinition
                {
                    StageKey = "ward", DisplayName = "Ward", CompletionModel = JourneyStageCompletionModel.Explicit,
                    InitialProgressState = "unsettled"
                }
            ]
        };
        var execution = NumericExecution(JourneyStageTransitionModel.Explicit, "explicit-completion") with
        {
            ProgressKind = JourneyProgressValueKind.ExplicitState,
            ProgressUnit = null
        };
        var processId = Guid.NewGuid();
        var started = JourneyProcessEngine.Start(ExpeditionJourneyState.Empty, processId, definition, execution,
            new JourneyClockReference(TimeSpan.Zero, 0), CrawlPartySheet.Empty, Dm).State;
        var resolved = JourneyProcessEngine.Resolve(started,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(), ProcessId = processId, StageKey = "ward",
                ProgressState = "ward-stable", Provenance = Dm
            }, new JourneyClockReference(TimeSpan.Zero, 0), CrawlPartySheet.Empty);
        Assert.Equal("ward-stable", resolved.Process.StageStates.Single().ExplicitState);

        var customUnit = NumericExecution(JourneyStageTransitionModel.Explicit, "explicit-completion") with
        {
            ProgressUnit = "custom-route-segments"
        };
        customUnit.Validate();
        Assert.Equal("custom-route-segments", customUnit.ProgressUnit);
    }

    [Fact]
    public void RoleDrivenResolutionUsesCurrentTypedAssignmentAndHistorySnapshotsParticipant()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var assignment = Guid.NewGuid();
        var party = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(first, "First"), new CrawlPartyMember(second, "Second")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(assignment, ParticipantActivityAssignmentScope.Role, first, "guide", "guide")
            ]
        };
        party.Validate();
        var processId = Guid.NewGuid();
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "role-journey",
            DisplayName = "Role journey",
            InitialStageKey = "route",
            StageOrder = ["route"],
            Stages = [new JourneyStageDefinition { StageKey = "route", DisplayName = "Route", CompletionModel = JourneyStageCompletionModel.Explicit }]
        };
        var execution = NumericExecution(JourneyStageTransitionModel.Explicit, "explicit-completion") with { RoleDriven = true };
        var started = JourneyProcessEngine.Start(ExpeditionJourneyState.Empty, processId, definition, execution,
            new JourneyClockReference(TimeSpan.Zero, 0), party, Dm).State;
        var result = JourneyProcessEngine.Resolve(started,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(), ProcessId = processId, StageKey = "route",
                RoleKey = "guide", ProgressDelta = 1, Provenance = Dm
            }, new JourneyClockReference(TimeSpan.Zero, 0), party);
        Assert.Equal(first, result.Resolution!.Actor!.ParticipantId);
        Assert.Equal(assignment, result.Resolution.Actor.AssignmentId);

        var reassigned = party with
        {
            ActivityAssignments =
            [new ParticipantActivityAssignment(Guid.NewGuid(), ParticipantActivityAssignmentScope.Role, second, "guide", "guide")]
        };
        result.State.Validate(reassigned);
        Assert.Equal(first, result.State.Resolutions.Single().Actor!.ParticipantId);
    }

    [Fact]
    public void AmbiguousRoleRequiresExplicitParticipantRatherThanSelectingFirst()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var party = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(first, "First"), new CrawlPartyMember(second, "Second")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(Guid.NewGuid(), ParticipantActivityAssignmentScope.Role, first, "guide", "guide"),
                new ParticipantActivityAssignment(Guid.NewGuid(), ParticipantActivityAssignmentScope.Role, second, "guide", "guide")
            ]
        };
        party.Validate();
        Assert.Throws<InvalidOperationException>(() =>
            JourneyProcessEngine.ResolveParticipantSnapshot(party, null, "guide", requireRole: true));
        Assert.Equal(second,
            JourneyProcessEngine.ResolveParticipantSnapshot(party, second, "guide", requireRole: true)!.ParticipantId);
    }

    [Fact]
    public void StandaloneEventOccurrenceIsStableAndResolvedContentIsExplicit()
    {
        var policy = JourneyProcedurePolicyResolver.ResolveEvents(SyntheticProcedureFixtures.MixedProcedure())
            with { TargetingModel = "explicit-target" };
        var id = Guid.NewGuid();
        var created = JourneyEventEngine.CreateOpportunity(
            ExpeditionJourneyState.Empty,
            policy,
            new JourneyEventOpportunityInput
            {
                OccurrenceId = id,
                Trigger = JourneyEventTriggerKind.Landmark,
                TriggerReference = "landmark:broken-tower",
                TargetKind = JourneyEventTargetKind.Unresolved,
                Environment = [new JourneyEnvironmentFactSnapshot("terrain", "rough", Source: "test")],
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty);
        Assert.Equal(JourneyEventStatus.ResolutionRequired, created.Occurrence.Status);
        Assert.Null(created.Occurrence.EventKey);

        var retry = JourneyEventEngine.CreateOpportunity(
            created.State,
            policy,
            new JourneyEventOpportunityInput
            {
                OccurrenceId = id,
                Trigger = JourneyEventTriggerKind.Landmark,
                TriggerReference = "landmark:broken-tower",
                TargetKind = JourneyEventTargetKind.Unresolved,
                Environment = [new JourneyEnvironmentFactSnapshot("terrain", "rough", Source: "test")],
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty);
        Assert.False(retry.StateChanged);
        Assert.Single(retry.State.EventOccurrences);

        var resolved = JourneyEventEngine.Resolve(
            created.State,
            policy,
            new JourneyEventResolutionInput
            {
                OccurrenceId = id,
                Status = JourneyEventStatus.Resolved,
                EventKey = "campaign-event",
                EventType = "hazard",
                TargetKind = JourneyEventTargetKind.Party,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty);
        Assert.Equal("campaign-event", resolved.Occurrence.EventKey);
        Assert.Equal("rough", resolved.Occurrence.Environment.Single().Value);
    }

    [Fact]
    public void MixedWatchIntegrationCreatesExactlyOneStandaloneOpportunityAndDoesNotChangeRuntime()
    {
        var procedure = SyntheticProcedureFixtures.MixedProcedure();
        var runtimeId = Guid.NewGuid();
        var before = new NonSpatialSessionState { Id = runtimeId, ElapsedTime = TimeSpan.Zero, CompletedWatches = 0 };
        var after = before with { ElapsedTime = TimeSpan.FromHours(4), CompletedWatches = 1 };
        var expedition = Stored(procedure, before);

        var first = JourneyRuntimeIntegration.ObserveCompletedWatches(expedition, before, after, ExpeditionJourneyState.Empty);
        Assert.Single(first.EventOccurrences);
        Assert.Equal(JourneyEventTriggerKind.WatchCompleted, first.EventOccurrences.Single().Trigger);
        Assert.Equal("watch:1", first.EventOccurrences.Single().TriggerReference);
        Assert.Equal(TimeSpan.FromHours(4), after.ElapsedTime);
        Assert.Equal(1, after.CompletedWatches);

        var retry = JourneyRuntimeIntegration.ObserveCompletedWatches(expedition with { Journey = first }, before, after, first);
        Assert.Single(retry.EventOccurrences);
        Assert.Single(retry.ObservedRuntimeOccurrenceIds);
    }

    [Fact]
    public void IntervalSpanningProcessGetsOnePendingOpportunityWithoutAutomaticProgress()
    {
        var processId = Guid.NewGuid();
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "collapsing-route",
            DisplayName = "Collapsing route",
            InitialStageKey = "escape",
            StageOrder = ["escape"],
            Stages = [new JourneyStageDefinition { StageKey = "escape", DisplayName = "Escape", CompletionModel = JourneyStageCompletionModel.Explicit }]
        };
        var execution = NumericExecution(JourneyStageTransitionModel.Explicit, "explicit-completion") with
        {
            IntervalIntegrationModel = JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity
        };
        var started = JourneyProcessEngine.Start(ExpeditionJourneyState.Empty, processId, definition, execution,
            new JourneyClockReference(TimeSpan.Zero, 0), CrawlPartySheet.Empty, Dm).State;
        var procedure = SyntheticProcedureFixtures.MixedProcedure();
        var before = new NonSpatialSessionState { Id = Guid.NewGuid(), ElapsedTime = TimeSpan.Zero, CompletedWatches = 0 };
        var after = before with { ElapsedTime = TimeSpan.FromHours(4), CompletedWatches = 1 };
        var expedition = Stored(procedure, before) with { Journey = started };

        var observed = JourneyRuntimeIntegration.ObserveCompletedWatches(expedition, before, after, started);
        var process = Assert.Single(observed.ActiveProcesses);
        Assert.Equal(JourneyProcessStatus.ResolutionRequired, process.Status);
        Assert.Single(process.PendingActions);
        Assert.Equal(0, process.StageStates.Single().NumericProgress);
    }

    [Fact]
    public void AggregateConsequenceTransitionUsesPhase10AndPhase11WithoutJourneyOwnedResourceMutation()
    {
        var procedure = SyntheticProcedureFixtures.MixedProcedure();
        var target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party);
        var resource = new ExpeditionResource
        {
            Id = Guid.NewGuid(), ResourceKey = "food", Target = target,
            InventoryModel = ExpeditionResourceInventoryModel.SupplyDie, SupplyDieSides = 8
        };
        var expedition = Stored(procedure, new NonSpatialSessionState
        {
            Id = Guid.NewGuid(), ElapsedTime = TimeSpan.Zero, CompletedWatches = 0
        }) with { Resources = new ExpeditionResourceState { Resources = [resource] } };
        var consequence = new ExpeditionConsequence
        {
            Id = Guid.NewGuid(),
            ConsequenceKey = "journey-food-loss",
            Category = ExpeditionConsequenceCategory.ResourceChange,
            Target = target,
            Components =
            [
                new ResourceChangeConsequenceComponent
                {
                    ResourceKey = "food", ResourceId = resource.Id,
                    Operation = ResourceChangeOperation.SetSupplyDie, SupplyDieSides = 6
                }
            ],
            Provenance = new ExpeditionConsequenceProvenance(ExpeditionConsequenceSourceKind.JourneyEvent, "journey-event")
        };

        var first = ExpeditionConsequenceAggregateTransition.Apply(expedition, consequence, Dm);
        Assert.True(first.StateChanged);
        Assert.Equal(6, first.Expedition.Resources.Resources.Single().SupplyDieSides);
        Assert.Equal(ExpeditionConsequenceStatus.Applied,
            first.Expedition.Effects.AppliedConsequences.Single().Status);

        var retry = ExpeditionConsequenceAggregateTransition.Apply(first.Expedition, consequence, Dm);
        Assert.False(retry.StateChanged);
        Assert.Equal(6, retry.Expedition.Resources.Resources.Single().SupplyDieSides);
        Assert.Single(retry.Expedition.Resources.History);
    }

    [Fact]
    public void EncounterCircumstanceRemainsPendingForPhase13()
    {
        var procedure = SyntheticProcedureFixtures.MixedProcedure();
        var expedition = Stored(procedure, new NonSpatialSessionState
        {
            Id = Guid.NewGuid(), ElapsedTime = TimeSpan.Zero, CompletedWatches = 0
        });
        var consequence = new ExpeditionConsequence
        {
            Id = Guid.NewGuid(), ConsequenceKey = "bad-position",
            Category = ExpeditionConsequenceCategory.EncounterCircumstance,
            Target = new ExpeditionEffectTarget(ExpeditionEffectScope.Party),
            Components = [new EncounterCircumstanceConsequenceComponent("bad-position", "narrow-route")],
            Provenance = new ExpeditionConsequenceProvenance(ExpeditionConsequenceSourceKind.JourneyEvent, "journey-event")
        };
        var result = ExpeditionConsequenceAggregateTransition.Apply(expedition, consequence, Dm);
        Assert.Equal(ExpeditionConsequenceStatus.Deferred, result.Status);
        Assert.Single(result.Expedition.Effects.PendingConsequences);
    }

    private static CampaignProcedure Materialize(string key) =>
        CrawlProcedureCatalog.Resolve(key).MaterializeGeneric().Procedure;

    private static JourneyProcessExecutionSnapshot NumericExecution(
        JourneyStageTransitionModel transition,
        string completionModel) => new()
    {
        StageModel = "explicit-stages",
        StageTransitionModel = transition,
        ProgressModel = "resolved-progress",
        ProgressKind = JourneyProgressValueKind.Numeric,
        ProgressUnit = "progress-points",
        AllowNegativeProgress = false,
        CompletionModel = completionModel,
        RoleDriven = false,
        RoleAssignmentModel = JourneyRoleAssignmentModel.CurrentAtResolution,
        IntervalIntegrationModel = JourneyIntervalIntegrationModel.None,
        BlocksRelevantTravelWhileResolutionRequired = false,
        MechanicKey = GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
        MechanicVersion = 1,
        ExecutionHandler = GenericProcedureExecutionHandlers.DeclarativeContract
    };

    private static JourneyProcessDefinition ComplexHazardDefinition() => new()
    {
        ProcessKey = "cross-flooded-canyon",
        DisplayName = "Cross the flooded canyon",
        InitialStageKey = "approach",
        StageOrder = ["approach", "cross", "secure"],
        Stages =
        [
            new JourneyStageDefinition
            {
                StageKey = "approach",
                DisplayName = "Find an approach",
                CompletionModel = JourneyStageCompletionModel.ProgressThreshold,
                ProgressTarget = 2,
                Approaches =
                [
                    new JourneyApproachDefinition("climb", "Climb", new JourneyExternalCapabilityReference("rules-core:competency/climb")),
                    new JourneyApproachDefinition("find-crossing", "Find crossing")
                ]
            },
            new JourneyStageDefinition
            {
                StageKey = "cross",
                DisplayName = "Cross",
                CompletionModel = JourneyStageCompletionModel.Explicit,
                Approaches =
                [
                    new JourneyApproachDefinition("construct-bridge", "Construct bridge"),
                    new JourneyApproachDefinition("magic", "Magic")
                ]
            },
            new JourneyStageDefinition
            {
                StageKey = "secure",
                DisplayName = "Secure far side",
                CompletionModel = JourneyStageCompletionModel.Explicit,
                Approaches = [new JourneyApproachDefinition("magic", "Magic")]
            }
        ]
    };

    private static StoredExpedition Stored(CampaignProcedure procedure, CrawlSessionRuntimeState runtime)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredExpedition(
            "test",
            runtime,
            new NonSpatialCrawlSessionContext("test"),
            null,
            procedure,
            null,
            TimeSpan.Zero,
            "owner",
            1,
            now,
            now);
    }
}