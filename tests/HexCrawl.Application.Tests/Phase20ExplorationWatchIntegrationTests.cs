using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class Phase20ExplorationWatchIntegrationTests
{
    [Fact]
    public void WatchResolutionIsPinnedToTheConfiguredExplorationStageWithoutInventingProgress()
    {
        var procedure = CrawlProcedureCatalog.Resolve(
            CrawlProcedureCatalog.WastelandWandererPresetKey)
            .MaterializeGeneric().Procedure with
            {
                Key = "unrelated-generic-procedure",
                Name = "Generic staged travel"
            };
        var flow = GenericProcedureRuntime.Bind(procedure).ExplorationFlow!;
        Assert.Equal("travel", flow.WatchCompletionStageKey);
        var selected = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.ExplorationFlowModule);
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "exploration-flow",
            DisplayName = "Travel stages",
            InitialStageKey = flow.StageKeys[0],
            StageOrder = flow.StageKeys,
            Stages = flow.StageKeys.Select(key => new JourneyStageDefinition
            {
                StageKey = key,
                DisplayName = key,
                CompletionModel = JourneyStageCompletionModel.Explicit,
                InitialProgressState = "unresolved"
            }).ToArray()
        };
        var execution = new JourneyProcessExecutionSnapshot
        {
            StageModel = "ordered-exploration",
            StageTransitionModel = JourneyStageTransitionModel.Sequential,
            ProgressModel = "explicit-resolution",
            ProgressKind = JourneyProgressValueKind.ExplicitState,
            CompletionModel = "final-stage-completion",
            IntervalIntegrationModel =
                JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity,
            BlocksRelevantTravelWhileResolutionRequired = true,
            MechanicKey = selected.Mechanic.Key,
            MechanicVersion = selected.Mechanic.Version,
            ExecutionHandler = selected.Mechanic.ExecutionHandler
        };
        var provenance = new ExpeditionConsequenceProvenance(
            ExpeditionConsequenceSourceKind.Dm, "watch-stage-integration-test");
        var before = new NonSpatialSessionState { Id = Guid.NewGuid() };
        var after = before with { CompletedWatches = 1, ElapsedTime = TimeSpan.FromHours(1) };
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "Generic expedition", before, new NonSpatialCrawlSessionContext("Test clock"),
            null, procedure, null, TimeSpan.Zero, "test-owner", 1, now, now);
        var processId = Guid.NewGuid();
        var started = JourneyProcessEngine.Start(
            expedition.Journey, processId, definition, execution,
            JourneyClockReference.From(before), expedition.Party, provenance);
        expedition = expedition with { Journey = started.State };

        var observed = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition, before, after, started.State);
        var waiting = Assert.Single(observed.ActiveProcesses);
        var pending = Assert.Single(waiting.PendingActions);
        Assert.Equal(JourneyPendingActionKind.WatchResolution, pending.Kind);
        Assert.Equal(flow.WatchCompletionStageKey, pending.StageKey);
        Assert.Empty(observed.Resolutions);
        Assert.Equal(JourneyProcessStatus.ResolutionRequired, waiting.Status);
        Assert.Throws<InvalidOperationException>(() =>
            JourneyRuntimeIntegration.EnsureRelevantTravelAllowed(
                expedition with { Journey = observed }));
        Assert.Same(observed, JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition, after, after, observed));

        var resolved = JourneyProcessEngine.Resolve(
            observed, new JourneyProcessResolutionInput
            {
                ResolutionId = Guid.NewGuid(),
                ProcessId = processId,
                StageKey = pending.StageKey,
                PendingActionId = pending.Id,
                CompleteStage = true,
                Provenance = provenance
            }, JourneyClockReference.From(after), expedition.Party);
        Assert.Equal("encounter", resolved.Process.CurrentStageKey);
        Assert.Empty(resolved.Process.PendingActions);
        JourneyRuntimeIntegration.EnsureRelevantTravelAllowed(
            expedition with { Journey = resolved.State });

        // Later watches do not accidentally create travel-stage work while the
        // same process is resolving its encounter or journal stages.
        var afterSecond = after with
        {
            CompletedWatches = 2,
            ElapsedTime = TimeSpan.FromHours(2)
        };
        var later = JourneyRuntimeIntegration.ObserveCompletedWatches(
            expedition with { Journey = resolved.State }, after, afterSecond, resolved.State);
        Assert.Empty(Assert.Single(later.ActiveProcesses).PendingActions);
        Assert.Single(later.Resolutions);
    }
}
