export type Phase11PolicySupport = "None" | "Supported" | "Unsupported";
export type ExpeditionResourceInventoryModel = "Counted" | "Abstract" | "SupplyDie" | "ExternalManual";
export type ExpeditionEffectScope = "Participant" | "Party" | "Mount" | "Vehicle" | "Expedition";
export type ResourceChangeOperation = "AdjustQuantity" | "SetQuantity" | "SetState" | "SetSupplyDie" | "Deplete";
export type ExpeditionConsequenceSourceKind =
    | "Dm"
    | "Procedure"
    | "EnvironmentResolution"
    | "Provider"
    | "ForcedTravelResult"
    | "JourneyEvent"
    | "Encounter"
    | "ExternalTool"
    | "ManualImport";
export type ExpeditionConsequenceStatus =
    | "Applied"
    | "AlreadyApplied"
    | "Recorded"
    | "Deferred"
    | "InputRequired"
    | "RequiresAdjudication"
    | "Unsupported"
    | "ExternalActionRequired"
    | "Failed";
export type SurvivalOperationStatus =
    | "Applied"
    | "AlreadyApplied"
    | "NotDue"
    | "InputRequired"
    | "RequiresAdjudication"
    | "Unsupported"
    | "ExternalActionRequired"
    | "Recorded";
export type PersistentEffectChangeOperation = "AdjustLevel" | "SetLevel" | "SetMagnitude" | "SetState" | "Clear";

export type ExpeditionTarget = {
    scope: ExpeditionEffectScope;
    targetId: string | null;
};

export type ConsequenceProvenance = {
    sourceKind: ExpeditionConsequenceSourceKind;
    sourceKey: string;
    sourceReference: string | null;
    providerName: string | null;
    note: string | null;
};

export type ResourceConsumptionPolicy = {
    support: Phase11PolicySupport;
    resourceKinds: string[];
    inventoryModel: ExpeditionResourceInventoryModel | null;
    consumptionModel: string | null;
    consumptionInterval: string | null;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type ForagingPolicy = {
    support: Phase11PolicySupport;
    resolutionModel: string | null;
    timeCost: number | null;
    timeUnit: string | null;
    movementTradeoff: string | null;
    activityBacked: boolean;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type CampingPolicy = {
    support: Phase11PolicySupport;
    resolutionModel: string | null;
    timeCost: number | null;
    timeUnit: string | null;
    watchModel: string | null;
    activityBacked: boolean;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type ForcedTravelPolicy = {
    support: Phase11PolicySupport;
    normalTravelLimit: number | null;
    limitUnit: string | null;
    checkModel: string | null;
    failureConsequence: string | null;
    failureTargetScope: ExpeditionEffectScope | null;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type ExposurePolicy = {
    support: Phase11PolicySupport;
    dimensions: string[];
    evaluationModel: string | null;
    evaluationInterval: string | null;
    targetScope: ExpeditionEffectScope | null;
    consequenceModel: string | null;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type ExpeditionResource = {
    id: string;
    resourceKey: string;
    target: ExpeditionTarget;
    inventoryModel: ExpeditionResourceInventoryModel;
    quantity: number | null;
    unit: string | null;
    symbolicState: string | null;
    supplyDieSides: number | null;
    isDepleted: boolean;
    note: string | null;
};

export type ForcedTravelResolution = {
    checkId: string;
    success: boolean;
    amountAtResolution: number;
    unit: string;
    consequenceId: string | null;
    provenance: ConsequenceProvenance;
};

export type ForcedTravelState = {
    amountSinceReset: number;
    unit: string | null;
    normalLimit: number | null;
    thresholdReached: boolean;
    forcedTravelBegun: boolean;
    checkDue: boolean;
    pendingCheckId: string | null;
    pendingConsequenceId: string | null;
    lastResolution: ForcedTravelResolution | null;
};

export type ExposureProgress = {
    id: string;
    exposureKey: string;
    target: ExpeditionTarget;
    amount: number;
    unit: string;
    sourceOccurrenceIds: string[];
};

export type CampState = {
    resolutionId: string;
    established: boolean;
    activityAssignmentIds: string[];
    resolutionModel: string | null;
    restTriggerKey: string | null;
    restSafe: boolean | null;
    restProlonged: boolean | null;
};

export type SurvivalEnvironmentFact = {
    id: string;
    dimension: string;
    valueKind: string;
    value: string | null;
    effective: boolean;
    sourceKind: string;
};

export type PendingResourceConsequence = {
    consequenceId: string;
    consequenceKey: string;
    status: ExpeditionConsequenceStatus;
    resourceKeys: string[];
    reason: string;
};

export type SurvivalResources = {
    expeditionVersion: number;
    resourcePolicy: ResourceConsumptionPolicy;
    foragingPolicy: ForagingPolicy;
    campingPolicy: CampingPolicy;
    forcedTravelPolicy: ForcedTravelPolicy;
    exposurePolicy: ExposurePolicy;
    resources: ExpeditionResource[];
    forcedTravel: ForcedTravelState;
    exposure: ExposureProgress[];
    camp: CampState | null;
    environmentFacts: SurvivalEnvironmentFact[];
    pendingResourceConsequences: PendingResourceConsequence[];
};

export type SurvivalOperation = {
    expeditionVersion: number;
    status: SurvivalOperationStatus;
    detail: string;
    consequenceId: string | null;
    state: SurvivalResources;
};

export type ConsequenceProvenanceRequest = {
    sourceKind: ExpeditionConsequenceSourceKind;
    sourceKey: string;
    sourceReference?: string | null;
    providerName?: string | null;
    note?: string | null;
};

export type ResourceChangeRequest = {
    resourceKey: string;
    resourceId?: string | null;
    operation: ResourceChangeOperation;
    quantity?: number | null;
    unit?: string | null;
    state?: string | null;
    supplyDieSides?: number | null;
};

export type ResolvedSurvivalComponentRequest = {
    kind: "PersistentEffect" | "ExternalState";
    key: string;
    effectOperation?: PersistentEffectChangeOperation | null;
    levelDelta?: number | null;
    level?: number | null;
    magnitude?: number | null;
    delta?: number | null;
    unit?: string | null;
    state?: string | null;
};

export type UpsertResourceRequest = {
    expectedVersion: number;
    resourceKey: string;
    target: ExpeditionTarget;
    inventoryModel: ExpeditionResourceInventoryModel;
    quantity: number | null;
    unit: string | null;
    symbolicState: string | null;
    supplyDieSides: number | null;
    note: string | null;
    provenance: ConsequenceProvenanceRequest;
};

export type RemoveResourceRequest = {
    expectedVersion: number;
    provenance: ConsequenceProvenanceRequest;
};

export type ApplyPendingResourceRequest = RemoveResourceRequest;

export type ResolveConsumptionRequest = {
    expectedVersion: number;
    occurrenceId: string;
    due: boolean;
    target: ExpeditionTarget;
    changes: ResourceChangeRequest[];
    provenance: ConsequenceProvenanceRequest;
};

export type ResolveForagingRequest = {
    expectedVersion: number;
    occurrenceId: string;
    target: ExpeditionTarget;
    activityAssignmentIds: string[];
    resourceGains: ResourceChangeRequest[];
    provenance: ConsequenceProvenanceRequest;
};

export type RecordForcedTravelUsageRequest = {
    expectedVersion: number;
    occurrenceId: string;
    amount: number;
    unit: string;
    provenance: ConsequenceProvenanceRequest;
};

export type ResolveForcedTravelCheckRequest = {
    expectedVersion: number;
    checkId: string;
    success: boolean;
    target: ExpeditionTarget;
    failureComponents: ResolvedSurvivalComponentRequest[];
    provenance: ConsequenceProvenanceRequest;
};

export type ResetForcedTravelRequest = {
    expectedVersion: number;
    provenance: ConsequenceProvenanceRequest;
};

export type ResolveExposureRequest = {
    expectedVersion: number;
    occurrenceId: string;
    exposureKey: string;
    target: ExpeditionTarget;
    progressDelta: number | null;
    progressUnit: string | null;
    consequenceComponents: ResolvedSurvivalComponentRequest[];
    provenance: ConsequenceProvenanceRequest;
};

export type ResolveCampRequest = {
    expectedVersion: number;
    resolutionId: string;
    established: boolean;
    activityAssignmentIds: string[];
    restTriggerKey: string | null;
    restSafe: boolean | null;
    restProlonged: boolean | null;
    provenance: ConsequenceProvenanceRequest;
};

export type RecoverFromRestRequest = {
    expectedVersion: number;
    triggerKey: string;
    effectId: string;
    levelReduction: number | null;
    clear: boolean;
    provenance: ConsequenceProvenanceRequest;
};
