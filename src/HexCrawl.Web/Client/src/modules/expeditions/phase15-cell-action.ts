import type { ExpeditionWorkspaceAction } from "./expedition-workspace-model.js";
import type { ExpeditionJourneyState } from "../../journey-types.js";
import type { SurvivalResources } from "../../survival-types.js";

/**
 * Projection from persisted cell-watch and gameplay state to the Phase 15
 * next-action vocabulary. Neither geometry nor runtime rules are synthesized.
 */
export function nextCellExpeditionAction(state: {
    pendingEncounter: boolean;
    pauseReason: string | null;
    executable: boolean;
    hasSelectedCourse: boolean;
    activeWatchNumber: number | null;
    survival: SurvivalResources | null;
    journey: ExpeditionJourneyState | null;
    navigationDue: boolean;
    attentionStatus: "loading" | "ready" | "error";
}): ExpeditionWorkspaceAction {
    const pause = state.pauseReason;
    if (state.pendingEncounter || pause === "EncounterTriggered") return {
        kind: "encounter", label: "Resolve encounter",
        detail: "An encounter interrupted travel. Complete its table resolution before continuing the watch.",
        urgent: true
    };
    if (pause === "CellCourseAdjudicationRequired") return {
        kind: "boundary", label: "Resolve boundary course",
        detail: "Multiple atomic interfaces meet the attempted course. Choose the confirmed exit.",
        urgent: true
    };
    if (pause === "LostRecognitionRequired") return {
        kind: "boundary", label: "Resolve lost-party decision",
        detail: "Decide whether the party recognizes its lost course and reorients.",
        urgent: true
    };
    if (pause !== null) return {
        kind: "travel", label: "Review paused travel",
        detail: "Review the saved watch and its remaining travel before resuming.",
        urgent: true
    };
    if (!state.executable) return {
        kind: "procedure", label: "Review exploration rules",
        detail: "This saved ruleset has no executable cell movement procedure.",
        urgent: false
    };
    if (state.attentionStatus !== "ready") return {
        kind: "travel",
        label: state.attentionStatus === "loading" ? "Loading gameplay state" : "Review gameplay state",
        detail: state.attentionStatus === "loading"
            ? "Checking saved survival and journey requirements before proposing the next action."
            : "Survival or journey state could not be loaded. Reload the expedition before continuing.",
        urgent: state.attentionStatus === "error"
    };
    if (state.survival?.forcedTravel.checkDue || (state.survival?.pendingResourceConsequences.length ?? 0) > 0) return {
        kind: "survival",
        label: state.survival?.forcedTravel.checkDue ? "Resolve forced travel" : "Resolve travel consequence",
        detail: state.survival?.forcedTravel.checkDue
            ? "A forced-travel check is due before routine travel continues."
            : "A pending resource or survival consequence requires resolution.",
        urgent: true
    };
    const event = state.journey?.eventOccurrences.find(item => item.status === "ResolutionRequired");
    if (event) return {
        kind: "journey", label: "Resolve journey event",
        detail: `${event.eventType ?? event.eventKey ?? "Journey event"} requires table resolution.`,
        urgent: true
    };
    const process = state.journey?.activeProcesses.find(item =>
        item.status === "ResolutionRequired" || item.pendingActions.length > 0);
    if (process) return {
        kind: "journey", label: "Resolve journey stage",
        detail: process.pendingActions[0]?.detail?.trim()
            ? `${process.definition.displayName}: ${process.pendingActions[0].detail}`
            : `${process.definition.displayName}: a stage resolution is required.`,
        urgent: true
    };
    if (!state.hasSelectedCourse) return {
        kind: "travel", label: "Choose travel direction",
        detail: "Select an adjacent atomic boundary in the current-cell navigator. Choosing a course does not move the party.",
        urgent: false
    };
    if (state.navigationDue) return {
        kind: "navigation", label: "Resolve navigation",
        detail: "Record the due navigation result in the travel/watch form before advancing.",
        urgent: true
    };
    return {
        kind: "travel",
        label: state.activeWatchNumber === null ? "Continue travel" : `Resume watch ${state.activeWatchNumber}`,
        detail: "Use the selected course and pace; supply only the DM decisions required by the saved ruleset.",
        urgent: false
    };
}
