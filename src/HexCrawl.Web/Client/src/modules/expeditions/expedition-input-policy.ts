import type { ResolutionSource } from "../../types";

// AutomaticRoll is intentionally absent until a trusted helper actually generates
// the corresponding resolved value. The domain/API still support it for future
// helper and integration paths.
export const manualEntryResolutionSources: readonly ResolutionSource[] = [
    "ProcedureDefault",
    "ManualRoll",
    "ExternalSystem",
    "DmOverride"
];
