import { formatHours } from "./runtime-view.js";
import type { CampaignProcedure, DiceRollFormula, ProcedureRuntime } from "./types";

export function campaignProcedureSummary(procedure: CampaignProcedure): string {
    const runtime = procedure.runtime;
    if (!runtime) {
        return `${procedure.modules.length} configured rules · manual or reference behavior · not fully automated`;
    }

    return `${formatHours(runtime.intervalHours)} watches · ${runtime.travelResolution === "HexSteps"
        ? "hex-step travel"
        : runtime.actualDistanceResolution === "Fixed"
            ? "fixed distance"
            : "resolved variable distance"} · ${runtime.usesNavigationChecks
        ? "navigation checks"
        : "no navigation checks"} · encounters ${prettyWords(runtime.encounterCadence)}`;
}

export function renderProcedureMechanicList(host: HTMLElement, procedure: CampaignProcedure): void {
    host.replaceChildren();
    for (const line of procedureMechanicLines(procedure)) {
        const item = document.createElement("li");
        item.textContent = line;
        host.append(item);
    }
}

export function procedureMechanicLines(procedure: CampaignProcedure): string[] {
    const runtime = procedure.runtime;
    if (!runtime) {
        return procedure.modules.length > 0
            ? procedure.modules.map(module =>
                `${module.moduleName}: ${module.mechanicKey} v${module.mechanicVersion} · ${module.automationLevel.toLowerCase()} · ${module.executionHandler}.`)
            : ["This ruleset contains no selected rules."];
    }

    const travel = runtime.travelResolution === "HexSteps"
        ? "Travel: resolved hex-step movement."
        : runtime.actualDistanceResolution === "VariableResolved"
            ? "Travel: continuous distance with a separately resolved expected and actual distance."
            : "Travel: continuous distance with a fixed resolved movement amount.";

    const navigation = runtime.usesNavigationChecks
        ? `Navigation: checks enabled; veer is ${runtime.usesPersistentVeer ? "persistent" : "not persistent"}; deliberate single-hex double-back ${runtime.supportsDeliberateDoubleBack ? "supported" : "not supported"}.`
        : "Navigation: procedure checks disabled.";

    const progress = runtime.tracksIntraHexProgress
        ? `Progress: intra-hex tracking enabled; exit factors start ${formatNumber(runtime.startingExitProgressFactor)}, near ${formatNumber(runtime.nearExitProgressFactor)}, far ${formatNumber(runtime.farExitProgressFactor)}, back ${formatNumber(runtime.backExitProgressFactor)}${runtime.directionChangesCostProgress ? `; direction-change cost factor ${formatNumber(runtime.directionChangeProgressCostFactor)}` : ""}.`
        : "Progress: discrete hex steps; no intra-hex progress tracking.";

    return [
        `Watch: ${formatHours(runtime.intervalHours)}; encounter cadence ${prettyWords(runtime.encounterCadence)}.`,
        travel,
        navigation,
        progress,
        ...procedureHelperLines(runtime),
        ...procedure.modules.map(module =>
            `${module.moduleName}: ${module.mechanicKey} v${module.mechanicVersion} · ${module.automationLevel.toLowerCase()}.`)
    ];
}

export type ProcedureHelperMechanics = {
    travel: string | null;
    navigation: string | null;
    encounter: string | null;
};

export function procedureHelperMechanics(procedure: CampaignProcedure): ProcedureHelperMechanics {
    const runtime = procedure.runtime;
    if (!runtime) return { travel: null, navigation: null, encounter: null };
    return runtimeHelperMechanics(runtime);
}

function runtimeHelperMechanics(runtime: ProcedureRuntime): ProcedureHelperMechanics {
    const helpers = runtime.resolutionHelpers;
    if (!helpers) return { travel: null, navigation: null, encounter: null };

    return {
        travel: helpers.travel
            && runtime.travelResolution === "ContinuousDistance"
            && runtime.actualDistanceResolution === "VariableResolved"
            ? `actual distance = expected distance × ${formatDiceFormula(helpers.travel.roll)} total × ${formatNumber(helpers.travel.distanceFactorPerRollPoint)}.`
            : null,
        navigation: helpers.navigation && runtime.usesNavigationChecks
            ? `${formatDiceFormula(helpers.navigation.checkRoll)} + the entered situational modifier vs. the DM-confirmed DC; a failed check uses the DM-confirmed non-zero veer.`
            : null,
        encounter: helpers.encounter && runtime.encounterCadence !== "None"
            ? `${formatDiceFormula(helpers.encounter.checkRoll)}; wandering on ${formatResultSet(helpers.encounter.wanderingResults)}, keyed location on ${formatResultSet(helpers.encounter.keyedLocationResults)}; encounter time uses 1d${helpers.encounter.timingSlots} equal watch slots.`
            : null
    };
}

function procedureHelperLines(runtime: ProcedureRuntime): string[] {
    const mechanics = runtimeHelperMechanics(runtime);
    const lines: string[] = [];
    if (mechanics.travel) lines.push(`Travel helper: ${mechanics.travel}`);
    if (mechanics.navigation) lines.push(`Navigation helper: ${mechanics.navigation}`);
    if (mechanics.encounter) lines.push(`Encounter helper: ${mechanics.encounter}`);

    if (lines.length > 0) return lines;
    return runtime.resolutionHelpers
        ? ["Automatic helpers: configured components are not applicable to the active procedure mechanics."]
        : ["Automatic helpers: none configured."];
}

function formatDiceFormula(formula: DiceRollFormula): string {
    const modifier = formula.modifier > 0
        ? `+${formula.modifier}`
        : formula.modifier < 0
            ? String(formula.modifier)
            : "";
    return `${formula.diceCount}d${formula.dieSides}${modifier}`;
}

function formatResultSet(values: number[]): string {
    return values.length > 0 ? values.join(", ") : "no configured results";
}

function formatNumber(value: number): string {
    return Number.isInteger(value)
        ? String(value)
        : value.toFixed(3).replace(/0+$/, "").replace(/\.$/, "");
}

function prettyWords(value: string): string {
    return value.replace(/([a-z0-9])([A-Z])/g, "$1 $2").toLowerCase();
}
