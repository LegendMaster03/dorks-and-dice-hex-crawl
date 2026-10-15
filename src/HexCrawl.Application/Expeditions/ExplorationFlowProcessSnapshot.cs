using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

/// <summary>
/// Materializes the reusable journey-process representation of a pinned
/// exploration flow. The same projection is used to start new process state
/// and to validate persisted state against the immutable procedure snapshot.
/// </summary>
internal static class ExplorationFlowProcessSnapshot
{
    public static (JourneyProcessDefinition Definition,
        JourneyProcessExecutionSnapshot Execution) From(CampaignProcedure procedure)
    {
        var runtime = GenericProcedureRuntime.Bind(procedure);
        var flow = runtime.ExplorationFlow
            ?? throw new InvalidOperationException(
                "The pinned procedure has no executable exploration flow.");
        var selected = procedure.Modules.Single(module =>
            module.Mechanic.ExecutionHandler ==
                GenericProcedureExecutionHandlers.ExplorationFlowPolicy);
        var definition = new JourneyProcessDefinition
        {
            ProcessKey = "exploration-flow",
            DisplayName = "Exploration flow",
            InitialStageKey = flow.StageKeys[0],
            StageOrder = flow.StageKeys,
            Stages = flow.StageKeys.Select(key => new JourneyStageDefinition
            {
                StageKey = key,
                DisplayName = Humanize(key),
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
            RoleDriven = false,
            IntervalIntegrationModel = flow.WatchCompletionStageKey is null
                ? JourneyIntervalIntegrationModel.None
                : JourneyIntervalIntegrationModel.CompletedWatchResolutionOpportunity,
            BlocksRelevantTravelWhileResolutionRequired = flow.WatchCompletionStageKey is not null,
            MechanicKey = selected.Mechanic.Key,
            MechanicVersion = selected.Mechanic.Version,
            ExecutionHandler = selected.Mechanic.ExecutionHandler
        };
        definition.Validate();
        execution.Validate();
        return (definition, execution);
    }

    public static void Validate(
        CampaignProcedure procedure, JourneyProcessInstance process)
    {
        var expected = From(procedure);
        if (process.Execution != expected.Execution
            || !process.Definition.StageOrder.SequenceEqual(
                expected.Definition.StageOrder, StringComparer.Ordinal)
            || process.Definition.Stages.Count != expected.Definition.Stages.Count
            || process.Definition.Stages.Where((stage, index) =>
                stage.StageKey != expected.Definition.Stages[index].StageKey
                || stage.CompletionModel != JourneyStageCompletionModel.Explicit
                || stage.InitialProgressState != "unresolved").Any()
            || process.Definition.InitialStageKey != expected.Definition.InitialStageKey)
            throw new InvalidOperationException(
                $"Exploration flow '{process.Id:D}' does not match its exact pinned procedure.");
    }

    private static string Humanize(string key)
    {
        var value = key.Replace('-', ' ').Replace('_', ' ').Trim();
        return value.Length == 0 ? key : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
