import type { HexCoordinate, MovementCapabilityComposition } from "./types";

export type EnvironmentValueKind = "Tag" | "Measurement";
export type EnvironmentAnnotationScopeKind = "World" | "Hex" | "SpatialFeature";
export type EnvironmentContextStatus = "Resolved" | "RequiresAdjudication" | "Unavailable";
export type EnvironmentFactSourceKind = "World" | "Hex" | "SpatialFeature" | "ExpeditionCurrent" | "DmOverride";
export type EnvironmentProcedureEvaluationStatus = "Resolved" | "Partial" | "RequiresAdjudication";

export type EnvironmentMeasurement = {
    value: number;
    unit: string;
};

export type EnvironmentFact = {
    id: string;
    dimension: string;
    valueKind: EnvironmentValueKind;
    tag: string | null;
    measurement: EnvironmentMeasurement | null;
    provenance: string | null;
    note: string | null;
};

export type EnvironmentAnnotationScope = {
    kind: EnvironmentAnnotationScopeKind;
    hex: HexCoordinate | null;
    featureId: string | null;
};

export type EnvironmentAnnotation = {
    id: string;
    scope: EnvironmentAnnotationScope;
    facts: EnvironmentFact[];
};

export type WorldEnvironment = {
    overworldId: string;
    version: number;
    annotations: EnvironmentAnnotation[];
};

export type EnvironmentFactSource = {
    kind: EnvironmentFactSourceKind;
    annotationId: string | null;
    hex: HexCoordinate | null;
    featureId: string | null;
    featureName: string | null;
};

export type EffectiveEnvironmentFact = {
    fact: EnvironmentFact;
    source: EnvironmentFactSource;
    precedence: number;
    effective: boolean;
};

export type EnvironmentConflict = {
    dimension: string;
    candidates: EffectiveEnvironmentFact[];
    detail: string;
};

export type EffectiveEnvironmentContext = {
    status: EnvironmentContextStatus;
    facts: EffectiveEnvironmentFact[];
    conflicts: EnvironmentConflict[];
};

export type ExpeditionEnvironmentState = {
    currentFacts: EnvironmentFact[];
    overrides: EnvironmentFact[];
};

export type EnvironmentProcedureEvaluation = {
    status: EnvironmentProcedureEvaluationStatus;
    terrainKey: string | null;
    routeKey: string | null;
    weatherAdjustmentModel: string | null;
    missingInputs: string[];
    unsupportedSemantics: string[];
    diagnostics: string[];
    provenance: string[];
};

export type EnvironmentWorkbench = {
    state: ExpeditionEnvironmentState;
    effectiveContext: EffectiveEnvironmentContext;
    evaluation: EnvironmentProcedureEvaluation;
    movementComposition: MovementCapabilityComposition;
};

export type ReplaceWorldEnvironmentRequest = {
    expectedVersion: number;
    annotations: EnvironmentAnnotation[];
};

export type UpdateExpeditionEnvironmentRequest = {
    expectedVersion: number;
    currentFacts: EnvironmentFact[];
    overrides: EnvironmentFact[];
};
