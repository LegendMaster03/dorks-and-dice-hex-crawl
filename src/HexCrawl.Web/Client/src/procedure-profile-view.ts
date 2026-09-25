import { formatHours } from "./runtime-view.js";
import type { DiceRollFormula, RuntimeProfile } from "./types";

export function procedureProfileSummary(profile: RuntimeProfile): string {
    return `${formatHours(profile.watchHours)} watches · ${profile.travelResolution === "HexSteps"
        ? "hex-step travel"
        : profile.actualDistanceResolution === "Fixed"
            ? "fixed distance"
            : "resolved variable distance"} · ${profile.usesNavigationChecks
        ? "navigation checks"
        : "no navigation checks"} · encounters ${prettyWords(profile.encounterCadence)}`;
}

export function renderProcedureMechanicList(host: HTMLElement, profile: RuntimeProfile): void {
    host.replaceChildren();
    for (const line of procedureMechanicLines(profile)) {
        const item = document.createElement("li");
        item.textContent = line;
        host.append(item);
    }
}

export function procedureMechanicLines(profile: RuntimeProfile): string[] {
    const travel = profile.travelResolution === "HexSteps"
        ? "Travel: resolved hex-step movement."
        : profile.actualDistanceResolution === "VariableResolved"
            ? "Travel: continuous distance with a separately resolved expected and actual distance."
            : "Travel: continuous distance with a fixed resolved movement amount.";

    const navigation = profile.usesNavigationChecks
        ? `Navigation: checks enabled; veer is ${profile.usesPersistentVeer ? "persistent" : "not persistent"}; deliberate single-hex double-back ${profile.supportsDeliberateDoubleBack ? "supported" : "not supported"}.`
        : "Navigation: procedure checks disabled.";

    const progress = profile.tracksIntraHexProgress
        ? `Progress: intra-hex tracking enabled; exit factors start ${formatNumber(profile.startingExitProgressFactor)}, near ${formatNumber(profile.nearExitProgressFactor)}, far ${formatNumber(profile.farExitProgressFactor)}, back ${formatNumber(profile.backExitProgressFactor)}${profile.directionChangesCostProgress ? `; direction-change cost factor ${formatNumber(profile.directionChangeProgressCostFactor)}` : ""}.`
        : "Progress: discrete hex steps; no intra-hex progress tracking.";

    return [
        `Watch: ${formatHours(profile.watchHours)}; encounter cadence ${prettyWords(profile.encounterCadence)}.`,
        travel,
        navigation,
        progress,
        ...procedureHelperLines(profile)
    ];
}

export type ProcedureHelperMechanics = {
    travel: string | null;
    navigation: string | null;
    encounter: string | null;
};

export function procedureHelperMechanics(profile: RuntimeProfile): ProcedureHelperMechanics {
    const helpers = profile.resolutionHelpers;
    if (!helpers) return { travel: null, navigation: null, encounter: null };

    return {
        travel: helpers.travel
            && profile.travelResolution === "ContinuousDistance"
            && profile.actualDistanceResolution === "VariableResolved"
            ? `actual distance = expected distance × ${formatDiceFormula(helpers.travel.roll)} total × ${formatNumber(helpers.travel.distanceFactorPerRollPoint)}.`
            : null,
        navigation: helpers.navigation && profile.usesNavigationChecks
            ? `${formatDiceFormula(helpers.navigation.checkRoll)} + the entered situational modifier vs. the DM-confirmed DC; a failed check uses the DM-confirmed non-zero veer.`
            : null,
        encounter: helpers.encounter && profile.encounterCadence !== "None"
            ? `${formatDiceFormula(helpers.encounter.checkRoll)}; wandering on ${formatResultSet(helpers.encounter.wanderingResults)}, keyed location on ${formatResultSet(helpers.encounter.keyedLocationResults)}; encounter time uses 1d${helpers.encounter.timingSlots} equal watch slots.`
            : null
    };
}

function procedureHelperLines(profile: RuntimeProfile): string[] {
    const mechanics = procedureHelperMechanics(profile);
    const lines: string[] = [];
    if (mechanics.travel) lines.push(`Travel helper: ${mechanics.travel}`);
    if (mechanics.navigation) lines.push(`Navigation helper: ${mechanics.navigation}`);
    if (mechanics.encounter) lines.push(`Encounter helper: ${mechanics.encounter}`);

    if (lines.length > 0) return lines;
    return profile.resolutionHelpers
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
