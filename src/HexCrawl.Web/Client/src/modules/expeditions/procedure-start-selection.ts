import type { SavedProcedureSummary } from "../../procedure-composer-types";
import type { ProcedurePreset, StartExpeditionInput, StartStandaloneCrawlSessionInput } from "../../types";

const SAVED_PREFIX = "saved:";
const PRESET_PREFIX = "preset:";

export type ProcedureStartChoice =
    | { kind: "saved"; procedureId: string; revision: number }
    | { kind: "preset"; presetKey: string };

export function populateProcedureStartChoices(
    select: HTMLSelectElement,
    saved: SavedProcedureSummary[],
    presets: ProcedurePreset[]): void {
    select.replaceChildren();
    if (saved.length > 0) {
        const group = document.createElement("optgroup");
        group.label = "Saved procedures";
        for (const value of saved) {
            group.append(option(
                `${SAVED_PREFIX}${value.procedureId}:${value.revision}`,
                `${value.name} · revision ${value.revision}${value.isExecutable ? "" : " · structural"}`));
        }
        select.append(group);
    }

    const presetGroup = document.createElement("optgroup");
    presetGroup.label = "Start from a preset";
    for (const preset of presets) {
        presetGroup.append(option(`${PRESET_PREFIX}${preset.presetKey}`, preset.displayName));
    }
    select.append(presetGroup);
}

export function readProcedureStartChoice(value: string): ProcedureStartChoice {
    if (value.startsWith(SAVED_PREFIX)) {
        const encoded = value.slice(SAVED_PREFIX.length);
        const separator = encoded.lastIndexOf(":");
        if (separator <= 0) throw new Error("The selected saved procedure is invalid.");
        const procedureId = encoded.slice(0, separator);
        const revision = Number(encoded.slice(separator + 1));
        if (!procedureId || !Number.isInteger(revision) || revision <= 0) {
            throw new Error("The selected saved procedure is invalid.");
        }
        return { kind: "saved", procedureId, revision };
    }
    if (value.startsWith(PRESET_PREFIX)) {
        const presetKey = value.slice(PRESET_PREFIX.length);
        if (!presetKey) throw new Error("The selected procedure preset is invalid.");
        return { kind: "preset", presetKey };
    }
    throw new Error("Choose a procedure before starting the expedition.");
}

export function startChoiceSummary(
    choice: ProcedureStartChoice,
    saved: SavedProcedureSummary[],
    presets: ProcedurePreset[]): string {
    if (choice.kind === "saved") {
        const selected = saved.find(value =>
            value.procedureId === choice.procedureId && value.revision === choice.revision);
        return selected
            ? `${selected.name} · saved revision ${selected.revision} · ${selected.moduleCount} ${selected.moduleCount === 1 ? "rule" : "rules"}${selected.originPresetDisplayName ? ` · started from ${selected.originPresetDisplayName}` : " · custom"}`
            : `Saved procedure revision ${choice.revision}`;
    }
    const preset = presets.find(value => value.presetKey === choice.presetKey);
    return preset
        ? `${preset.description} · creates a new saved procedure for this crawl when play begins`
        : "Preset starting point";
}

export function applyWorldProcedureChoice(
    base: Omit<StartExpeditionInput, "procedureKey" | "procedureId" | "procedureRevision">,
    choice: ProcedureStartChoice): StartExpeditionInput {
    if (choice.kind === "preset") {
        return { ...base, procedureKey: choice.presetKey };
    }
    return {
        ...base,
        procedureId: choice.procedureId,
        procedureRevision: choice.revision
    };
}

export function applyStandaloneProcedureChoice(
    base: Omit<StartStandaloneCrawlSessionInput, "procedureKey" | "procedureId" | "procedureRevision">,
    choice: ProcedureStartChoice): StartStandaloneCrawlSessionInput {
    if (choice.kind === "preset") {
        return { ...base, procedureKey: choice.presetKey };
    }
    return {
        ...base,
        procedureId: choice.procedureId,
        procedureRevision: choice.revision
    };
}

function option(value: string, label: string): HTMLOptionElement {
    const result = document.createElement("option");
    result.value = value;
    result.textContent = label;
    return result;
}
