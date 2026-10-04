using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Web.Modules.Expeditions;

public sealed record JourneyProcessPolicyContract(
    JourneyPolicySupport Support,
    string? StageModel,
    IReadOnlyList<string> StageKeys,
    JourneyStageTransitionModel? StageTransitionModel,
    string? ProgressModel,
    JourneyProgressValueKind? ProgressKind,
    string? ProgressUnit,
    bool AllowNegativeProgress,
    double? ProgressFloor,
    double? ProgressCeiling,
    string? CompletionModel,
    bool RoleDriven,
    JourneyRoleAssignmentModel? RoleAssignmentModel,
    JourneyIntervalIntegrationModel? IntervalIntegrationModel,
    bool BlocksRelevantTravelWhileResolutionRequired,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static JourneyProcessPolicyContract From(JourneyProcessPolicy policy) => new(
        policy.Support,
        policy.StageModel,
        policy.StageKeys,
        policy.StageTransitionModel,
        policy.ProgressModel,
        policy.ProgressKind,
        policy.ProgressUnit,
        policy.AllowNegativeProgress,
        policy.ProgressFloor,
        policy.ProgressCeiling,
        policy.CompletionModel,
        policy.RoleDriven,
        policy.RoleAssignmentModel,
        policy.IntervalIntegrationModel,
        policy.BlocksRelevantTravelWhileResolutionRequired,
        policy.MechanicKey,
        policy.MechanicVersion,
        policy.ExecutionHandler,
        policy.UnsupportedReason);
}

public sealed record JourneyEventPolicyContract(
    JourneyPolicySupport Support,
    string? TriggerModel,
    IReadOnlyList<JourneyEventTriggerKind> TriggerSources,
    JourneyEventLinkMode? LinkMode,
    string? TargetingModel,
    string? TerrainInfluence,
    string? ConsequenceModel,
    bool RequiresResolvedTrigger,
    bool BlocksRelevantTravelWhileResolutionRequired,
    string? MechanicKey,
    int? MechanicVersion,
    string? ExecutionHandler,
    string? UnsupportedReason)
{
    public static JourneyEventPolicyContract From(JourneyEventPolicy policy) => new(
        policy.Support,
        policy.TriggerModel,
        policy.TriggerSources,
        policy.LinkMode,
        policy.TargetingModel,
        policy.TerrainInfluence,
        policy.ConsequenceModel,
        policy.RequiresResolvedTrigger,
        policy.BlocksRelevantTravelWhileResolutionRequired,
        policy.MechanicKey,
        policy.MechanicVersion,
        policy.ExecutionHandler,
        policy.UnsupportedReason);
}

public sealed record ExpeditionJourneyStateContract(
    long ExpeditionVersion,
    JourneyProcessPolicyContract ProcessPolicy,
    JourneyEventPolicyContract EventPolicy,
    IReadOnlyList<JourneyProcessInstance> ActiveProcesses,
    IReadOnlyList<JourneyProcessInstance> ClosedProcesses,
    IReadOnlyList<JourneyEventOccurrence> EventOccurrences,
    IReadOnlyList<JourneyResolutionRecord> Resolutions,
    IReadOnlyList<JourneyHistoryRecord> History)
{
    public static ExpeditionJourneyStateContract From(StoredExpedition expedition) => new(
        expedition.Version,
        JourneyProcessPolicyContract.From(JourneyProcedurePolicyResolver.ResolveProcess(expedition.CampaignProcedure)),
        JourneyEventPolicyContract.From(JourneyProcedurePolicyResolver.ResolveEvents(expedition.CampaignProcedure)),
        expedition.Journey.ActiveProcesses,
        expedition.Journey.ClosedProcesses,
        expedition.Journey.EventOccurrences,
        expedition.Journey.Resolutions,
        expedition.Journey.History);
}

public sealed record JourneyOperationContract(
    string Detail,
    bool StateChanged,
    Guid? ProcessId,
    Guid? EventOccurrenceId,
    ExpeditionJourneyStateContract State)
{
    public static JourneyOperationContract From(JourneyOperationResult result) => new(
        result.Detail,
        result.StateChanged,
        result.ProcessId,
        result.EventOccurrenceId,
        ExpeditionJourneyStateContract.From(result.Expedition));
}

public sealed record StartJourneyProcessRequest(
    long ExpectedVersion,
    Guid ProcessId,
    JourneyProcessDefinition? Definition,
    string? ProcessKey,
    string? DisplayName,
    string? Description,
    string? DestinationReference,
    string? RouteReference,
    string? LocationReference,
    ExpeditionConsequenceProvenance Provenance)
{
    public StartJourneyProcessCommand ToCommand() => new()
    {
        ExpectedVersion = ExpectedVersion,
        ProcessId = ProcessId,
        Definition = Definition,
        ProcessKey = ProcessKey,
        DisplayName = DisplayName,
        Description = Description,
        DestinationReference = DestinationReference,
        RouteReference = RouteReference,
        LocationReference = LocationReference,
        Provenance = Provenance
    };
}

public sealed record ResolveJourneyProcessRequest(
    long ExpectedVersion,
    JourneyProcessResolutionInput Resolution,
    bool CaptureCurrentEnvironment = true)
{
    public ResolveJourneyProcessCommand ToCommand(Guid processId)
    {
        if (Resolution.ProcessId != processId)
        {
            throw new InvalidOperationException("Journey process id in the resolution must match the route.");
        }
        return new(ExpectedVersion, Resolution, CaptureCurrentEnvironment);
    }
}

public sealed record CreateJourneyEventOpportunityRequest(
    long ExpectedVersion,
    JourneyEventOpportunityInput Opportunity,
    bool CaptureCurrentEnvironment = true)
{
    public CreateJourneyEventOpportunityCommand ToCommand() =>
        new(ExpectedVersion, Opportunity, CaptureCurrentEnvironment);
}

public sealed record ResolveJourneyEventRequest(
    long ExpectedVersion,
    JourneyEventResolutionInput Resolution,
    bool CaptureCurrentEnvironment = true)
{
    public ResolveJourneyEventCommand ToCommand(Guid occurrenceId)
    {
        if (Resolution.OccurrenceId != occurrenceId)
        {
            throw new InvalidOperationException("Journey event occurrence id in the resolution must match the route.");
        }
        return new(ExpectedVersion, Resolution, CaptureCurrentEnvironment);
    }
}

public sealed record CloseJourneyProcessRequest(
    long ExpectedVersion,
    string Reason,
    ExpeditionConsequenceProvenance Provenance)
{
    public CloseJourneyProcessCommand ToCommand(JourneyProcessStatus status) =>
        new(ExpectedVersion, status, Reason, Provenance);
}