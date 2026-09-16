import type { EncounterCadence, ResolutionSource } from "./types";

// AutomaticRoll is intentionally absent until a trusted helper actually generates
// the corresponding resolved value. The domain/API still support it for future
// helper and integration paths.
export const manualEntryResolutionSources: readonly ResolutionSource[] = [
    "ProcedureDefault",
    "ManualRoll",
    "ExternalSystem",
    "DmOverride"
];

// Custom remains a domain value for persisted legacy profiles. New expedition
// customization can offer it only after a real typed custom-cadence model exists.
export const newExpeditionEncounterCadences: readonly EncounterCadence[] = [
    "None",
    "PerWatch",
    "PerDay"
];
