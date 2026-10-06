import type { ExpeditionJourneyState } from "../../journey-types";
import type { SurvivalResources } from "../../survival-types";
import type { ExpeditionDetail, ProcedureModule, SpatialRuntimeExpedition } from "../../types";
import { canUseFocusedNonSpatialWatch } from "./focused-interval-policy";
import { navigationResolutionDue } from "./expedition-workflow";

export type ExpeditionWorkspaceActionKind =
    | "encounter"
    | "navigation"
    | "boundary"
    | "survival"
    | "journey"
    | "travel"
    | "watch"
    | "procedure";

export type ExpeditionWorkspaceAction = {
    kind: ExpeditionWorkspaceActionKind;
    label: string;
    detail: string;
    urgent: boolean;
};

export type ExpeditionWorkspaceCapabilities = {
    travel: boolean;
    navigation: boolean;
    activities: boolean;
    encounters: boolean;
    resources: boolean;
    survival: boolean;
    effects: boolean;
    journey: boolean;
    interval: boolean;
};

export type ExpeditionWorkspacePresentation = {
    action: ExpeditionWorkspaceAction;
    capabilities: ExpeditionWorkspaceCapabilities;
    timeLabel: string;
    routeLabel: string | null;
    navigationLabel: string | null;
    resourceLabel: string | null;
    journeyLabel: string | null;
};

export function expeditionWorkspacePresentation(
    runtime: ExpeditionDetail,
    journey: ExpeditionJourneyState | null,
    survival: SurvivalResources | null): ExpeditionWorkspacePresentation {
    const capabilities = expeditionWorkspaceCapabilities(runtime.procedure.modules);
    return {
        action: expeditionWorkspaceAction(runtime, journey, survival),
        capabilities,
        timeLabel: timeLabel(runtime, capabilities.interval),
        routeLabel: runtime.expedition.isSpatial ? routeLabel(runtime) : null,
        navigationLabel: runtime.expedition.isSpatial && capabilities.navigation
            ? navigationLabel(runtime)
            : null,
        resourceLabel: resourceLabel(survival),
        journeyLabel: journeyLabel(journey)
    };
}

export function expeditionWorkspaceCapabilities(modules: ProcedureModule[]): ExpeditionWorkspaceCapabilities {
    const keys = new Set(modules.map(module => module.moduleKey));
    const hasPrefix = (prefix: string): boolean => [...keys].some(key => key.startsWith(prefix));
    return {
        travel: keys.has("movement.resolution")
            || keys.has("movement.hex-progress")
            || keys.has("movement.budget")
            || keys.has("movement.terrain"),
        navigation: keys.has("navigation.check") || keys.has("navigation.outcome"),
        activities: keys.has("party.activities"),
        encounters: hasPrefix("encounters."),
        resources: keys.has("survival.resources"),
        survival: keys.has("exploration.foraging")
            || keys.has("survival.camping")
            || keys.has("time.forced-travel"),
        effects: keys.has("effects.expedition"),
        journey: keys.has("journey.process") || keys.has("journey.events"),
        interval: keys.has("time.interval")
    };
}

export function expeditionWorkspaceAction(
    runtime: ExpeditionDetail,
    journey: ExpeditionJourneyState | null,
    survival: SurvivalResources | null = null): ExpeditionWorkspaceAction {
    if (runtime.pauseReason === "EncounterTriggered") {
        return {
            kind: "encounter",
            label: "Resolve encounter",
            detail: "An encounter interrupted the expedition. Resolve it before continuing the current travel procedure.",
            urgent: true
        };
    }

    if (runtime.pauseReason === "LostRecognitionRequired") {
        return {
            kind: "boundary",
            label: "Resolve lost-party boundary decision",
            detail: "The party crossed a boundary while lost. Resolve recognition and reorientation before travel continues.",
            urgent: true
        };
    }

    if (runtime.pauseReason === "BacktrackBoundaryReached") {
        return {
            kind: "travel",
            label: "Review backtrack boundary",
            detail: "The deliberate double-back reached the known entry boundary. Review the resulting position and travel intent, then resume the same watch.",
            urgent: true
        };
    }

    if (runtime.pauseReason === "ConditionsReviewRequired") {
        return {
            kind: "travel",
            label: "Review changed travel conditions",
            detail: "The current travel segment reached a procedure boundary. Review only the travel inputs that changed, then resume the same watch.",
            urgent: true
        };
    }

    if (survival?.forcedTravel.checkDue || (survival?.pendingResourceConsequences.length ?? 0) > 0) {
        return {
            kind: "survival",
            label: survival?.forcedTravel.checkDue ? "Resolve forced travel" : "Resolve travel consequence",
            detail: survival?.forcedTravel.checkDue
                ? "A forced-travel check is due. Resolve it before routine travel continues."
                : "A resource or survival consequence is pending. Resolve it before routine travel continues.",
            urgent: true
        };
    }

    const journeyPending = journey?.activeProcesses.find(process =>
        process.status === "ResolutionRequired" || process.pendingActions.length > 0);
    if (journeyPending) {
        return {
            kind: "journey",
            label: "Resolve journey stage",
            detail: `${journeyPending.definition.displayName}: ${currentJourneyStageLabel(journeyPending)} needs a table resolution.`,
            urgent: true
        };
    }

    if (runtime.expedition.isSpatial
        && runtime.procedure.runtime !== null
        && navigationResolutionDue(runtime, false, false)) {
        return {
            kind: "navigation",
            label: "Resolve navigation",
            detail: "Navigation is due for the intended course. Resolve that check without advancing travel, then continue with the same course and pace.",
            urgent: true
        };
    }

    if (runtime.expedition.isSpatial
        && runtime.expedition.activeWatchNumber !== null
        && runtime.procedure.runtime !== null) {
        return {
            kind: "travel",
            label: `Resume watch ${runtime.expedition.activeWatchNumber}`,
            detail: "Continue the current watch with its persisted course and reusable travel choices. Only unresolved procedure inputs need new values.",
            urgent: false
        };
    }

    if (runtime.expedition.isSpatial && runtime.procedure.runtime !== null) {
        return {
            kind: "travel",
            label: `Run watch ${runtime.expedition.completedWatches + 1}`,
            detail: "Use the current travel intent and authoritative movement suggestion, then supply only procedure inputs that are still unresolved.",
            urgent: false
        };
    }

    if (!runtime.expedition.isSpatial && canUseFocusedNonSpatialWatch(runtime)) {
        const activeWatch = runtime.expedition.activeWatchNumber;
        return {
            kind: "watch",
            label: activeWatch === null
                ? `Run watch ${runtime.expedition.completedWatches + 1}`
                : `Resume watch ${activeWatch}`,
            detail: activeWatch === null
                ? "Advance the configured interval without fabricating spatial state."
                : "Continue the active configured interval without fabricating spatial travel state.",
            urgent: false
        };
    }

    const activeJourney = journey?.activeProcesses.find(process => !process.isTerminal);
    if (activeJourney) {
        return {
            kind: "journey",
            label: "Continue journey",
            detail: `${activeJourney.definition.displayName}: ${currentJourneyStageLabel(activeJourney)}.`,
            urgent: false
        };
    }

    if (journey?.processPolicy.support === "Supported") {
        return {
            kind: "journey",
            label: "Start or manage journey",
            detail: "This procedure supports a multi-stage journey process without requiring a fabricated map or travel watch.",
            urgent: false
        };
    }

    return {
        kind: "procedure",
        label: "Review procedure",
        detail: "This expedition has no currently executable travel or journey action. The materialized procedure remains authoritative for reference and manual play.",
        urgent: false
    };
}

function timeLabel(runtime: ExpeditionDetail, hasInterval: boolean): string {
    const state = runtime.expedition;
    if (!hasInterval) {
        return `Day ${state.currentDay} · ${formatNumber(state.elapsedTravelHours)} h elapsed`;
    }
    if (state.activeWatchNumber !== null) {
        const remaining = state.activeWatchRemainingHours;
        return remaining === null
            ? `Day ${state.currentDay} · watch ${state.activeWatchNumber} active`
            : `Day ${state.currentDay} · watch ${state.activeWatchNumber} · ${formatNumber(remaining)} h remaining`;
    }
    return `Day ${state.currentDay} · ${state.completedWatches} watch${state.completedWatches === 1 ? "" : "es"} complete`;
}

function routeLabel(runtime: ExpeditionDetail): string {
    const state = runtime.expedition as SpatialRuntimeExpedition;
    const intended = state.intendedDirection === null ? "No course selected" : "Course selected";
    return `Current cell · ${intended}`;
}

function navigationLabel(runtime: ExpeditionDetail): string {
    const state = runtime.expedition as SpatialRuntimeExpedition;
    const actualDiffers = state.actualDirection !== null
        && state.intendedDirection !== null
        && state.actualDirection !== state.intendedDirection;
    if (state.isLost) {
        return `Lost · veer ${state.veerSteps}${actualDiffers ? " · actual course differs" : ""}`;
    }
    if (actualDiffers) {
        return "Off course · actual course differs";
    }
    return "On course";
}

function resourceLabel(state: SurvivalResources | null): string | null {
    if (!state) return null;
    if (state.resourcePolicy.support === "None" && state.resources.length === 0) return null;
    const pending = state.pendingResourceConsequences.length;
    const depleted = state.resources.filter(resource => resource.isDepleted).length;
    if (pending > 0) return `${pending} pending resource consequence${pending === 1 ? "" : "s"}`;
    if (depleted > 0) return `${depleted} depleted resource${depleted === 1 ? "" : "s"}`;
    if (state.resources.length > 0) return `${state.resources.length} tracked resource${state.resources.length === 1 ? "" : "s"}`;
    return "Resource procedure active";
}

function journeyLabel(state: ExpeditionJourneyState | null): string | null {
    if (!state || state.processPolicy.support === "None") return null;
    const active = state.activeProcesses.find(process => !process.isTerminal);
    if (!active) return "No active journey";
    return `${active.definition.displayName} · ${currentJourneyStageLabel(active)}`;
}

function currentJourneyStageLabel(process: ExpeditionJourneyState["activeProcesses"][number]): string {
    return process.definition.stages.find(stage => stage.stageKey === process.currentStageKey)?.displayName
        ?? process.currentStageKey;
}


function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}
