import type { ExpeditionDetail } from "../../types";

export type FocusedIntervalPolicySupport = "None" | "Supported" | "Unsupported";

export type FocusedIntervalPolicyProjection = {
    support: FocusedIntervalPolicySupport;
    intervalHours: number | null;
    mechanicKey: string | null;
    mechanicVersion: number | null;
    executionHandler: string | null;
    unsupportedReason: string | null;
};

type ProcedureWithFocusedIntervalPolicy = ExpeditionDetail["procedure"] & {
    focusedIntervalPolicy: FocusedIntervalPolicyProjection;
};

export function focusedIntervalPolicy(runtime: ExpeditionDetail): FocusedIntervalPolicyProjection {
    return (runtime.procedure as ProcedureWithFocusedIntervalPolicy).focusedIntervalPolicy;
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
