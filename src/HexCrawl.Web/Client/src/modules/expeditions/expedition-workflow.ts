import type { ExpeditionDetail } from "../../types";

export type WatchPhase = "ready" | "active" | "paused";

export function watchPhase(runtime: ExpeditionDetail): WatchPhase {
    if (runtime.expedition.activeWatchNumber === null) return "ready";
    return runtime.pauseReason === null ? "active" : "paused";
}

export function encounterCheckDue(runtime: ExpeditionDetail): boolean {
    if (runtime.expedition.activeWatchNumber !== null || runtime.procedure.runtime === null) return false;
    return encounterCheckDueForWatch(runtime, runtime.expedition.completedWatches + 1);
}

export function navigationResolutionDue(runtime: ExpeditionDetail, suppressesNavigationCheck: boolean, deliberateDoubleBack: boolean): boolean {
    const procedureRuntime = runtime.procedure.runtime;
    if (runtime.expedition.activeWatchNumber !== null
        || procedureRuntime === null
        || !procedureRuntime.usesNavigationChecks
        || suppressesNavigationCheck
        || deliberateDoubleBack) {
        return false;
    }

    const watchNumber = runtime.expedition.completedWatches + 1;
    return !runtime.history.some(event =>
        event.kind === "NavigationCheckResolved"
        && event.watchNumber === watchNumber);
}

export type SpatialTravelContinuationTarget =
    | "unavailable"
    | "course"
    | "navigation"
    | "encounter"
    | "movement"
    | "boundary"
    | "advance";

export function spatialTravelContinuationTarget(
    runtime: ExpeditionDetail,
    hasSelectedCourse: boolean,
    hasResolvedMovement: boolean,
    suppressesNavigationCheck = false,
    deliberateDoubleBack = false): SpatialTravelContinuationTarget {
    if (!runtime.expedition.isSpatial || runtime.procedure.runtime === null) return "unavailable";
    if (runtime.pauseReason === "EncounterTriggered") return "encounter";
    if (runtime.pauseReason === "LostRecognitionRequired") return "boundary";
    if (!hasSelectedCourse) return "course";
    if (navigationResolutionDue(runtime, suppressesNavigationCheck, deliberateDoubleBack)) return "navigation";
    if (encounterCheckDue(runtime)) return "encounter";
    if (!hasResolvedMovement) return "movement";
    return "advance";
}

export function watchActionLabel(runtime: ExpeditionDetail): string {
    if (runtime.procedure.runtime === null) return "Procedure not executable";
    if (runtime.expedition.activeWatchNumber === null) return "Run watch";
    return `Resume watch ${runtime.expedition.activeWatchNumber}`;
}

export function pauseInstruction(runtime: ExpeditionDetail): string | null {
    switch (runtime.pauseReason) {
        case "ConditionsReviewRequired":
            return "A hex boundary was crossed before the watch ended. Review the new travel conditions, then resume this same watch with its remaining time.";
        case "LostRecognitionRequired":
            return "The party crossed a boundary while lost. Resolve whether the party recognizes the problem and whether it reorients before travel continues.";
        case "EncounterTriggered":
            return "An encounter interrupted the watch. Resolve it at the table, then resume this same watch with its remaining time.";
        case "BacktrackBoundaryReached":
            return "The deliberate double-back reached the known entry boundary. Review the resulting position before resuming travel.";
        default:
            return null;
    }
}

export function assistantEncounterCheckDue(runtime: ExpeditionDetail): boolean {
    if (runtime.procedure.runtime === null) return false;
    if (runtime.expedition.isSpatial && runtime.expedition.activeWatchNumber !== null) return false;
    const watchNumber = runtime.expedition.activeWatchNumber ?? runtime.expedition.completedWatches + 1;
    return encounterCheckDueForWatch(runtime, watchNumber);
}

function encounterCheckDueForWatch(runtime: ExpeditionDetail, watchNumber: number): boolean {
    const cadence = runtime.procedure.runtime?.encounterCadence;
    if (!cadence || cadence === "None") return false;
    if (cadence === "PerWatch") {
        return !runtime.history.some(event =>
            event.kind === "EncounterCheckPerformed"
            && event.watchNumber === watchNumber);
    }

    const dayIndex = runtime.expedition.currentDay - 1;
    return !runtime.history.some(event =>
        event.kind === "EncounterCheckPerformed"
        && Math.floor(event.expeditionElapsedHours / 24) === dayIndex);
}
