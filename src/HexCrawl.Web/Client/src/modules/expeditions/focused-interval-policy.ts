import type { ExpeditionDetail, FocusedIntervalPolicy } from "../../types";

export type FocusedIntervalProcedurePresentation = {
    procedureLabel: string;
    executionLabel: "Executable" | "Structural";
};

export function focusedIntervalPolicy(runtime: ExpeditionDetail): FocusedIntervalPolicy {
    return runtime.procedure.focusedIntervalPolicy;
}

export function focusedIntervalProcedurePresentation(runtime: ExpeditionDetail): FocusedIntervalProcedurePresentation {
    const executable = runtime.procedure.runtime !== null;
    return {
        procedureLabel: executable
            ? runtime.procedure.name
            : `${runtime.procedure.name} · structural`,
        executionLabel: executable ? "Executable" : "Structural"
    };
}

export function focusedIntervalHours(runtime: ExpeditionDetail): number | null {
    const policy = focusedIntervalPolicy(runtime);
    if (policy.support !== "Supported"
        || policy.intervalHours === null
        || !Number.isFinite(policy.intervalHours)
        || policy.intervalHours <= 0) {
        return null;
    }
    return policy.intervalHours;
}

export function canUseFocusedNonSpatialWatch(runtime: ExpeditionDetail): boolean {
    return !runtime.expedition.isSpatial && focusedIntervalHours(runtime) !== null;
}

export function focusedIntervalUnavailableMessage(runtime: ExpeditionDetail): string {
    const policy = focusedIntervalPolicy(runtime);
    if (policy.support === "None") {
        return "The current procedure does not define a repeating interval.";
    }
    if (policy.support === "Unsupported") {
        return policy.unsupportedReason
            ?? "The stored interval mechanic is not supported by focused interval bookkeeping.";
    }
    return "Focused interval bookkeeping is unavailable because the stored interval duration is invalid.";
}
