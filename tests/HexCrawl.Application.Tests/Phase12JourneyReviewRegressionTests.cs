using System.Reflection;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase12JourneyReviewRegressionTests
{
    private static readonly ExpeditionConsequenceProvenance Dm = new(
        ExpeditionConsequenceSourceKind.Dm,
        "phase-12-review");

    [Fact]
    public void CompletedWatchDoesNotCreateJourneyBookkeepingWhenNoJourneyCapabilityApplies()
    {
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var id = Guid.NewGuid();
        var before = new NonSpatialSessionState
        {
            Id = id,
            ElapsedTime = TimeSpan.Zero,
            CompletedWatches = 0
        };
        var after = before with
        {
            ElapsedTime = TimeSpan.FromHours(4),
            CompletedWatches = 1
        };
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "simple",
            before,
            new NonSpatialCrawlSessionContext("review"),
            null,
            procedure,
            null,
            TimeSpan.Zero,
            "owner",
            1,
            now,
            now);
        var state = new ExpeditionJourneyState();

        var observed = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition,
            before,
            after,
            state);

        Assert.Same(state, observed);
        Assert.Empty(observed.ObservedRuntimeOccurrenceIds);
        Assert.Empty(observed.EventOccurrences);
        Assert.Empty(observed.History);
    }

    [Fact]
    public void ResolutionRequiredProcessMustConsumeAnExplicitPendingAction()
    {
        var processId = Guid.NewGuid();
        var definition = SingleStageDefinition("watch-challenge", "route");
        var execution = NumericExecution("explicit-completion") with
        {
            StageTransitionModel = JourneyStageTransitionModel.Explicit,
            IntervalIntegrationModel = JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity
        };
        var started = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            definition,
            execution,
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty,
            Dm).State;
        var before = new NonSpatialSessionState
        {
            Id = Guid.NewGuid(),
            ElapsedTime = TimeSpan.Zero,
            CompletedWatches = 0
        };
        var after = before with
        {
            ElapsedTime = TimeSpan.FromHours(4),
            CompletedWatches = 1
        };
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "challenge",
            before,
            new NonSpatialCrawlSessionContext("review"),
            null,
            procedure,
            null,
            TimeSpan.Zero,
            "owner",
            1,
            now,
            now)
        {
            Journey = started
        };
        var observed = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition,
            before,
            after,
            started);
        var pending = Assert.Single(Assert.Single(observed.ActiveProcesses).PendingActions);
        var resolution = new JourneyProcessResolutionInput
        {
            ResolutionId = Guid.NewGuid(),
            ProcessId = processId,
            StageKey = "route",
            ProgressDelta = 1,
            Provenance = Dm
        };

        Assert.Throws<InvalidOperationException>(() =>
            JourneyProcessEngine.Resolve(
                observed,
                resolution,
                new JourneyClockReference(TimeSpan.FromHours(4), 1),
                CrawlPartySheet.Empty));

        var resolved = JourneyProcessEngine.Resolve(
            observed,
            resolution with { PendingActionId = pending.Id },
            new JourneyClockReference(TimeSpan.FromHours(4), 1),
            CrawlPartySheet.Empty);
        Assert.Empty(resolved.Process.PendingActions);
        Assert.Equal(JourneyProcessStatus.Active, resolved.Process.Status);
        Assert.Equal(1, resolved.Process.StageStates.Single().NumericProgress);
    }

    [Fact]
    public void FinalStageCompletionModelCanNotBeClosedOrExplicitlyCompletedEarly()
    {
        var processId = Guid.NewGuid();
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "ordered-route",
            DisplayName = "Ordered route",
            InitialStageKey = "route",
            StageOrder = ["route", "arrival"],
            Stages =
            [
                new JourneyStageDefinition
                {
                    StageKey = "route",
                    DisplayName = "Route",
                    CompletionModel = JourneyStageCompletionModel.Explicit
                },
                new JourneyStageDefinition
                {
                    StageKey = "arrival",
                    DisplayName = "Arrival",
                    CompletionModel = JourneyStageCompletionModel.Explicit
                }
            ]
        };
        var execution = NumericExecution("final-stage-completion");
        var started = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            definition,
            execution,
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty,
            Dm).State;

        Assert.Throws<InvalidOperationException>(() => JourneyProcessEngine.Close(
            started,
            processId,
            JourneyProcessStatus.Completed,
            "premature",
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty,
            Dm));
        Assert.Throws<InvalidOperationException>(() => JourneyProcessEngine.Resolve(
            started,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(),
                ProcessId = processId,
                StageKey = "route",
                CompleteProcess = true,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty));

        var route = JourneyProcessEngine.Resolve(
            started,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(),
                ProcessId = processId,
                StageKey = "route",
                CompleteStage = true,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            CrawlPartySheet.Empty);
        Assert.Equal("arrival", route.Process.CurrentStageKey);
        Assert.Throws<InvalidOperationException>(() => JourneyProcessEngine.Close(
            route.State,
            processId,
            JourneyProcessStatus.Completed,
            "arrival not complete",
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            CrawlPartySheet.Empty,
            Dm));

        var arrival = JourneyProcessEngine.Resolve(
            route.State,
            new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(),
                ProcessId = processId,
                StageKey = "arrival",
                CompleteStage = true,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(2), 0),
            CrawlPartySheet.Empty);
        Assert.Equal(JourneyProcessStatus.Completed, arrival.Process.Status);
        Assert.Empty(arrival.State.ActiveProcesses);
        Assert.Single(arrival.State.ClosedProcesses);
    }

    [Fact]
    public void ExplicitStateProcessRejectsNumericProgressThresholdStage()
    {
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "stateful",
            DisplayName = "Stateful",
            InitialStageKey = "phase",
            StageOrder = ["phase"],
            Stages =
            [
                new JourneyStageDefinition
                {
                    StageKey = "phase",
                    DisplayName = "Phase",
                    CompletionModel = JourneyStageCompletionModel.ProgressThreshold,
                    ProgressTarget = 1,
                    InitialProgressState = "initial"
                }
            ]
        };
        var execution = NumericExecution("explicit-completion") with
        {
            ProgressKind = JourneyProgressValueKind.ExplicitState,
            ProgressUnit = null
        };

        Assert.Throws<InvalidOperationException>(() => JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            Guid.NewGuid(),
            definition,
            execution,
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty,
            Dm));
    }

    [Fact]
    public void TravelRoleTargetingRejectsArbitraryPartyTarget()
    {
        var policy = JourneyProcedurePolicyResolver.ResolveEvents(
            SyntheticProcedureFixtures.MixedProcedure());
        var occurrenceId = Guid.NewGuid();
        var created = JourneyEventEngine.CreateOpportunity(
            ExpeditionJourneyState.Empty,
            policy,
            new JourneyEventOpportunityInput
            {
                OccurrenceId = occurrenceId,
                Trigger = JourneyEventTriggerKind.Explicit,
                TriggerReference = "manual:role-target",
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty);

        Assert.Throws<InvalidOperationException>(() => JourneyEventEngine.Resolve(
            created.State,
            policy,
            new JourneyEventResolutionInput
            {
                OccurrenceId = occurrenceId,
                Status = JourneyEventStatus.Resolved,
                EventKey = "event",
                TargetKind = JourneyEventTargetKind.Party,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty));
    }

    [Fact]
    public void JourneyEventReplayMustMatchPreviouslyResolvedMeaning()
    {
        var policy = JourneyProcedurePolicyResolver.ResolveEvents(
            SyntheticProcedureFixtures.MixedProcedure()) with { TargetingModel = "explicit-target" };
        var occurrenceId = Guid.NewGuid();
        var opportunity = new JourneyEventOpportunityInput
        {
            OccurrenceId = occurrenceId,
            Trigger = JourneyEventTriggerKind.Explicit,
            TriggerReference = "manual:test",
            Provenance = Dm
        };
        var created = JourneyEventEngine.CreateOpportunity(
            ExpeditionJourneyState.Empty,
            policy,
            opportunity,
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty);
        var resolution = new JourneyEventResolutionInput
        {
            OccurrenceId = occurrenceId,
            Status = JourneyEventStatus.Resolved,
            EventKey = "weather-turn",
            EventType = "hazard",
            TargetKind = JourneyEventTargetKind.Party,
            Provenance = Dm
        };
        var first = JourneyEventEngine.Resolve(
            created.State,
            policy,
            resolution,
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty);

        var retry = JourneyEventEngine.Resolve(
            first.State,
            policy,
            resolution,
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty);
        Assert.False(retry.StateChanged);

        Assert.Throws<InvalidOperationException>(() => JourneyEventEngine.Resolve(
            first.State,
            policy,
            resolution with { EventKey = "different-event" },
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty));
    }

    [Fact]
    public void PostgreSqlPersistenceRejectsIntegerEnumPayloads()
    {
        var field = typeof(PostgresHexCrawlStore).GetField(
            "JsonOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var options = Assert.IsType<JsonSerializerOptions>(field?.GetValue(null));

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<JourneyEventStatus>("1", options));
        Assert.Equal("\"Resolved\"", JsonSerializer.Serialize(JourneyEventStatus.Resolved, options));
    }

    private static JourneyProcessDefinition SingleStageDefinition(string processKey, string stageKey) => new()
    {
        ProcessKey = processKey,
        DisplayName = processKey,
        InitialStageKey = stageKey,
        StageOrder = [stageKey],
        Stages =
        [
            new JourneyStageDefinition
            {
                StageKey = stageKey,
                DisplayName = stageKey,
                CompletionModel = JourneyStageCompletionModel.Explicit
            }
        ]
    };

    private static JourneyProcessExecutionSnapshot NumericExecution(string completionModel) => new()
    {
        StageModel = "explicit-stages",
        StageTransitionModel = JourneyStageTransitionModel.Sequential,
        ProgressModel = "resolved-progress",
        ProgressKind = JourneyProgressValueKind.Numeric,
        ProgressUnit = "progress-points",
        CompletionModel = completionModel,
        RoleAssignmentModel = JourneyRoleAssignmentModel.CurrentAtResolution,
        IntervalIntegrationModel = JourneyIntervalIntegrationModel.None,
        MechanicKey = GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
        MechanicVersion = 1,
        ExecutionHandler = GenericProcedureExecutionHandlers.DeclarativeContract
    };
}
