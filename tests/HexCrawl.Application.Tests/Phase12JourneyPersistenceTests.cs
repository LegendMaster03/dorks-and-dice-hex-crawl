using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class Phase12JourneyPersistenceTests
{
    [Fact]
    public async Task OneRingMaplessJourneyStateSurvivesPostgresRestart()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();

        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey).MaterializeGeneric().Procedure;
        var policy = JourneyProcedurePolicyResolver.ResolveProcess(procedure);
        Assert.Equal(JourneyPolicySupport.Supported, policy.Support);
        var guideId = Guid.NewGuid();
        var party = new CrawlPartySheet
        {
            Members = [new CrawlPartyMember(guideId, "Guide")],
            ActivityAssignments =
            [
                new ParticipantActivityAssignment(
                    Guid.NewGuid(), ParticipantActivityAssignmentScope.Role, guideId, "guide", "guide")
            ]
        };
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "route-to-refuge",
            DisplayName = "Route to refuge",
            InitialStageKey = "route",
            StageOrder = ["route", "events", "arrival"],
            Stages =
            [
                new JourneyStageDefinition { StageKey = "route", DisplayName = "Route", CompletionModel = JourneyStageCompletionModel.Explicit },
                new JourneyStageDefinition { StageKey = "events", DisplayName = "Events", CompletionModel = JourneyStageCompletionModel.Explicit },
                new JourneyStageDefinition { StageKey = "arrival", DisplayName = "Arrival", CompletionModel = JourneyStageCompletionModel.Explicit }
            ],
            DestinationReference = "refuge-A",
            RouteReference = "route-A"
        };
        var processId = Guid.NewGuid();
        var journey = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            definition,
            policy.ToExecutionSnapshot(),
            new JourneyClockReference(TimeSpan.Zero, 0),
            party,
            new ExpeditionConsequenceProvenance(ExpeditionConsequenceSourceKind.Dm, "persistence-test")).State;
        var resolutionId = Guid.NewGuid();
        journey = JourneyProcessEngine.Resolve(
            journey,
            new JourneyProcessResolutionInput
            {
                ResolutionId = resolutionId,
                ProcessId = processId,
                StageKey = "route",
                RoleKey = "guide",
                ProgressDelta = 2,
                Provenance = new ExpeditionConsequenceProvenance(ExpeditionConsequenceSourceKind.Dm, "guide-progress")
            },
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            party).State;

        var runtime = new NonSpatialSessionState
        {
            Id = Guid.NewGuid(),
            ElapsedTime = TimeSpan.FromHours(1),
            CompletedWatches = 0
        };
        var now = DateTimeOffset.UtcNow;
        var created = await store.CreateExpeditionAsync(new StoredExpedition(
            "One Ring persistence",
            runtime,
            new NonSpatialCrawlSessionContext("Mapless journey"),
            null,
            procedure,
            null,
            TimeSpan.Zero,
            "owner",
            1,
            now,
            now)
        {
            Party = party,
            Journey = journey
        });

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();
        var loaded = await restarted.GetExpeditionAsync(created.Id, "owner");

        Assert.NotNull(loaded);
        Assert.Equal(processId, Assert.Single(loaded!.Journey.ActiveProcesses).Id);
        Assert.Equal(2, loaded.Journey.ActiveProcesses.Single().StageStates.Single(value => value.StageKey == "route").NumericProgress);
        Assert.Contains(resolutionId, loaded.Journey.ConsumedResolutionIds);
        Assert.Equal(guideId, Assert.Single(loaded.Journey.Resolutions).Actor!.ParticipantId);
        Assert.IsType<NonSpatialSessionState>(loaded.Runtime);
    }

    [Fact]
    public async Task JourneyAggregateRejectsDanglingLiveParticipantBeforePersistence()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var participant = Guid.NewGuid();
        var party = new CrawlPartySheet { Members = [new CrawlPartyMember(participant, "Participant")] };
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "custom",
            DisplayName = "Custom",
            InitialStageKey = "one",
            StageOrder = ["one"],
            Stages = [new JourneyStageDefinition { StageKey = "one", DisplayName = "One", CompletionModel = JourneyStageCompletionModel.Explicit }]
        };
        var execution = new JourneyProcessExecutionSnapshot
        {
            StageModel = "manual-stages",
            StageTransitionModel = JourneyStageTransitionModel.Explicit,
            ProgressModel = "progress-points",
            ProgressKind = JourneyProgressValueKind.Numeric,
            ProgressUnit = "points",
            CompletionModel = "explicit-completion",
            RoleAssignmentModel = JourneyRoleAssignmentModel.CurrentAtResolution,
            IntervalIntegrationModel = JourneyIntervalIntegrationModel.None,
            MechanicKey = GenericProcedureCatalog.MultiStageExpeditionProcessMechanic,
            MechanicVersion = 1,
            ExecutionHandler = GenericProcedureExecutionHandlers.DeclarativeContract
        };
        var processId = Guid.NewGuid();
        var started = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty, processId, definition, execution,
            new JourneyClockReference(TimeSpan.Zero, 0), party,
            new ExpeditionConsequenceProvenance(ExpeditionConsequenceSourceKind.Dm, "test")).State;
        var process = started.ActiveProcesses.Single() with
        {
            Status = JourneyProcessStatus.ResolutionRequired,
            PendingActions =
            [
                new JourneyPendingAction
                {
                    Id = Guid.NewGuid(), Kind = JourneyPendingActionKind.ProcessResolution,
                    StageKey = "one", ParticipantId = participant
                }
            ]
        };
        var invalidJourney = started with { ActiveProcesses = [process] };
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var runtime = new NonSpatialSessionState { Id = Guid.NewGuid() };
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "invalid", runtime, new NonSpatialCrawlSessionContext("test"), null, procedure,
            null, TimeSpan.Zero, "owner", 1, now, now)
        {
            Party = CrawlPartySheet.Empty,
            Journey = invalidJourney
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateExpeditionAsync(expedition));
    }
}