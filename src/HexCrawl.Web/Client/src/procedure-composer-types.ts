import type { ProcedureAutomationLevel } from "./types";

export type ProcedureExecutionSupport = "Native" | "Declarative" | "Unsupported";

export type ProcedureParameterDefinition = {
    type: string;
    required: boolean;
    description: string | null;
    defaultValue: string | null;
};

export type ProcedureInput = {
    inputKey: string;
    allowedSources: string[];
};

export type ProcedureMechanicOption = {
    key: string;
    displayName: string;
    description: string;
    version: number;
    executionHandler: string;
    automationLevel: ProcedureAutomationLevel;
    executionSupport: ProcedureExecutionSupport;
    inputs: ProcedureInput[];
    outputs: string[];
    parameterSchema: Record<string, ProcedureParameterDefinition>;
    compatibilityTags: string[];
};

export type ProcedureDependencyIssueKind =
    | "MissingRequiredModule"
    | "MissingRequiredProducer"
    | "UnresolvedInput"
    | "ManualInputRequired"
    | "OptionalProviderInputRequired"
    | "ExternalInputRequired"
    | "ProducedButUnused"
    | "IncompatibleMechanic";

export type ProcedureDependencyIssue = {
    kind: ProcedureDependencyIssueKind;
    moduleKey: string;
    message: string;
    inputKey: string | null;
    allowedInputSources: string[];
};

export type ProcedureOverride = {
    overrideId: string;
    moduleKey: string;
    replacementMechanicKey: string | null;
    replacementMechanicVersion: number | null;
    parameters: Record<string, string>;
    note: string | null;
};

export type ProcedureModuleComposer = {
    moduleKey: string;
    category: string;
    displayName: string;
    purpose: string;
    executionStage: string;
    reads: string[];
    produces: string[];
    requiredDependencies: string[];
    optionalDependencies: string[];
    presentationMetadata: Record<string, string>;
    mechanic: ProcedureMechanicOption;
    alternatives: ProcedureMechanicOption[];
    configurationSchema: Record<string, ProcedureParameterDefinition>;
    parameters: Record<string, string>;
    requiredInputs: ProcedureInput[];
    outputs: string[];
    dependencyIssues: ProcedureDependencyIssue[];
    isModified: boolean;
    modificationCount: number;
    validationIssues: string[];
};

export type ProcedureOrigin = {
    presetKey: string | null;
    presetDisplayName: string | null;
    presetRevision: number | null;
    attribution: string | null;
    disclaimer: string | null;
};

export type ProcedureDependencyReport = {
    hasErrors: boolean;
    issues: ProcedureDependencyIssue[];
};

export type ProcedureComposer = {
    procedureId: string;
    revision: number;
    key: string;
    name: string;
    isExecutable: boolean;
    modificationCount: number;
    modifiedModuleCount: number;
    origin: ProcedureOrigin | null;
    modules: ProcedureModuleComposer[];
    dependencies: ProcedureDependencyReport;
    overrides: ProcedureOverride[];
};

export type SavedProcedureSummary = {
    procedureId: string;
    revision: number;
    key: string;
    name: string;
    campaignId: string | null;
    originPresetKey: string | null;
    originPresetDisplayName: string | null;
    isExecutable: boolean;
    moduleCount: number;
    createdAt: string;
};

export type ProcedureComposerModuleSelectionInput = {
    moduleKey: string;
    included: boolean;
};

export type ProcedureComposerOverrideInput = {
    overrideId: string;
    moduleKey: string;
    replacementMechanicKey: string | null;
    replacementMechanicVersion: number | null;
    parameters: Record<string, string>;
    note: string | null;
};

export type ProcedureComposerDraftInput = {
    presetKey?: string | null;
    procedureId?: string | null;
    revision?: number | null;
    name?: string | null;
    moduleSelections?: ProcedureComposerModuleSelectionInput[];
    overrides?: ProcedureComposerOverrideInput[];
};

export type ProcedureComposerCreateInput = {
    presetKey?: string | null;
    campaignId?: string | null;
    name?: string | null;
    moduleSelections?: ProcedureComposerModuleSelectionInput[];
    overrides?: ProcedureComposerOverrideInput[];
};

export type ProcedureComposerRevisionInput = {
    expectedRevision: number;
    name?: string | null;
    moduleSelections?: ProcedureComposerModuleSelectionInput[];
    overrides: ProcedureComposerOverrideInput[];
};

export type ProcedureRevisionSummary = {
    procedureId: string;
    revision: number;
    name: string;
    modificationCount: number;
    createdAt: string;
};

export type ProcedureCanonicalJson = {
    procedureId: string;
    revision: number;
    canonicalJson: string;
};

export type ProcedureCanonicalValidation = {
    isValid: boolean;
    procedureId: string | null;
    revision: number | null;
    error: string | null;
    lineNumber: number | null;
    bytePositionInLine: number | null;
};

export type ProcedureCanonicalCreateInput = {
    canonicalJson: string;
    presetKey?: string | null;
    campaignId?: string | null;
};

export type ProcedureCanonicalRevisionInput = {
    expectedRevision: number;
    canonicalJson: string;
};
