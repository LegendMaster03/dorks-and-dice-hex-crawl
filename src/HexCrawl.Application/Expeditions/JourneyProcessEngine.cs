using System.Security.Cryptography;
using System.Text;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application;

public sealed record JourneyClockReference(TimeSpan ExpeditionTime, int CompletedWatches)
{
    public static JourneyClockReference From(CrawlSessionRuntimeState runtime) => runtime switch
    {
        ExpeditionState spatial => new(spatial.ElapsedTravelTime, spatial.CompletedWatches),
        NonSpatialSessionState nonSpatial => new(nonSpatial.ElapsedTime, nonSpatial.CompletedWatches),
        _ => throw new InvalidOperationException("Unsupported expedition runtime state for journey reference.")
    };
}

public sealed record JourneyProcessResolutionInput
{
    public required Guid ResolutionId { get; init; }
    public required Guid ProcessId { get; init; }
    public required string StageKey { get; init; }
    public Guid? PendingActionId { get; init; }
    public string? ApproachKey { get; init; }
    public Guid? ActorParticipantId { get; init; }
    public string? RoleKey { get; init; }
    public string? OutcomeKey { get; init; }
    public double? ProgressDelta { get; init; }
    public string? ProgressState { get; init; }
    public int SuccessDelta { get; init; }
    public int FailureDelta { get; init; }
    public int ComplicationDelta { get; init; }
    public bool CompleteStage { get; init; }
    public string? TargetStageKey { get; init; }
    public bool CompleteProcess { get; init; }
    public bool FailProcess { get; init; }
    public IReadOnlyList<ExpeditionConsequence> Consequences { get; init; } = [];
    public required ExpeditionConsequenceProvenance Provenance { get; init; }
}

public sealed record JourneyProcessTransitionResult(
    ExpeditionJourneyState State,
    JourneyProcessInstance Process,
    JourneyResolutionRecord? Resolution,
    bool StateChanged,
    bool ProgressChanged,
    bool StageChanged);

public static class JourneyProcessEngine
{
    public static JourneyProcessTransitionResult Start(
        ExpeditionJourneyState state,
        Guid processId,
        JourneyProcessDefinition definition,
        JourneyProcessExecutionSnapshot execution,
        JourneyClockReference clock,
        CrawlPartySheet party,
        ExpeditionConsequenceProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(party);
        definition.Validate();
        execution.Validate();
        provenance.Validate();
        if (processId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey process id is required.");
        }
        if (state.ActiveProcesses.Any(value => value.Id == processId))
        {
            throw new InvalidOperationException("A journey process with this id is already active.");
        }
        if (state.ClosedProcesses.Any(value => value.Id == processId))
        {
            throw new InvalidOperationException("A journey process with this id already exists in process history.");
        }

        var stageStates = definition.Stages.Select(stage => InitialStageState(stage, execution)).ToArray();
        var process = new JourneyProcessInstance
        {
            Id = processId,
            Status = JourneyProcessStatus.Active,
            Definition = definition,
            Execution = execution,
            CurrentStageKey = definition.InitialStageKey,
            StageStates = stageStates,
            StartedAtExpeditionTime = clock.ExpeditionTime,
            StartedAfterCompletedWatches = clock.CompletedWatches,
            Provenance = provenance
        };
        process.Validate(party);
        var updated = state with
        {
            ActiveProcesses = state.ActiveProcesses.Append(process).ToArray(),
            History = state.History.Append(History(
                JourneyHistoryKind.ProcessStarted,
                clock,
                provenance,
                $"Started journey process '{definition.ProcessKey}' at stage '{definition.InitialStageKey}'.",
                processId,
                definition.InitialStageKey)).ToArray()
        };
        updated.Validate(party);
        return new(updated, process, null, true, false, false);
    }

    public static JourneyProcessTransitionResult Resolve(
        ExpeditionJourneyState state,
        JourneyProcessResolutionInput input,
        JourneyClockReference clock,
        CrawlPartySheet party)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(party);
        input.Provenance.Validate();
        if (input.ResolutionId == Guid.Empty || input.ProcessId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey resolution and process ids are required.");
        }
        if (state.ConsumedResolutionIds.Contains(input.ResolutionId))
        {
            var prior = state.Resolutions.Single(value => value.ResolutionId == input.ResolutionId);
            if (prior.ProcessId != input.ProcessId
                || !string.Equals(prior.StageKey, input.StageKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Journey resolution id is already consumed by a different process or stage resolution.");
            }
            var retained = state.ActiveProcesses.Concat(state.ClosedProcesses).Single(value => value.Id == prior.ProcessId);
            return new(state, retained, prior, false,
                prior.ProgressBefore != prior.ProgressAfter || !string.Equals(prior.StateBefore, prior.StateAfter, StringComparison.Ordinal),
                prior.TransitionToStageKey is not null);
        }

        var process = state.ActiveProcesses.SingleOrDefault(value => value.Id == input.ProcessId)
            ?? throw new InvalidOperationException("The requested journey process is not active.");
        if (process.Status is not (JourneyProcessStatus.Active or JourneyProcessStatus.ResolutionRequired))
        {
            throw new InvalidOperationException("Only an active journey process can accept a stage resolution.");
        }
        if (!string.Equals(process.CurrentStageKey, input.StageKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Journey resolution must target the process current stage.");
        }

        var definition = process.Definition.Stages.Single(value => string.Equals(value.StageKey, input.StageKey, StringComparison.Ordinal));
        var stageState = process.StageStates.Single(value => string.Equals(value.StageKey, input.StageKey, StringComparison.Ordinal));
        ValidateApproach(definition, input.ApproachKey);
        var actor = ResolveActor(process, definition, input, party);
        var pending = process.PendingActions.ToList();
        if (input.PendingActionId.HasValue)
        {
            var action = pending.SingleOrDefault(value => value.Id == input.PendingActionId.Value)
                ?? throw new InvalidOperationException("The supplied journey pending action is not active for this process.");
            if (!string.Equals(action.StageKey, input.StageKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Journey pending action belongs to a different stage.");
            }
            pending.Remove(action);
        }

        ValidateCounterDelta(stageState.Successes, input.SuccessDelta, "success");
        ValidateCounterDelta(stageState.Failures, input.FailureDelta, "failure");
        ValidateCounterDelta(stageState.Complications, input.ComplicationDelta, "complication");
        var beforeNumeric = stageState.NumericProgress;
        var beforeState = stageState.ExplicitState;
        var nextStageState = ApplyProgress(process.Execution, stageState, input);
        nextStageState = nextStageState with
        {
            Successes = checked(nextStageState.Successes + input.SuccessDelta),
            Failures = checked(nextStageState.Failures + input.FailureDelta),
            Complications = checked(nextStageState.Complications + input.ComplicationDelta)
        };

        var processFailed = input.FailProcess
            || definition.FailProcessAtFailureLimit && definition.FailureLimit.HasValue && nextStageState.Failures >= definition.FailureLimit.Value
            || definition.FailProcessAtComplicationLimit && definition.ComplicationLimit.HasValue && nextStageState.Complications >= definition.ComplicationLimit.Value;
        var stageComplete = !processFailed && IsStageComplete(definition, nextStageState, input.CompleteStage);
        nextStageState = nextStageState with { Completed = stageComplete };

        var stageStates = process.StageStates.Select(value =>
            string.Equals(value.StageKey, input.StageKey, StringComparison.Ordinal) ? nextStageState : value).ToArray();
        var currentStageKey = process.CurrentStageKey;
        string? transitionTarget = null;
        var completeProcess = input.CompleteProcess;
        if (!processFailed && stageComplete && !completeProcess)
        {
            transitionTarget = ResolveTransitionTarget(process, definition, input);
            if (transitionTarget is null)
            {
                var last = IsLastStage(process.Definition, definition.StageKey);
                if (last && string.Equals(process.Execution.CompletionModel, "final-stage-completion", StringComparison.Ordinal))
                {
                    completeProcess = true;
                }
                else if (!last || process.Execution.StageTransitionModel != JourneyStageTransitionModel.Explicit)
                {
                    pending.Add(new JourneyPendingAction
                    {
                        Id = DeterministicId(input.ResolutionId, "stage-transition"),
                        Kind = JourneyPendingActionKind.StageTransition,
                        StageKey = input.StageKey,
                        SourceReference = input.ResolutionId.ToString("D"),
                        Detail = last
                            ? "The final stage is complete; explicit process completion remains required by the pinned policy."
                            : "The stage is complete but the exact transition target remains unresolved."
                    });
                }
            }
            else
            {
                currentStageKey = transitionTarget;
            }
        }

        if (completeProcess && processFailed)
        {
            throw new InvalidOperationException("A journey process resolution can not complete and fail the process simultaneously.");
        }
        if (input.CompleteProcess
            && !string.Equals(process.Execution.CompletionModel, "explicit-completion", StringComparison.Ordinal)
            && !string.Equals(process.Execution.CompletionModel, "reach-destination", StringComparison.Ordinal)
            && !string.Equals(process.Execution.CompletionModel, "final-stage-completion", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The exact pinned completion model does not support explicit process completion.");
        }

        var terminalStatus = processFailed ? JourneyProcessStatus.Failed
            : completeProcess ? JourneyProcessStatus.Completed
            : (JourneyProcessStatus?)null;
        var nextProcess = process with
        {
            Status = terminalStatus ?? (pending.Count > 0 ? JourneyProcessStatus.ResolutionRequired : JourneyProcessStatus.Active),
            CurrentStageKey = currentStageKey,
            StageStates = stageStates,
            PendingActions = terminalStatus.HasValue ? [] : pending,
            EndedAtExpeditionTime = terminalStatus.HasValue ? clock.ExpeditionTime : null,
            EndedAfterCompletedWatches = terminalStatus.HasValue ? clock.CompletedWatches : null,
            EndReason = terminalStatus switch
            {
                JourneyProcessStatus.Completed => "Process completed by the resolved journey operation and pinned completion policy.",
                JourneyProcessStatus.Failed => "Process failed by explicit resolution or a configured stage failure/complication limit.",
                _ => null
            }
        };
        nextProcess.Validate(party);

        var resolution = new JourneyResolutionRecord
        {
            ResolutionId = input.ResolutionId,
            ProcessId = process.Id,
            StageKey = input.StageKey,
            ApproachKey = input.ApproachKey,
            OutcomeKey = input.OutcomeKey,
            Actor = actor,
            ProgressBefore = beforeNumeric,
            ProgressAfter = nextStageState.NumericProgress,
            StateBefore = beforeState,
            StateAfter = nextStageState.ExplicitState,
            SuccessDelta = input.SuccessDelta,
            FailureDelta = input.FailureDelta,
            ComplicationDelta = input.ComplicationDelta,
            TransitionFromStageKey = transitionTarget is null ? null : input.StageKey,
            TransitionToStageKey = transitionTarget,
            ConsequenceIds = input.Consequences.Select(value => value.Id).ToArray(),
            Provenance = input.Provenance
        };
        resolution.Validate();

        var active = state.ActiveProcesses.Where(value => value.Id != process.Id).ToList();
        var closed = state.ClosedProcesses.ToList();
        if (terminalStatus.HasValue) closed.Add(nextProcess); else active.Add(nextProcess);
        var history = state.History.ToList();
        history.Add(History(
            JourneyHistoryKind.ResolutionRecorded,
            clock,
            input.Provenance,
            $"Recorded resolution '{input.ResolutionId:D}' for stage '{input.StageKey}'.",
            process.Id,
            input.StageKey,
            input.ResolutionId));
        var progressChanged = beforeNumeric != nextStageState.NumericProgress
            || !string.Equals(beforeState, nextStageState.ExplicitState, StringComparison.Ordinal);
        if (progressChanged)
        {
            history.Add(History(
                JourneyHistoryKind.ProgressChanged,
                clock,
                input.Provenance,
                "Journey stage progress changed by explicit resolved input.",
                process.Id,
                input.StageKey,
                input.ResolutionId));
        }
        if (input.FailureDelta != 0)
        {
            history.Add(History(JourneyHistoryKind.FailureChanged, clock, input.Provenance,
                $"Journey stage failure count changed by {input.FailureDelta}.", process.Id, input.StageKey, input.ResolutionId));
        }
        if (input.ComplicationDelta != 0)
        {
            history.Add(History(JourneyHistoryKind.ComplicationChanged, clock, input.Provenance,
                $"Journey stage complication count changed by {input.ComplicationDelta}.", process.Id, input.StageKey, input.ResolutionId));
        }
        if (transitionTarget is not null)
        {
            history.Add(History(JourneyHistoryKind.StageTransitioned, clock, input.Provenance,
                $"Journey stage transitioned from '{input.StageKey}' to '{transitionTarget}'.",
                process.Id, transitionTarget, input.ResolutionId));
        }
        if (terminalStatus == JourneyProcessStatus.Completed)
        {
            history.Add(History(JourneyHistoryKind.ProcessCompleted, clock, input.Provenance,
                "Journey process completed.", process.Id, input.StageKey, input.ResolutionId));
        }
        else if (terminalStatus == JourneyProcessStatus.Failed)
        {
            history.Add(History(JourneyHistoryKind.ProcessFailed, clock, input.Provenance,
                "Journey process failed.", process.Id, input.StageKey, input.ResolutionId));
        }

        var updated = state with
        {
            ActiveProcesses = active,
            ClosedProcesses = closed,
            Resolutions = state.Resolutions.Append(resolution).ToArray(),
            ConsumedResolutionIds = state.ConsumedResolutionIds.Append(input.ResolutionId).ToArray(),
            History = history
        };
        updated.Validate(party);
        return new(updated, nextProcess, resolution, true, progressChanged, transitionTarget is not null);
    }

    public static ExpeditionJourneyState Close(
        ExpeditionJourneyState state,
        Guid processId,
        JourneyProcessStatus terminalStatus,
        string reason,
        JourneyClockReference clock,
        CrawlPartySheet party,
        ExpeditionConsequenceProvenance provenance)
    {
        if (terminalStatus is not (JourneyProcessStatus.Completed or JourneyProcessStatus.Failed or JourneyProcessStatus.Abandoned))
        {
            throw new InvalidOperationException("Journey process close operation requires Completed, Failed, or Abandoned status.");
        }
        reason = RequiredText(reason, "Journey process close reason", 2000);
        provenance.Validate();
        var process = state.ActiveProcesses.SingleOrDefault(value => value.Id == processId)
            ?? throw new InvalidOperationException("The requested journey process is not active.");
        var closed = process with
        {
            Status = terminalStatus,
            PendingActions = [],
            EndedAtExpeditionTime = clock.ExpeditionTime,
            EndedAfterCompletedWatches = clock.CompletedWatches,
            EndReason = reason
        };
        closed.Validate(party);
        var kind = terminalStatus switch
        {
            JourneyProcessStatus.Completed => JourneyHistoryKind.ProcessCompleted,
            JourneyProcessStatus.Failed => JourneyHistoryKind.ProcessFailed,
            JourneyProcessStatus.Abandoned => JourneyHistoryKind.ProcessAbandoned,
            _ => throw new InvalidOperationException()
        };
        var updated = state with
        {
            ActiveProcesses = state.ActiveProcesses.Where(value => value.Id != processId).ToArray(),
            ClosedProcesses = state.ClosedProcesses.Append(closed).ToArray(),
            History = state.History.Append(History(kind, clock, provenance, reason, processId, process.CurrentStageKey)).ToArray()
        };
        updated.Validate(party);
        return updated;
    }

    public static JourneyParticipantSnapshot? ResolveParticipantSnapshot(
        CrawlPartySheet party,
        Guid? participantId,
        string? roleKey,
        bool requireRole)
    {
        if (roleKey is not null && string.IsNullOrWhiteSpace(roleKey))
        {
            throw new InvalidOperationException("Journey role key can not be blank.");
        }
        if (requireRole && string.IsNullOrWhiteSpace(roleKey))
        {
            throw new InvalidOperationException("The pinned role-driven journey policy requires an explicit role for this resolution.");
        }

        ParticipantActivityAssignment? assignment = null;
        if (!string.IsNullOrWhiteSpace(roleKey))
        {
            var matches = party.ActivityAssignments.Where(value =>
                value.Scope == ParticipantActivityAssignmentScope.Role
                && string.Equals(value.RoleKey, roleKey, StringComparison.Ordinal)).ToArray();
            if (participantId.HasValue)
            {
                matches = matches.Where(value => value.ParticipantId == participantId).ToArray();
                if (matches.Length == 0)
                {
                    throw new InvalidOperationException("The selected participant does not currently hold the resolved journey role.");
                }
            }
            else if (matches.Select(value => value.ParticipantId).Distinct().Count() > 1)
            {
                throw new InvalidOperationException("More than one participant currently holds this journey role; choose the participant explicitly.");
            }
            assignment = matches.Length switch
            {
                0 => throw new InvalidOperationException("No current participant assignment exists for the resolved journey role."),
                1 => matches[0],
                _ => matches.OrderBy(value => value.Id).First()
            };
            participantId ??= assignment.ParticipantId;
        }

        if (!participantId.HasValue) return null;
        var member = party.Members.SingleOrDefault(value => value.Id == participantId.Value)
            ?? throw new InvalidOperationException("Journey resolution references a participant that does not exist.");
        return new JourneyParticipantSnapshot(member.Id, member.Name, roleKey?.Trim(), assignment?.Id);
    }

    public static Guid DeterministicId(Guid sourceId, string discriminator)
    {
        discriminator = RequiredText(discriminator, "Journey deterministic-id discriminator", 200);
        var source = Encoding.UTF8.GetBytes($"{sourceId:D}:{discriminator}");
        var hash = SHA256.HashData(source);
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }

    private static JourneyParticipantSnapshot? ResolveActor(
        JourneyProcessInstance process,
        JourneyStageDefinition stage,
        JourneyProcessResolutionInput input,
        CrawlPartySheet party)
    {
        if (input.RoleKey is not null && stage.RoleKeys.Count > 0 && !stage.RoleKeys.Contains(input.RoleKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The selected journey role is not available for the current stage.");
        }
        return ResolveParticipantSnapshot(
            party,
            input.ActorParticipantId,
            input.RoleKey,
            process.Execution.RoleDriven);
    }

    private static void ValidateApproach(JourneyStageDefinition stage, string? approachKey)
    {
        if (approachKey is null)
        {
            if (stage.Approaches.Count > 0)
            {
                throw new InvalidOperationException("The current journey stage requires an explicit approach selection.");
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(approachKey)
            || !stage.Approaches.Any(value => string.Equals(value.ApproachKey, approachKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The selected journey approach is not available for the current stage.");
        }
    }

    private static JourneyStageState ApplyProgress(
        JourneyProcessExecutionSnapshot execution,
        JourneyStageState state,
        JourneyProcessResolutionInput input)
    {
        if (execution.ProgressKind == JourneyProgressValueKind.Numeric)
        {
            if (input.ProgressState is not null)
            {
                throw new InvalidOperationException("Numeric journey progress can not accept an explicit-state value.");
            }
            if (!input.ProgressDelta.HasValue) return state;
            if (!double.IsFinite(input.ProgressDelta.Value))
            {
                throw new InvalidOperationException("Journey progress delta must be finite.");
            }
            var next = state.NumericProgress!.Value + input.ProgressDelta.Value;
            if (!double.IsFinite(next))
            {
                throw new InvalidOperationException("Journey progress transition exceeds the supported numeric range.");
            }
            if (!execution.AllowNegativeProgress) next = Math.Max(0, next);
            if (execution.ProgressFloor.HasValue) next = Math.Max(execution.ProgressFloor.Value, next);
            if (execution.ProgressCeiling.HasValue) next = Math.Min(execution.ProgressCeiling.Value, next);
            return state with { NumericProgress = next };
        }

        if (input.ProgressDelta.HasValue)
        {
            throw new InvalidOperationException("Explicit-state journey progress can not accept a numeric delta.");
        }
        return input.ProgressState is null
            ? state
            : state with { ExplicitState = RequiredText(input.ProgressState, "Journey explicit progress state") };
    }

    private static JourneyStageState InitialStageState(
        JourneyStageDefinition stage,
        JourneyProcessExecutionSnapshot execution)
    {
        if (execution.ProgressKind == JourneyProgressValueKind.Numeric)
        {
            var initial = execution.ProgressFloor ?? 0;
            if (!execution.AllowNegativeProgress) initial = Math.Max(0, initial);
            return new JourneyStageState { StageKey = stage.StageKey, NumericProgress = initial };
        }
        return new JourneyStageState
        {
            StageKey = stage.StageKey,
            ExplicitState = RequiredText(stage.InitialProgressState, $"Initial explicit progress state for stage '{stage.StageKey}'")
        };
    }

    private static bool IsStageComplete(
        JourneyStageDefinition definition,
        JourneyStageState state,
        bool explicitCompletion) => definition.CompletionModel switch
    {
        JourneyStageCompletionModel.Explicit => explicitCompletion,
        JourneyStageCompletionModel.ProgressThreshold => state.NumericProgress >= definition.ProgressTarget,
        JourneyStageCompletionModel.SuccessCount => state.Successes >= definition.SuccessTarget,
        JourneyStageCompletionModel.ResolutionSelected => explicitCompletion,
        _ => throw new InvalidOperationException("Journey stage completion model is not supported.")
    };

    private static string? ResolveTransitionTarget(
        JourneyProcessInstance process,
        JourneyStageDefinition stage,
        JourneyProcessResolutionInput input)
    {
        if (input.TargetStageKey is { } explicitTarget)
        {
            if (!process.Definition.Stages.Any(value => string.Equals(value.StageKey, explicitTarget, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Journey resolution selected a stage that does not exist.");
            }
            if (process.Execution.StageTransitionModel == JourneyStageTransitionModel.Sequential)
            {
                var expected = NextSequential(process.Definition, stage.StageKey);
                if (!string.Equals(expected, explicitTarget, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A sequential journey process can transition only to its explicitly ordered next stage.");
                }
            }
            else if (process.Execution.StageTransitionModel == JourneyStageTransitionModel.OutcomeSelected)
            {
                var mapped = OutcomeTarget(stage, input.OutcomeKey);
                if (!string.Equals(mapped, explicitTarget, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Outcome-selected journey transition target does not match the stage outcome mapping.");
                }
            }
            return explicitTarget;
        }

        return process.Execution.StageTransitionModel switch
        {
            JourneyStageTransitionModel.Sequential => NextSequential(process.Definition, stage.StageKey),
            JourneyStageTransitionModel.Explicit => stage.ExplicitNextStageKey,
            JourneyStageTransitionModel.OutcomeSelected => OutcomeTarget(stage, input.OutcomeKey),
            _ => throw new InvalidOperationException("Journey transition model is not supported.")
        };
    }

    private static string? NextSequential(JourneyProcessDefinition definition, string stageKey)
    {
        var index = definition.StageOrder.ToList().FindIndex(value => string.Equals(value, stageKey, StringComparison.Ordinal));
        if (index < 0) throw new InvalidOperationException("Current stage is missing from explicit stage order.");
        return index + 1 < definition.StageOrder.Count ? definition.StageOrder[index + 1] : null;
    }

    private static string? OutcomeTarget(JourneyStageDefinition stage, string? outcomeKey)
    {
        if (string.IsNullOrWhiteSpace(outcomeKey)) return null;
        return stage.OutcomeTransitions.SingleOrDefault(value => string.Equals(value.OutcomeKey, outcomeKey, StringComparison.Ordinal))?.TargetStageKey;
    }

    private static bool IsLastStage(JourneyProcessDefinition definition, string stageKey) =>
        string.Equals(definition.StageOrder[^1], stageKey, StringComparison.Ordinal);

    private static void ValidateCounterDelta(int current, int delta, string label)
    {
        if ((long)current + delta < 0)
        {
            throw new InvalidOperationException($"Journey {label} count can not become negative.");
        }
    }

    private static JourneyHistoryRecord History(
        JourneyHistoryKind kind,
        JourneyClockReference clock,
        ExpeditionConsequenceProvenance provenance,
        string detail,
        Guid? processId = null,
        string? stageKey = null,
        Guid? resolutionId = null,
        Guid? eventOccurrenceId = null) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        ProcessId = processId,
        StageKey = stageKey,
        ResolutionId = resolutionId,
        EventOccurrenceId = eventOccurrenceId,
        ExpeditionTime = clock.ExpeditionTime,
        CompletedWatches = clock.CompletedWatches,
        Detail = detail,
        Provenance = provenance
    };

    private static string RequiredText(string? value, string label, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new InvalidOperationException($"{label} is too long.");
        }
        return trimmed;
    }
}
