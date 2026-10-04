using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class Phase12JourneyInvariantTests
{
    private static readonly ExpeditionConsequenceProvenance Dm = new(
        ExpeditionConsequenceSourceKind.Dm,
        "phase-12-invariant-test");

    [Fact]
    public void ConsumedResolutionIdCanNotBeReusedForDifferentProcessOrStage()
    {
        var party = CrawlPartySheet.Empty;
        var processId = Guid.NewGuid();
        var started = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            processId,
            Definition(),
            Execution(),
            new JourneyClockReference(TimeSpan.Zero, 0),
            party,
            Dm).State;
        var resolutionId = Guid.NewGuid();
        var resolved = JourneyProcessEngine.Resolve(
            started,
            new JourneyProcessResolutionInput
            {
                ResolutionId = resolutionId,
                ProcessId = processId,
                StageKey = "stage",
                ProgressDelta = 1,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            party).State;

        var differentProcess = Assert.Throws<InvalidOperationException>(() => JourneyProcessEngine.Resolve(
            resolved,
            new JourneyProcessResolutionInput
            {
                ResolutionId = resolutionId,
                ProcessId = Guid.NewGuid(),
                StageKey = "stage",
                ProgressDelta = 1,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            party));
        Assert.Contains("already consumed", differentProcess.Message, StringComparison.OrdinalIgnoreCase);

        var differentStage = Assert.Throws<InvalidOperationException>(() => JourneyProcessEngine.Resolve(
            resolved,
            new JourneyProcessResolutionInput
            {
                ResolutionId = resolutionId,
                ProcessId = processId,
                StageKey = "other-stage",
                ProgressDelta = 1,
                Provenance = Dm
            },
            new JourneyClockReference(TimeSpan.FromHours(1), 0),
            party));
        Assert.Contains("already consumed", differentStage.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerminalProcessRequiresBothEndReferencesAndTheyCanNotPrecedeStart()
    {
        var party = CrawlPartySheet.Empty;
        var process = JourneyProcessEngine.Start(
            ExpeditionJourneyState.Empty,
            Guid.NewGuid(),
            Definition(),
            Execution(),
            new JourneyClockReference(TimeSpan.FromHours(2), 3),
            party,
            Dm).Process;

        Assert.Throws<InvalidOperationException>(() => (process with
        {
            Status = JourneyProcessStatus.Completed,
            EndedAtExpeditionTime = TimeSpan.FromHours(3),
            EndReason = "done"
        }).Validate(party));

        Assert.Throws<InvalidOperationException>(() => (process with
        {
            Status = JourneyProcessStatus.Completed,
            EndedAtExpeditionTime = TimeSpan.FromHours(1),
            EndedAfterCompletedWatches = 2,
            EndReason = "done"
        }).Validate(party));
    }

    [Fact]
    public void JourneyHistoryRejectsEmptyOptionalIdentifiers()
    {
        var history = new JourneyHistoryRecord
        {
            Id = Guid.NewGuid(),
            Kind = JourneyHistoryKind.ProcessStarted,
            ProcessId = Guid.Empty,
            ExpeditionTime = TimeSpan.Zero,
            CompletedWatches = 0,
            Detail = "invalid persisted history",
            Provenance = Dm
        };

        Assert.Throws<InvalidOperationException>(history.Validate);
    }

    private static JourneyProcessDefinition Definition() => new()
    {
        ProcessKey = "invariant-test",
        DisplayName = "Invariant test",
        InitialStageKey = "stage",
        StageOrder = ["stage"],
        Stages =
        [
            new JourneyStageDefinition
            {
                StageKey = "stage",
                DisplayName = "Stage"
            }
        ]
    };

    private static JourneyProcessExecutionSnapshot Execution() => new()
    {
        StageModel = "single-stage",
        StageTransitionModel = JourneyStageTransitionModel.Explicit,
        ProgressModel = "resolved-progress",
        ProgressKind = JourneyProgressValueKind.Numeric,
        ProgressUnit = "progress",
        CompletionModel = "explicit-completion",
        RoleDriven = false,
        RoleAssignmentModel = JourneyRoleAssignmentModel.CurrentAtResolution,
        IntervalIntegrationModel = JourneyIntervalIntegrationModel.None,
        MechanicKey = "test.journey-process",
        MechanicVersion = 1,
        ExecutionHandler = "procedure.declarative-contract"
    };
}
