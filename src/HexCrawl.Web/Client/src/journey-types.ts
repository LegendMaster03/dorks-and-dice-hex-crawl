import type {
    ConsequenceProvenance,
    ConsequenceProvenanceRequest,
    ExpeditionConsequenceSourceKind,
    ExpeditionEffectScope,
    ExpeditionTarget,
    PersistentEffectChangeOperation,
    ResourceChangeOperation
} from "./survival-types";

export type JourneyPolicySupport = "None" | "Supported" | "Unsupported";
export type JourneyProcessStatus = "Planned" | "Active" | "ResolutionRequired" | "Completed" | "Failed" | "Abandoned";
export type JourneyProgressValueKind = "Numeric" | "ExplicitState";
export type JourneyStageCompletionModel = "Explicit" | "ProgressThreshold" | "SuccessCount" | "ResolutionSelected";
export type JourneyStageTransitionModel = "Explicit" | "Sequential" | "OutcomeSelected";
export type JourneyRoleAssignmentModel = "CurrentAtResolution";
export type JourneyIntervalIntegrationModel = "None" | "CompletedWatchResolutionOpportunity";
export type JourneyPendingActionKind = "ProcessResolution" | "WatchResolution" | "ApproachSelection" | "StageTransition";
export type JourneyEventStatus = "ResolutionRequired" | "Resolved" | "Skipped" | "NotApplicable";
export type JourneyEventTriggerKind = "Explicit" | "ProcessProgress" | "StageTransition" | "WatchCompleted" | "Landmark" | "External";
export type JourneyEventTargetKind = "Unresolved" | "Participant" | "Role" | "Party" | "Mount" | "Vehicle" | "Expedition";
export type JourneyEventLinkMode = "Standalone" | "ProcessLinked" | "Both";
export type JourneyHistoryKind =
    | "ProcessStarted"
    | "ResolutionRecorded"
    | "ProgressChanged"
    | "ComplicationChanged"
    | "FailureChanged"
    | "StageTransitioned"
    | "ProcessCompleted"
    | "ProcessFailed"
    | "ProcessAbandoned"
    | "EventOpportunityCreated"
    | "EventResolved"
    | "EventSkipped"
    | "WatchOpportunityCreated";

export type JourneyExternalCapabilityReference = {
    capabilityKey: string;
    providerKey: string | null;
    sourceReference: string | null;
};

export type JourneyApproachDefinition = {
    approachKey: string;
    displayName: string;
    capabilityReference: JourneyExternalCapabilityReference | null;
    note: string | null;
};

export type JourneyOutcomeTransition = {
    outcomeKey: string;
    targetStageKey: string;
};

export type JourneyStageDefinition = {
    stageKey: string;
    displayName: string;
    description: string | null;
    completionModel: JourneyStageCompletionModel;
    progressTarget: number | null;
    successTarget: number | null;
    failureLimit: number | null;
    complicationLimit: number | null;
    failProcessAtFailureLimit: boolean;
    failProcessAtComplicationLimit: boolean;
    initialProgressState: string | null;
    explicitNextStageKey: string | null;
    outcomeTransitions: JourneyOutcomeTransition[];
    approaches: JourneyApproachDefinition[];
    roleKeys: string[];
};

export type JourneyProcessDefinition = {
    processKey: string;
    displayName: string;
    description: string | null;
    initialStageKey: string;
    stageOrder: string[];
    stages: JourneyStageDefinition[];
    destinationReference: string | null;
    routeReference: string | null;
    locationReference: string | null;
    note: string | null;
};

export type JourneyProcessExecutionSnapshot = {
    stageModel: string;
    stageTransitionModel: JourneyStageTransitionModel;
    progressModel: string;
    progressKind: JourneyProgressValueKind;
    progressUnit: string | null;
    allowNegativeProgress: boolean;
    progressFloor: number | null;
    progressCeiling: number | null;
    completionModel: string;
    roleDriven: boolean;
    roleAssignmentModel: JourneyRoleAssignmentModel;
    intervalIntegrationModel: JourneyIntervalIntegrationModel;
    blocksRelevantTravelWhileResolutionRequired: boolean;
    mechanicKey: string;
    mechanicVersion: number;
    executionHandler: string;
};

export type JourneyStageState = {
    stageKey: string;
    numericProgress: number | null;
    explicitState: string | null;
    successes: number;
    failures: number;
    complications: number;
    completed: boolean;
};

export type JourneyParticipantSnapshot = {
    participantId: string;
    participantName: string;
    roleKey: string | null;
    assignmentId: string | null;
};

export type JourneyPendingAction = {
    id: string;
    kind: JourneyPendingActionKind;
    stageKey: string;
    sourceReference: string | null;
    requiredRoleKey: string | null;
    participantId: string | null;
    detail: string | null;
};

export type JourneyProcessInstance = {
    id: string;
    status: JourneyProcessStatus;
    definition: JourneyProcessDefinition;
    execution: JourneyProcessExecutionSnapshot;
    currentStageKey: string;
    stageStates: JourneyStageState[];
    pendingActions: JourneyPendingAction[];
    startedAtExpeditionTime: string;
    startedAfterCompletedWatches: number;
    endedAtExpeditionTime: string | null;
    endedAfterCompletedWatches: number | null;
    endReason: string | null;
    provenance: ConsequenceProvenance;
    processKey: string;
    isTerminal: boolean;
};

export type JourneyEnvironmentFactSnapshot = {
    dimension: string;
    value: string;
    unit: string | null;
    source: string | null;
};

export type JourneyEventOccurrence = {
    id: string;
    processId: string | null;
    stageKey: string | null;
    trigger: JourneyEventTriggerKind;
    triggerReference: string;
    status: JourneyEventStatus;
    targetKind: JourneyEventTargetKind;
    targetRoleKey: string | null;
    targetId: string | null;
    participantSnapshot: JourneyParticipantSnapshot | null;
    eventKey: string | null;
    eventType: string | null;
    environment: JourneyEnvironmentFactSnapshot[];
    consequenceIds: string[];
    provenance: ConsequenceProvenance;
    note: string | null;
};

export type JourneyResolutionRecord = {
    resolutionId: string;
    processId: string;
    stageKey: string;
    approachKey: string | null;
    outcomeKey: string | null;
    actor: JourneyParticipantSnapshot | null;
    progressBefore: number | null;
    progressAfter: number | null;
    stateBefore: string | null;
    stateAfter: string | null;
    successDelta: number;
    failureDelta: number;
    complicationDelta: number;
    transitionFromStageKey: string | null;
    transitionToStageKey: string | null;
    consequenceIds: string[];
    eventOccurrenceIds: string[];
    provenance: ConsequenceProvenance;
};

export type JourneyHistoryRecord = {
    id: string;
    kind: JourneyHistoryKind;
    processId: string | null;
    stageKey: string | null;
    resolutionId: string | null;
    eventOccurrenceId: string | null;
    expeditionTime: string;
    completedWatches: number;
    detail: string;
    provenance: ConsequenceProvenance;
};

export type JourneyProcessPolicy = {
    support: JourneyPolicySupport;
    stageModel: string | null;
    stageKeys: string[];
    stageTransitionModel: JourneyStageTransitionModel | null;
    progressModel: string | null;
    progressKind: JourneyProgressValueKind | null;
    progressUnit: string | null;
    allowNegativeProgress: boolean;
    progressFloor: number | null;
    progressCeiling: number | null;
    completionModel: string | null;
    roleDriven: boolean;
    roleAssignmentModel: JourneyRoleAssignmentModel | null;
    intervalIntegrationModel: JourneyIntervalIntegrationModel | null;
    blocksRelevantTravelWhileResolutionRequired: boolean;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type JourneyEventPolicy = {
    support: JourneyPolicySupport;
    triggerModel: string | null;
    triggerSources: JourneyEventTriggerKind[];
    linkMode: JourneyEventLinkMode | null;
    targetingModel: string | null;
    terrainInfluence: string | null;
    consequenceModel: string | null;
    requiresResolvedTrigger: boolean;
    blocksRelevantTravelWhileResolutionRequired: boolean;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type ExpeditionJourneyState = {
    expeditionVersion: number;
    processPolicy: JourneyProcessPolicy;
    eventPolicy: JourneyEventPolicy;
    activeProcesses: JourneyProcessInstance[];
    closedProcesses: JourneyProcessInstance[];
    eventOccurrences: JourneyEventOccurrence[];
    resolutions: JourneyResolutionRecord[];
    history: JourneyHistoryRecord[];
};

export type TimeDelayUnit = "Minutes" | "Hours" | "Days";
export type ExpeditionConsequenceCategory =
    | "TimeDelay"
    | "MovementChange"
    | "ResourceChange"
    | "ExposureFatigue"
    | "DamageEndurance"
    | "NavigationChange"
    | "EncounterCircumstance"
    | "PersistentEffectChange"
    | "Custom";

export type JourneyConsequenceComponent =
    | { kind: "timeDelay"; value: number; unit: TimeDelayUnit }
    | {
        kind: "resourceChange";
        resourceKey: string;
        resourceId?: string | null;
        operation: ResourceChangeOperation;
        quantity?: number | null;
        unit?: string | null;
        state?: string | null;
        supplyDieSides?: number | null;
    }
    | {
        kind: "persistentEffectChange";
        effectKey: string;
        operation: PersistentEffectChangeOperation;
        levelDelta?: number | null;
        level?: number | null;
        magnitude?: number | null;
        unit?: string | null;
        state?: string | null;
        explicitlyResolved?: boolean;
        movementComponents?: unknown[];
    }
    | { kind: "encounterCircumstance"; circumstanceKey: string; value?: string | null }
    | { kind: "externalState"; stateKey: string; delta: number; unit?: string | null }
    | { kind: "custom"; componentKey: string; quantity?: number | null; unit?: string | null; state?: string | null };

export type ExpeditionConsequenceInput = {
    id: string;
    consequenceKey: string;
    category: ExpeditionConsequenceCategory;
    target: ExpeditionTarget;
    components: JourneyConsequenceComponent[];
    provenance: ConsequenceProvenanceRequest;
    sourceReference?: string | null;
    note?: string | null;
};

export type JourneyProcessResolutionInput = {
    resolutionId: string;
    processId: string;
    stageKey: string;
    pendingActionId?: string | null;
    approachKey?: string | null;
    actorParticipantId?: string | null;
    roleKey?: string | null;
    outcomeKey?: string | null;
    progressDelta?: number | null;
    progressState?: string | null;
    successDelta: number;
    failureDelta: number;
    complicationDelta: number;
    completeStage: boolean;
    targetStageKey?: string | null;
    completeProcess: boolean;
    failProcess: boolean;
    consequences: ExpeditionConsequenceInput[];
    provenance: ConsequenceProvenanceRequest;
};

export type JourneyEventOpportunityInput = {
    occurrenceId: string;
    processId?: string | null;
    stageKey?: string | null;
    trigger: JourneyEventTriggerKind;
    triggerReference: string;
    targetKind: JourneyEventTargetKind;
    targetRoleKey?: string | null;
    targetId?: string | null;
    environment: JourneyEnvironmentFactSnapshot[];
    provenance: ConsequenceProvenanceRequest;
    note?: string | null;
};

export type JourneyEventResolutionInput = {
    occurrenceId: string;
    status: JourneyEventStatus;
    eventKey?: string | null;
    eventType?: string | null;
    targetKind: JourneyEventTargetKind;
    targetRoleKey?: string | null;
    targetId?: string | null;
    environment?: JourneyEnvironmentFactSnapshot[] | null;
    consequences: ExpeditionConsequenceInput[];
    provenance: ConsequenceProvenanceRequest;
    note?: string | null;
};

export type StartJourneyProcessRequest = {
    expectedVersion: number;
    processId: string;
    definition?: JourneyProcessDefinition | null;
    processKey?: string | null;
    displayName?: string | null;
    description?: string | null;
    destinationReference?: string | null;
    routeReference?: string | null;
    locationReference?: string | null;
    provenance: ConsequenceProvenanceRequest;
};

export type ResolveJourneyProcessRequest = {
    expectedVersion: number;
    resolution: JourneyProcessResolutionInput;
    captureCurrentEnvironment: boolean;
};

export type CreateJourneyEventOpportunityRequest = {
    expectedVersion: number;
    opportunity: JourneyEventOpportunityInput;
    captureCurrentEnvironment: boolean;
};

export type ResolveJourneyEventRequest = {
    expectedVersion: number;
    resolution: JourneyEventResolutionInput;
    captureCurrentEnvironment: boolean;
};

export type CloseJourneyProcessRequest = {
    expectedVersion: number;
    reason: string;
    provenance: ConsequenceProvenanceRequest;
};

export type JourneyOperation = {
    detail: string;
    stateChanged: boolean;
    processId: string | null;
    eventOccurrenceId: string | null;
    state: ExpeditionJourneyState;
};

export function dmJourneyProvenance(sourceKey: string, note?: string | null): ConsequenceProvenanceRequest {
    return {
        sourceKind: "Dm" satisfies ExpeditionConsequenceSourceKind,
        sourceKey,
        sourceReference: null,
        providerName: null,
        note: note ?? null
    };
}

export function target(scope: ExpeditionEffectScope, targetId?: string | null): ExpeditionTarget {
    return { scope, targetId: targetId ?? null };
}
