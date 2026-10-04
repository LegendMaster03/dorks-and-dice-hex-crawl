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
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "watch-challenge",
            DisplayName = "Watch challenge",
            InitialStageKey = "route",
            StageOrder = ["route"],
            Stages =
            [
                new JourneyStageDefinition
                {
                    StageKey = "route",
                    DisplayName = "Route",
                    CompletionModel = JourneyStageCompletionModel.Explicit
                }
            ]
        };
        var execution = new JourneyProcessExecutionSnapshot
        {
            StageModel = "explicit-stages",
            StageTransitionModel = JourneyStageTransitionModel.Explicit,
            ProgressModel = "resolved-progress",
            ProgressKind = JourneyProgressValueKind.Numeric,
            ProgressUnit = "progress-points",
            CompletionModel = "explicit-completion",
            RoleAssignmentModel = JourneyRoleAssignmentModel.CurrentAtResolution,
            IntervalIntegrationModel = JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity,
            MechanicKey = GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
            MechanicVersion = 1,
            ExecutionHandler = GenericProcedureExecutionHandlers.DeclarativeContract
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
}
