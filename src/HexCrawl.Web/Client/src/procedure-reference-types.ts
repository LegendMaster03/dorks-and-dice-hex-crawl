export interface ProcedureReferenceInputSource {
    key: string;
    label: string;
}

export interface ProcedureReferenceNamedValue {
    key: string;
    displayName: string;
}

export interface ProcedureReferenceMapEntry {
    key: string;
    value: string;
}

export interface ProcedureReferenceParameter {
    key: string;
    displayName: string;
    type: string;
    description: string | null;
    rawValue: string;
    displayValue: string;
    isUnknown: boolean;
    listValues: string[];
    mapEntries: ProcedureReferenceMapEntry[];
    technicalDetail: string | null;
}

export interface ProcedureReferenceInput {
    key: string;
    displayName: string;
    producerModules: string[];
    allowedSources: ProcedureReferenceInputSource[];
}

export interface ProcedureReferenceDiagnostic {
    kind: string;
    message: string;
    inputKey: string | null;
    allowedSources: ProcedureReferenceInputSource[];
}

export interface ProcedureReferenceMechanic {
    key: string;
    displayName: string;
    description: string;
    version: number;
    executionHandler: string;
    automationLevel: string;
    executionSupport: "Native" | "Declarative" | "Unsupported";
    executionStatus: string;
    compatibilityTags: string[];
}

export interface ProcedureReferenceModule {
    moduleKey: string;
    category: string;
    section: string;
    displayName: string;
    purpose: string;
    executionStage: string;
    presentationMetadata: Record<string, string>;
    mechanic: ProcedureReferenceMechanic;
    parameters: ProcedureReferenceParameter[];
    requiredInputs: ProcedureReferenceInput[];
    outputs: ProcedureReferenceNamedValue[];
    diagnostics: ProcedureReferenceDiagnostic[];
    isModified: boolean;
    modificationCount: number;
    modificationNotes: string[];
}

export interface ProcedureReferenceSection {
    name: string;
    modules: ProcedureReferenceModule[];
}

export interface ProcedureReferenceOrigin {
    presetKey: string | null;
    presetDisplayName: string | null;
    presetRevision: number | null;
    attribution: string | null;
    disclaimer: string | null;
}

export interface ProcedureReferenceDependencyIssue {
    kind: string;
    moduleKey: string;
    message: string;
    inputKey: string | null;
    allowedInputSources: string[];
}

export interface ProcedureReferenceDependencyReport {
    hasErrors: boolean;
    issues: ProcedureReferenceDependencyIssue[];
}

export interface ProcedureReference {
    procedureId: string;
    revision: number;
    key: string;
    name: string;
    isExecutable: boolean;
    modificationCount: number;
    modifiedModuleCount: number;
    origin: ProcedureReferenceOrigin | null;
    dependencies: ProcedureReferenceDependencyReport;
    sections: ProcedureReferenceSection[];
}
