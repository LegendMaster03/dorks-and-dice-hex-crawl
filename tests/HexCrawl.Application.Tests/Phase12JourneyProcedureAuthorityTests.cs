using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class Phase12JourneyProcedureAuthorityTests
{
    private static readonly ExpeditionConsequenceProvenance Dm = new(
        ExpeditionConsequenceSourceKind.Dm,
        "phase-12-authority-test");

    [Fact]
    public void PersistedProcessExecutionMustMatchExactPinnedProcedure()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey)
            .MaterializeGeneric().Procedure;
        var policy = JourneyProcedurePolicyResolver.ResolveProcess(procedure);
        var processId = Guid.NewGuid();
        var state = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            OneRingDefinition(),
            policy.ToExecutionSnapshot(),
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty,
            Dm).State;

        JourneyAggregateProcedureValidator.Validate(procedure, state);

        var process = Assert.Single(state.ActiveProcesses);
        var tampered = state with
        {
            ActiveProcesses =
            [
                process with
                {
                    Execution = process.Execution with { ProgressUnit = "hours" }
                }
            ]
        };
        Assert.Throws<InvalidOperationException>(() =>
            JourneyAggregateProcedureValidator.Validate(procedure, tampered));
    }

    [Fact]
    public void HistoricalRoleSnapshotRequiresConcretePhase7AssignmentIdentity()
    {
        var procedure = CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.OneRing2ePresetKey)
            .MaterializeGeneric().Procedure;
        var policy = JourneyProcedurePolicyResolver.ResolveProcess(procedure);
        var processId = Guid.NewGuid();
        var state = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            OneRingDefinition(),
            policy.ToExecutionSnapshot(),
            new JourneyClockReference(TimeSpan.Zero, 0),
            CrawlPartySheet.Empty,
            Dm).State;
        var resolutionId = Guid.NewGuid();
        state = state with
        {
            Resolutions =
            [
                new JourneyResolutionRecord
                {
                    ResolutionId = resolutionId,
                    ProcessId = processId,
                    StageKey = "route",
                    Actor = new JourneyParticipantSnapshot(Guid.NewGuid(), "Guide", "guide"),
                    ProgressBefore = 0,
                    ProgressAfter = 1,
                    Provenance = Dm
                }
            ],
            ConsumedResolutionIds = [resolutionId]
        };

        Assert.Throws<InvalidOperationException>(() =>
            JourneyAggregateProcedureValidator.Validate(procedure, state));
    }

    private static JourneyProcessDefinition OneRingDefinition() => new()
    {
        ProcessKey = "route-to-refuge",
        DisplayName = "Route to refuge",
        InitialStageKey = "route",
        StageOrder = ["route", "events", "arrival"],
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
                StageKey = "events",
                DisplayName = "Events",
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
}
