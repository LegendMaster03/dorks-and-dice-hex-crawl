import type { ExpeditionDetail } from "./types";
import type {
    ConsequenceProvenance,
    ConsequenceProvenanceRequest,
    ExpeditionConsequenceStatus,
    ExpeditionEffectScope,
    ExpeditionTarget
} from "./survival-types";

export type PersistentEffectPolicy = {
    support: "None" | "Supported" | "Unsupported";
    effectKinds: string[];
    accumulationModel: string | null;
    recoveryModel: string | null;
    scope: ExpeditionEffectScope | null;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

export type ExpeditionEffect = {
    id: string;
    effectKey: string;
    target: ExpeditionTarget;
    mergeKey: string | null;
    level: number | null;
    magnitude: number | null;
    unit: string | null;
    state: string | null;
    movementComponents: unknown[];
    sourceConsequenceIds: string[];
    provenance: ConsequenceProvenance[];
    recoveryModel: string | null;
};

export type AppliedConsequence = {
    consequenceId: string;
    consequenceKey: string;
    status: ExpeditionConsequenceStatus;
    effectIds: string[];
    provenance: ConsequenceProvenance;
    detail: string | null;
    resolutionProvenance: ConsequenceProvenance | null;
};

export type PendingConsequence = {
    consequence: {
        id: string;
        consequenceKey: string;
        category: string;
        target: ExpeditionTarget;
        components: unknown[];
        provenance: ConsequenceProvenance;
        sourceReference: string | null;
        note: string | null;
    };
    status: ExpeditionConsequenceStatus;
    reason: string;
    requiredAction: string;
    unresolvedComponents: unknown[];
};

export type EffectAuditRecord = {
    id: string;
    effectId: string;
    effectKey: string;
    operation: string;
    beforeLevel: number | null;
    afterLevel: number | null;
    beforeMagnitude: number | null;
    afterMagnitude: number | null;
    consequenceId: string | null;
    provenance: ConsequenceProvenance;
};

export type ExpeditionEffectState = {
    policy: PersistentEffectPolicy;
    activeEffects: ExpeditionEffect[];
    appliedConsequences: AppliedConsequence[];
    pendingConsequences: PendingConsequence[];
    history: EffectAuditRecord[];
};

export type EffectOperation = {
    expedition: ExpeditionDetail;
    effects: ExpeditionEffectState;
    status: ExpeditionConsequenceStatus;
    detail: string;
};

export type RecoverEffectRequest = {
    expectedVersion: number;
    triggerKey: string;
    levelReduction: number | null;
    clear: boolean;
    provenance: ConsequenceProvenanceRequest;
};

export type ClearEffectRequest = {
    expectedVersion: number;
    provenance: ConsequenceProvenanceRequest;
};

export type ResolvePendingConsequenceRequest = {
    expectedVersion: number;
    resolutionNote: string;
    provenance: ConsequenceProvenanceRequest;
};
