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
    const runnableSaved = saved.filter(value => value.isExecutable);
    if (runnableSaved.length > 0) {
        const group = document.createElement("optgroup");
        group.label = "Saved procedures";
        for (const value of runnableSaved) {
            group.append(option(
                `${SAVED_PREFIX}${value.procedureId}:${value.revision}`,
                `${value.name} · revision ${value.revision}`));
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
            ? `${selected.name} · saved revision ${selected.revision} · ${selected.moduleCount} rule ${selected.moduleCount === 1 ? "block" : "blocks"}${selected.originPresetDisplayName ? ` · started from ${selected.originPresetDisplayName}` : " · custom"}`
            : `Saved procedure revision ${choice.revision}`;
    }
    const preset = presets.find(value => value.presetKey === choice.presetKey);
    return preset
        ? `${preset.description} · materializes a campaign-owned procedure when the expedition starts`
        : "Preset starting point";
}

export function applyWorldProcedureChoice(
    base: Omit<StartExpeditionInput, "procedureKey">,
    choice: ProcedureStartChoice): StartExpeditionInput {
    if (choice.kind === "preset") {
        return { ...base, procedureKey: choice.presetKey };
    }
    return {
        ...base,
        procedureKey: "",
        procedureId: choice.procedureId,
        procedureRevision: choice.revision
    } as StartExpeditionInput & { procedureId: string; procedureRevision: number };
}

export function applyStandaloneProcedureChoice(
    base: Omit<StartStandaloneCrawlSessionInput, "procedureKey">,
    choice: ProcedureStartChoice): StartStandaloneCrawlSessionInput {
    if (choice.kind === "preset") {
        return { ...base, procedureKey: choice.presetKey };
    }
    return {
        ...base,
        procedureKey: "",
        procedureId: choice.procedureId,
        procedureRevision: choice.revision
    } as StartStandaloneCrawlSessionInput & { procedureId: string; procedureRevision: number };
}

function option(value: string, label: string): HTMLOptionElement {
    const result = document.createElement("option");
    result.value = value;
    result.textContent = label;
    return result;
}
