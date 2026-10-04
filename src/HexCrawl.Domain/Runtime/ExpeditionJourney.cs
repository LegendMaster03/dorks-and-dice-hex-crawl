namespace HexCrawl.Domain.Runtime;

public enum JourneyProcessStatus
{
    Planned,
    Active,
    ResolutionRequired,
    Completed,
    Failed,
    Abandoned
}

public enum JourneyProgressValueKind
{
    Numeric,
    ExplicitState
}

public enum JourneyStageCompletionModel
{
    Explicit,
    ProgressThreshold,
    SuccessCount,
    ResolutionSelected
}

public enum JourneyStageTransitionModel
{
    Explicit,
    Sequential,
    OutcomeSelected
}

public enum JourneyRoleAssignmentModel
{
    CurrentAtResolution
}

public enum JourneyIntervalIntegrationModel
{
    None,
    CompletedWatchResolutionOpportunity
}

public enum JourneyPendingActionKind
{
    ProcessResolution,
    WatchResolution,
    ApproachSelection,
    StageTransition
}

public enum JourneyEventStatus
{
    ResolutionRequired,
    Resolved,
    Skipped,
    NotApplicable
}

public enum JourneyEventTriggerKind
{
    Explicit,
    ProcessProgress,
    StageTransition,
    WatchCompleted,
    Landmark,
    External
}

public enum JourneyEventTargetKind
{
    Unresolved,
    Participant,
    Role,
    Party,
    Mount,
    Vehicle,
    Expedition
}

public enum JourneyHistoryKind
{
    ProcessStarted,
    ResolutionRecorded,
    ProgressChanged,
    ComplicationChanged,
    FailureChanged,
    StageTransitioned,
    ProcessCompleted,
    ProcessFailed,
    ProcessAbandoned,
    EventOpportunityCreated,
    EventResolved,
    EventSkipped,
    WatchOpportunityCreated
}

public sealed record JourneyExternalCapabilityReference(
    string CapabilityKey,
    string? ProviderKey = null,
    string? SourceReference = null)
{
    public void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(CapabilityKey, 512, "Journey capability key");
        ExpeditionConsequenceProvenance.ValidateOptional(ProviderKey, 200, "Journey capability provider key");
        ExpeditionConsequenceProvenance.ValidateOptional(SourceReference, 1000, "Journey capability source reference");
    }
}

public sealed record JourneyApproachDefinition(
    string ApproachKey,
    string DisplayName,
    JourneyExternalCapabilityReference? CapabilityReference = null,
    string? Note = null)
{
    public void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(ApproachKey, 200, "Journey approach key");
        ExpeditionConsequenceProvenance.RequireText(DisplayName, 300, "Journey approach display name");
        ExpeditionConsequenceProvenance.ValidateOptional(Note, 2000, "Journey approach note");
        CapabilityReference?.Validate();
    }
}

public sealed record JourneyOutcomeTransition(string OutcomeKey, string TargetStageKey)
{
    public void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(OutcomeKey, 200, "Journey outcome key");
        ExpeditionConsequenceProvenance.RequireText(TargetStageKey, 200, "Journey outcome target stage key");
    }
}

public sealed record JourneyStageDefinition
{
    public required string StageKey { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public JourneyStageCompletionModel CompletionModel { get; init; } = JourneyStageCompletionModel.Explicit;
    public double? ProgressTarget { get; init; }
    public int? SuccessTarget { get; init; }
    public int? FailureLimit { get; init; }
    public int? ComplicationLimit { get; init; }
    public bool FailProcessAtFailureLimit { get; init; }
    public bool FailProcessAtComplicationLimit { get; init; }
    public string? InitialProgressState { get; init; }
    public string? ExplicitNextStageKey { get; init; }
    public IReadOnlyList<JourneyOutcomeTransition> OutcomeTransitions { get; init; } = [];
    public IReadOnlyList<JourneyApproachDefinition> Approaches { get; init; } = [];
    public IReadOnlyList<string> RoleKeys { get; init; } = [];

    public void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(StageKey, 200, "Journey stage key");
        ExpeditionConsequenceProvenance.RequireText(DisplayName, 300, "Journey stage display name");
        ExpeditionConsequenceProvenance.ValidateOptional(Description, 4000, "Journey stage description");
        ExpeditionConsequenceProvenance.ValidateOptional(InitialProgressState, 1000, "Journey stage initial progress state");
        ExpeditionConsequenceProvenance.ValidateOptional(ExplicitNextStageKey, 200, "Journey stage next key");
        if (ProgressTarget.HasValue && (!double.IsFinite(ProgressTarget.Value) || ProgressTarget.Value < 0))
        {
            throw new InvalidOperationException("Journey stage progress target must be finite and non-negative.");
        }
        if (SuccessTarget is <= 0 || FailureLimit is <= 0 || ComplicationLimit is <= 0)
        {
            throw new InvalidOperationException("Journey stage count targets and limits must be positive when supplied.");
        }
        if (CompletionModel == JourneyStageCompletionModel.ProgressThreshold && !ProgressTarget.HasValue)
        {
            throw new InvalidOperationException("A progress-threshold journey stage requires a progress target.");
        }
        if (CompletionModel == JourneyStageCompletionModel.SuccessCount && !SuccessTarget.HasValue)
        {
            throw new InvalidOperationException("A success-count journey stage requires a success target.");
        }
        if (Approaches.Select(value => value.ApproachKey).Distinct(StringComparer.Ordinal).Count() != Approaches.Count)
        {
            throw new InvalidOperationException("Journey stage approach keys must be unique.");
        }
        foreach (var approach in Approaches) approach.Validate();
        if (RoleKeys.Any(string.IsNullOrWhiteSpace)
            || RoleKeys.Distinct(StringComparer.Ordinal).Count() != RoleKeys.Count)
        {
            throw new InvalidOperationException("Journey stage role keys must be nonblank and unique.");
        }
        if (OutcomeTransitions.Select(value => value.OutcomeKey).Distinct(StringComparer.Ordinal).Count() != OutcomeTransitions.Count)
        {
            throw new InvalidOperationException("Journey stage outcome transition keys must be unique.");
        }
        foreach (var transition in OutcomeTransitions) transition.Validate();
    }
}

public sealed record JourneyProcessDefinition
{
    public required string ProcessKey { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string InitialStageKey { get; init; }
    public required IReadOnlyList<string> StageOrder { get; init; }
    public required IReadOnlyList<JourneyStageDefinition> Stages { get; init; }
    public string? DestinationReference { get; init; }
    public string? RouteReference { get; init; }
    public string? LocationReference { get; init; }
    public string? Note { get; init; }

    public void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(ProcessKey, 200, "Journey process key");
        ExpeditionConsequenceProvenance.RequireText(DisplayName, 300, "Journey process display name");
        ExpeditionConsequenceProvenance.RequireText(InitialStageKey, 200, "Journey initial stage key");
        ExpeditionConsequenceProvenance.ValidateOptional(Description, 4000, "Journey process description");
        ExpeditionConsequenceProvenance.ValidateOptional(DestinationReference, 1000, "Journey destination reference");
        ExpeditionConsequenceProvenance.ValidateOptional(RouteReference, 1000, "Journey route reference");
        ExpeditionConsequenceProvenance.ValidateOptional(LocationReference, 1000, "Journey location reference");
        ExpeditionConsequenceProvenance.ValidateOptional(Note, 4000, "Journey process note");
        if (Stages.Count == 0)
        {
            throw new InvalidOperationException("A journey process definition requires at least one stage.");
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in Stages)
        {
            stage.Validate();
            if (!keys.Add(stage.StageKey))
            {
                throw new InvalidOperationException("Journey process stage keys must be unique.");
            }
        }
        if (!keys.Contains(InitialStageKey))
        {
            throw new InvalidOperationException("Journey process initial stage must exist in the stage definition.");
        }
        if (StageOrder.Count != Stages.Count
            || StageOrder.Any(string.IsNullOrWhiteSpace)
            || StageOrder.Distinct(StringComparer.Ordinal).Count() != StageOrder.Count
            || StageOrder.Any(value => !keys.Contains(value)))
        {
            throw new InvalidOperationException("Journey process stage order must explicitly contain every stage exactly once.");
        }
        foreach (var stage in Stages)
        {
            if (stage.ExplicitNextStageKey is { } next && !keys.Contains(next))
            {
                throw new InvalidOperationException($"Journey stage '{stage.StageKey}' references missing next stage '{next}'.");
            }
            foreach (var transition in stage.OutcomeTransitions)
            {
                if (!keys.Contains(transition.TargetStageKey))
                {
                    throw new InvalidOperationException(
                        $"Journey stage '{stage.StageKey}' outcome references missing stage '{transition.TargetStageKey}'.");
                }
            }
        }
    }
}

public sealed record JourneyProcessExecutionSnapshot
{
    public required string StageModel { get; init; }
    public required JourneyStageTransitionModel StageTransitionModel { get; init; }
    public required string ProgressModel { get; init; }
    public required JourneyProgressValueKind ProgressKind { get; init; }
    public string? ProgressUnit { get; init; }
    public bool AllowNegativeProgress { get; init; }
    public double? ProgressFloor { get; init; }
    public double? ProgressCeiling { get; init; }
    public required string CompletionModel { get; init; }
    public bool RoleDriven { get; init; }
    public JourneyRoleAssignmentModel RoleAssignmentModel { get; init; } = JourneyRoleAssignmentModel.CurrentAtResolution;
    public JourneyIntervalIntegrationModel IntervalIntegrationModel { get; init; } = JourneyIntervalIntegrationModel.None;
    public bool BlocksRelevantTravelWhileResolutionRequired { get; init; }
    public required string MechanicKey { get; init; }
    public required int MechanicVersion { get; init; }
    public required string ExecutionHandler { get; init; }

    public void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(StageModel, 200, "Journey stage model");
        ExpeditionConsequenceProvenance.RequireText(ProgressModel, 200, "Journey progress model");
        ExpeditionConsequenceProvenance.RequireText(CompletionModel, 200, "Journey completion model");
        ExpeditionConsequenceProvenance.RequireText(MechanicKey, 200, "Journey process mechanic key");
        ExpeditionConsequenceProvenance.RequireText(ExecutionHandler, 200, "Journey process handler");
        if (MechanicVersion <= 0)
        {
            throw new InvalidOperationException("Journey process mechanic version must be positive.");
        }
        if (ProgressKind == JourneyProgressValueKind.Numeric && string.IsNullOrWhiteSpace(ProgressUnit))
        {
            throw new InvalidOperationException("Numeric journey progress requires an explicit progress unit.");
        }
        if (ProgressKind == JourneyProgressValueKind.ExplicitState && ProgressUnit is not null)
        {
            throw new InvalidOperationException("Explicit-state journey progress does not use a numeric progress unit.");
        }
        ExpeditionConsequenceProvenance.ValidateOptional(ProgressUnit, 100, "Journey progress unit");
        if (ProgressFloor.HasValue && !double.IsFinite(ProgressFloor.Value)
            || ProgressCeiling.HasValue && !double.IsFinite(ProgressCeiling.Value))
        {
            throw new InvalidOperationException("Journey progress bounds must be finite.");
        }
        if (ProgressFloor.HasValue && ProgressCeiling.HasValue && ProgressFloor > ProgressCeiling)
        {
            throw new InvalidOperationException("Journey progress floor can not exceed its ceiling.");
        }
        if (!AllowNegativeProgress && ProgressFloor is < 0)
        {
            throw new InvalidOperationException("A journey process that forbids negative progress can not declare a negative floor.");
        }
    }
}

public sealed record JourneyStageState
{
    public required string StageKey { get; init; }
    public double? NumericProgress { get; init; }
    public string? ExplicitState { get; init; }
    public int Successes { get; init; }
    public int Failures { get; init; }
    public int Complications { get; init; }
    public bool Completed { get; init; }

    public void Validate(JourneyProcessExecutionSnapshot execution, JourneyStageDefinition definition)
    {
        if (!string.Equals(StageKey, definition.StageKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Journey stage runtime state does not match its stage definition.");
        }
        if (Successes < 0 || Failures < 0 || Complications < 0)
        {
            throw new InvalidOperationException("Journey stage counters can not be negative.");
        }
        if (execution.ProgressKind == JourneyProgressValueKind.Numeric)
        {
            if (!NumericProgress.HasValue || !double.IsFinite(NumericProgress.Value) || ExplicitState is not null)
            {
                throw new InvalidOperationException("Numeric journey progress requires one finite numeric value.");
            }
            if (!execution.AllowNegativeProgress && NumericProgress.Value < 0)
            {
                throw new InvalidOperationException("Journey progress can not be negative for this process.");
            }
            if (execution.ProgressFloor.HasValue && NumericProgress.Value < execution.ProgressFloor.Value
                || execution.ProgressCeiling.HasValue && NumericProgress.Value > execution.ProgressCeiling.Value)
            {
                throw new InvalidOperationException("Journey progress is outside the configured process bounds.");
            }
        }
        else if (string.IsNullOrWhiteSpace(ExplicitState) || NumericProgress.HasValue)
        {
            throw new InvalidOperationException("Explicit-state journey progress requires one nonblank state value.");
        }
    }
}

public sealed record JourneyParticipantSnapshot(
    Guid ParticipantId,
    string ParticipantName,
    string? RoleKey = null,
    Guid? AssignmentId = null)
{
    public void Validate()
    {
        if (ParticipantId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey participant snapshot id is required.");
        }
        ExpeditionConsequenceProvenance.RequireText(ParticipantName, 300, "Journey participant snapshot name");
        ExpeditionConsequenceProvenance.ValidateOptional(RoleKey, 200, "Journey participant snapshot role");
        if (AssignmentId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey participant assignment id can not be empty.");
        }
    }
}

public sealed record JourneyPendingAction
{
    public required Guid Id { get; init; }
    public required JourneyPendingActionKind Kind { get; init; }
    public required string StageKey { get; init; }
    public string? SourceReference { get; init; }
    public string? RequiredRoleKey { get; init; }
    public Guid? ParticipantId { get; init; }
    public string? Detail { get; init; }

    public void Validate(CrawlPartySheet party, JourneyProcessDefinition definition)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Journey pending action id is required.");
        }
        if (!definition.Stages.Any(value => string.Equals(value.StageKey, StageKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Journey pending action references a stage that does not exist.");
        }
        ExpeditionConsequenceProvenance.ValidateOptional(SourceReference, 1000, "Journey pending action source reference");
        ExpeditionConsequenceProvenance.ValidateOptional(RequiredRoleKey, 200, "Journey pending action role key");
        ExpeditionConsequenceProvenance.ValidateOptional(Detail, 2000, "Journey pending action detail");
        if (ParticipantId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey pending action participant id can not be empty.");
        }
        if (ParticipantId.HasValue && !party.Members.Any(value => value.Id == ParticipantId.Value))
        {
            throw new InvalidOperationException("Journey pending action references a participant that does not exist.");
        }
    }
}

public sealed record JourneyProcessInstance
{
    public required Guid Id { get; init; }
    public required JourneyProcessStatus Status { get; init; }
    public required JourneyProcessDefinition Definition { get; init; }
    public required JourneyProcessExecutionSnapshot Execution { get; init; }
    public required string CurrentStageKey { get; init; }
    public required IReadOnlyList<JourneyStageState> StageStates { get; init; }
    public IReadOnlyList<JourneyPendingAction> PendingActions { get; init; } = [];
    public required TimeSpan StartedAtExpeditionTime { get; init; }
    public required int StartedAfterCompletedWatches { get; init; }
    public TimeSpan? EndedAtExpeditionTime { get; init; }
    public int? EndedAfterCompletedWatches { get; init; }
    public string? EndReason { get; init; }
    public required ExpeditionConsequenceProvenance Provenance { get; init; }

    public string ProcessKey => Definition.ProcessKey;
    public bool IsTerminal => Status is JourneyProcessStatus.Completed or JourneyProcessStatus.Failed or JourneyProcessStatus.Abandoned;

    public void Validate(CrawlPartySheet party)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Journey process id is required.");
        }
        Definition.Validate();
        Execution.Validate();
        Provenance.Validate();
        if (!Definition.Stages.Any(value => string.Equals(value.StageKey, CurrentStageKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Journey process current stage does not exist in its definition.");
        }
        if (StartedAtExpeditionTime < TimeSpan.Zero || StartedAfterCompletedWatches < 0)
        {
            throw new InvalidOperationException("Journey process start reference must be non-negative.");
        }
        if (EndedAtExpeditionTime is < TimeSpan.Zero || EndedAfterCompletedWatches is < 0)
        {
            throw new InvalidOperationException("Journey process end reference must be non-negative.");
        }
        if (IsTerminal != EndedAtExpeditionTime.HasValue)
        {
            throw new InvalidOperationException("Journey terminal status and end reference must agree.");
        }
        if (IsTerminal && string.IsNullOrWhiteSpace(EndReason))
        {
            throw new InvalidOperationException("A terminal journey process requires an end reason.");
        }
        if (!IsTerminal && EndReason is not null)
        {
            throw new InvalidOperationException("An active journey process can not have an end reason.");
        }
        if (StageStates.Count != Definition.Stages.Count
            || StageStates.Select(value => value.StageKey).Distinct(StringComparer.Ordinal).Count() != StageStates.Count)
        {
            throw new InvalidOperationException("Journey process must retain exactly one runtime state for every stage.");
        }
        foreach (var definition in Definition.Stages)
        {
            var state = StageStates.SingleOrDefault(value => string.Equals(value.StageKey, definition.StageKey, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Journey process is missing runtime state for a stage.");
            state.Validate(Execution, definition);
        }
        if (PendingActions.Select(value => value.Id).Distinct().Count() != PendingActions.Count)
        {
            throw new InvalidOperationException("Journey pending action ids must be unique within a process.");
        }
        foreach (var pending in PendingActions) pending.Validate(party, Definition);
        if (Status == JourneyProcessStatus.ResolutionRequired && PendingActions.Count == 0)
        {
            throw new InvalidOperationException("Resolution-required journey process status requires pending work.");
        }
        if (Status == JourneyProcessStatus.Active && PendingActions.Count > 0)
        {
            throw new InvalidOperationException("A journey process with pending work must use ResolutionRequired status.");
        }
        if (IsTerminal && PendingActions.Count > 0)
        {
            throw new InvalidOperationException("A terminal journey process can not retain pending actions.");
        }
    }
}

public sealed record JourneyEnvironmentFactSnapshot(
    string Dimension,
    string Value,
    string? Unit = null,
    string? Source = null)
{
    public void Validate()
    {
        ExpeditionConsequenceProvenance.RequireText(Dimension, 200, "Journey environment dimension");
        ExpeditionConsequenceProvenance.RequireText(Value, 1000, "Journey environment value");
        ExpeditionConsequenceProvenance.ValidateOptional(Unit, 100, "Journey environment unit");
        ExpeditionConsequenceProvenance.ValidateOptional(Source, 1000, "Journey environment source");
    }
}

public sealed record JourneyEventOccurrence
{
    public required Guid Id { get; init; }
    public Guid? ProcessId { get; init; }
    public string? StageKey { get; init; }
    public required JourneyEventTriggerKind Trigger { get; init; }
    public required string TriggerReference { get; init; }
    public required JourneyEventStatus Status { get; init; }
    public JourneyEventTargetKind TargetKind { get; init; } = JourneyEventTargetKind.Unresolved;
    public string? TargetRoleKey { get; init; }
    public Guid? TargetId { get; init; }
    public JourneyParticipantSnapshot? ParticipantSnapshot { get; init; }
    public string? EventKey { get; init; }
    public string? EventType { get; init; }
    public IReadOnlyList<JourneyEnvironmentFactSnapshot> Environment { get; init; } = [];
    public IReadOnlyList<Guid> ConsequenceIds { get; init; } = [];
    public required ExpeditionConsequenceProvenance Provenance { get; init; }
    public string? Note { get; init; }

    public void Validate(CrawlPartySheet party, IReadOnlySet<Guid> activeProcessIds, IReadOnlySet<Guid>? knownConsequenceIds = null)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("Journey event occurrence id is required.");
        }
        if (ProcessId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey event process id can not be empty.");
        }
        if (ProcessId.HasValue && !activeProcessIds.Contains(ProcessId.Value))
        {
            throw new InvalidOperationException("Journey event references a process that is not retained in journey state.");
        }
        ExpeditionConsequenceProvenance.RequireText(TriggerReference, 1000, "Journey event trigger reference");
        ExpeditionConsequenceProvenance.ValidateOptional(StageKey, 200, "Journey event stage key");
        ExpeditionConsequenceProvenance.ValidateOptional(TargetRoleKey, 200, "Journey event target role");
        ExpeditionConsequenceProvenance.ValidateOptional(EventKey, 300, "Journey event key");
        ExpeditionConsequenceProvenance.ValidateOptional(EventType, 300, "Journey event type");
        ExpeditionConsequenceProvenance.ValidateOptional(Note, 4000, "Journey event note");
        Provenance.Validate();
        foreach (var fact in Environment) fact.Validate();
        if (ConsequenceIds.Any(value => value == Guid.Empty)
            || ConsequenceIds.Distinct().Count() != ConsequenceIds.Count)
        {
            throw new InvalidOperationException("Journey event consequence ids must be non-empty and unique.");
        }
        if (knownConsequenceIds is not null && ConsequenceIds.Any(value => !knownConsequenceIds.Contains(value)))
        {
            throw new InvalidOperationException("Journey event references a consequence that is not retained in expedition effect state.");
        }
        if (Status == JourneyEventStatus.ResolutionRequired)
        {
            if (ParticipantSnapshot is not null || ConsequenceIds.Count > 0 || EventKey is not null)
            {
                throw new InvalidOperationException("An unresolved journey event can not contain resolved event output.");
            }
            ValidateLiveTarget(party);
        }
        else
        {
            ParticipantSnapshot?.Validate();
            if (Status == JourneyEventStatus.Resolved && string.IsNullOrWhiteSpace(EventKey))
            {
                throw new InvalidOperationException("A resolved journey event requires an event key.");
            }
        }
    }

    private void ValidateLiveTarget(CrawlPartySheet party)
    {
        if (TargetId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey event target id can not be empty.");
        }
        switch (TargetKind)
        {
            case JourneyEventTargetKind.Unresolved:
            case JourneyEventTargetKind.Role:
            case JourneyEventTargetKind.Party:
            case JourneyEventTargetKind.Expedition:
                if (TargetId.HasValue)
                {
                    throw new InvalidOperationException("This journey event target kind can not contain a target id while unresolved.");
                }
                break;
            case JourneyEventTargetKind.Participant:
                if (!TargetId.HasValue || !party.Members.Any(value => value.Id == TargetId.Value))
                {
                    throw new InvalidOperationException("Journey event references a participant that does not exist.");
                }
                break;
            case JourneyEventTargetKind.Mount:
            case JourneyEventTargetKind.Vehicle:
                var kind = TargetKind == JourneyEventTargetKind.Mount
                    ? MovementCapabilityContributorKind.Mount
                    : MovementCapabilityContributorKind.Vehicle;
                if (!TargetId.HasValue || !party.MovementContributors.Any(value => value.Id == TargetId && value.Kind == kind))
                {
                    throw new InvalidOperationException("Journey event references a movement contributor that does not exist.");
                }
                break;
            default:
                throw new InvalidOperationException("Journey event target kind is not supported.");
        }
    }
}

public sealed record JourneyResolutionRecord
{
    public required Guid ResolutionId { get; init; }
    public required Guid ProcessId { get; init; }
    public required string StageKey { get; init; }
    public string? ApproachKey { get; init; }
    public string? OutcomeKey { get; init; }
    public JourneyParticipantSnapshot? Actor { get; init; }
    public double? ProgressBefore { get; init; }
    public double? ProgressAfter { get; init; }
    public string? StateBefore { get; init; }
    public string? StateAfter { get; init; }
    public int SuccessDelta { get; init; }
    public int FailureDelta { get; init; }
    public int ComplicationDelta { get; init; }
    public string? TransitionFromStageKey { get; init; }
    public string? TransitionToStageKey { get; init; }
    public IReadOnlyList<Guid> ConsequenceIds { get; init; } = [];
    public IReadOnlyList<Guid> EventOccurrenceIds { get; init; } = [];
    public required ExpeditionConsequenceProvenance Provenance { get; init; }

    public void Validate(IReadOnlySet<Guid>? knownConsequenceIds = null)
    {
        if (ResolutionId == Guid.Empty || ProcessId == Guid.Empty)
        {
            throw new InvalidOperationException("Journey resolution identifiers are required.");
        }
        ExpeditionConsequenceProvenance.RequireText(StageKey, 200, "Journey resolution stage key");
        ExpeditionConsequenceProvenance.ValidateOptional(ApproachKey, 200, "Journey resolution approach key");
        ExpeditionConsequenceProvenance.ValidateOptional(OutcomeKey, 200, "Journey resolution outcome key");
        ExpeditionConsequenceProvenance.ValidateOptional(TransitionFromStageKey, 200, "Journey resolution transition source");
        ExpeditionConsequenceProvenance.ValidateOptional(TransitionToStageKey, 200, "Journey resolution transition target");
        if (ProgressBefore.HasValue && !double.IsFinite(ProgressBefore.Value)
            || ProgressAfter.HasValue && !double.IsFinite(ProgressAfter.Value))
        {
            throw new InvalidOperationException("Journey resolution progress values must be finite.");
        }
        Actor?.Validate();
        Provenance.Validate();
        if (ConsequenceIds.Any(value => value == Guid.Empty)
            || ConsequenceIds.Distinct().Count() != ConsequenceIds.Count
            || EventOccurrenceIds.Any(value => value == Guid.Empty)
            || EventOccurrenceIds.Distinct().Count() != EventOccurrenceIds.Count)
        {
            throw new InvalidOperationException("Journey resolution generated ids must be non-empty and unique.");
        }
        if (knownConsequenceIds is not null && ConsequenceIds.Any(value => !knownConsequenceIds.Contains(value)))
        {
            throw new InvalidOperationException("Journey resolution references a consequence that is not retained in expedition effect state.");
        }
    }
}

public sealed record JourneyHistoryRecord
{
    public required Guid Id { get; init; }
    public required JourneyHistoryKind Kind { get; init; }
    public Guid? ProcessId { get; init; }
    public string? StageKey { get; init; }
    public Guid? ResolutionId { get; init; }
    public Guid? EventOccurrenceId { get; init; }
    public required TimeSpan ExpeditionTime { get; init; }
    public required int CompletedWatches { get; init; }
    public required string Detail { get; init; }
    public required ExpeditionConsequenceProvenance Provenance { get; init; }

    public void Validate()
    {
        if (Id == Guid.Empty || ResolutionId == Guid.Empty || EventOccurrenceId == Guid.Empty || ProcessId == Guid.Empty)
        {
            if (Id == Guid.Empty)
            {
                throw new InvalidOperationException("Journey history id is required.");
            }
        }
        ExpeditionConsequenceProvenance.ValidateOptional(StageKey, 200, "Journey history stage key");
        if (ExpeditionTime < TimeSpan.Zero || CompletedWatches < 0)
        {
            throw new InvalidOperationException("Journey history expedition reference must be non-negative.");
        }
        ExpeditionConsequenceProvenance.RequireText(Detail, 4000, "Journey history detail");
        Provenance.Validate();
    }
}

public sealed record ExpeditionJourneyState
{
    public IReadOnlyList<JourneyProcessInstance> ActiveProcesses { get; init; } = [];
    public IReadOnlyList<JourneyProcessInstance> ClosedProcesses { get; init; } = [];
    public IReadOnlyList<JourneyEventOccurrence> EventOccurrences { get; init; } = [];
    public IReadOnlyList<JourneyResolutionRecord> Resolutions { get; init; } = [];
    public IReadOnlyList<JourneyHistoryRecord> History { get; init; } = [];
    public IReadOnlyList<Guid> ConsumedResolutionIds { get; init; } = [];
    public IReadOnlyList<string> ObservedRuntimeOccurrenceIds { get; init; } = [];

    public static ExpeditionJourneyState Empty { get; } = new();

    public void Validate(CrawlPartySheet party, IReadOnlySet<Guid>? knownConsequenceIds = null)
    {
        var processIds = new HashSet<Guid>();
        foreach (var process in ActiveProcesses)
        {
            process.Validate(party);
            if (process.IsTerminal)
            {
                throw new InvalidOperationException("A terminal journey process can not remain active.");
            }
            if (!processIds.Add(process.Id))
            {
                throw new InvalidOperationException("Journey process ids must be unique across active and closed state.");
            }
        }
        foreach (var process in ClosedProcesses)
        {
            process.Validate(party);
            if (!process.IsTerminal)
            {
                throw new InvalidOperationException("Closed journey process state must use a terminal status.");
            }
            if (!processIds.Add(process.Id))
            {
                throw new InvalidOperationException("Journey process ids must be unique across active and closed state.");
            }
        }
        var eventIds = new HashSet<Guid>();
        foreach (var occurrence in EventOccurrences)
        {
            occurrence.Validate(party, processIds, knownConsequenceIds);
            if (!eventIds.Add(occurrence.Id))
            {
                throw new InvalidOperationException("Journey event occurrence ids must be unique.");
            }
        }
        var resolutionIds = new HashSet<Guid>();
        foreach (var resolution in Resolutions)
        {
            resolution.Validate(knownConsequenceIds);
            if (!processIds.Contains(resolution.ProcessId))
            {
                throw new InvalidOperationException("Journey resolution references a process that is not retained in journey state.");
            }
            if (!resolutionIds.Add(resolution.ResolutionId))
            {
                throw new InvalidOperationException("Journey resolution ids must be unique.");
            }
            if (resolution.EventOccurrenceIds.Any(value => !eventIds.Contains(value)))
            {
                throw new InvalidOperationException("Journey resolution references an event occurrence that does not exist.");
            }
        }
        if (ConsumedResolutionIds.Any(value => value == Guid.Empty)
            || ConsumedResolutionIds.Distinct().Count() != ConsumedResolutionIds.Count
            || !ConsumedResolutionIds.All(resolutionIds.Contains))
        {
            throw new InvalidOperationException("Consumed journey resolution ids must be non-empty, unique, and retained in resolution history.");
        }
        if (ObservedRuntimeOccurrenceIds.Any(string.IsNullOrWhiteSpace)
            || ObservedRuntimeOccurrenceIds.Distinct(StringComparer.Ordinal).Count() != ObservedRuntimeOccurrenceIds.Count)
        {
            throw new InvalidOperationException("Observed journey runtime occurrence ids must be nonblank and unique.");
        }
        var historyIds = new HashSet<Guid>();
        foreach (var history in History)
        {
            history.Validate();
            if (!historyIds.Add(history.Id))
            {
                throw new InvalidOperationException("Journey history ids must be unique.");
            }
            if (history.ProcessId.HasValue && !processIds.Contains(history.ProcessId.Value))
            {
                throw new InvalidOperationException("Journey history references a process that is not retained.");
            }
            if (history.ResolutionId.HasValue && !resolutionIds.Contains(history.ResolutionId.Value))
            {
                throw new InvalidOperationException("Journey history references a resolution that is not retained.");
            }
            if (history.EventOccurrenceId.HasValue && !eventIds.Contains(history.EventOccurrenceId.Value))
            {
                throw new InvalidOperationException("Journey history references an event occurrence that is not retained.");
            }
        }
    }
}
