import type { ExpeditionWorkspaceAction } from "./expedition-workspace-model.js";

/**
 * Projection from V2 persisted cell-watch state to the unchanged Phase 15
 * next-action vocabulary. Neither geometry nor runtime rules are synthesized.
 */
export function nextCellExpeditionAction(state: {
    pendingEncounter: boolean;
    pauseReason: string | null;
    executable: boolean;
    hasSelectedCourse: boolean;
    activeWatchNumber: number | null;
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
    if (!state.hasSelectedCourse) return {
        kind: "travel", label: "Choose travel direction",
        detail: "Select an adjacent atomic boundary in the current-cell navigator. Choosing a course does not move the party.",
        urgent: false
    };
    return {
        kind: "travel",
        label: state.activeWatchNumber === null ? "Continue travel" : `Resume watch ${state.activeWatchNumber}`,
        detail: "Use the selected course and pace; supply only the DM decisions required by the saved ruleset.",
        urgent: false
    };
}
