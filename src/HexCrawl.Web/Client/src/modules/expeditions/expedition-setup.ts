import type { HexCrawlApi } from "../../api";
import { ProcedureComposerApi } from "../../procedure-composer-api";
import type { SavedProcedureSummary } from "../../procedure-composer-types";
import type { PresentationProfile, ProcedurePreset } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { campaignProcedureSummary, renderProcedureMechanicList } from "../../campaign-procedure-view";
import {
    applyWorldProcedureChoice,
    populateProcedureStartChoices,
    readProcedureStartChoice,
    startChoiceSummary
} from "./procedure-start-selection";

export async function enhanceExpeditionSetup(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    const previous = root.querySelector<HTMLFormElement>("[data-expedition-form]");
    if (!previous) return () => {};

    const composerApi = await ProcedureComposerApi.create(root);
    const [presets, presentations, savedProcedures] = await Promise.all([
        api.getProcedurePresets(),
        api.getPresentationProfiles(),
        composerApi.listProcedures()
    ]);
    const form = document.createElement("form");
    form.className = "hc-form";
    form.dataset.expeditionForm = "";
    form.innerHTML = `
        <label>Name <input name="name" required value="Expedition"></label>
        <label>Exploration ruleset <select name="procedure"></select></label>
        <p class="hc-hint" data-procedure-summary></p>
        <details class="hc-optional-reference"><summary>Ruleset details</summary><ul data-procedure-mechanics></ul></details>
        <label>Map presentation <select name="presentation"></select></label>
        <p class="hc-hint" data-presentation-summary></p>
        <div class="hc-inline"><label>Start q <input name="q" type="number" step="1" value="0"></label><label>Start r <input name="r" type="number" step="1" value="0"></label></div>
        <p class="hc-hint">Saved rulesets use the selected revision. A preset saves an editable campaign copy when play begins.</p>
        <button type="submit" class="hc-primary-action">Start expedition</button>`;
    previous.replaceWith(form);

    const procedure = select(form, "procedure");
    populateProcedureStartChoices(procedure, savedProcedures, presets);
    const presentation = select(form, "presentation");
    for (const policy of presentations) presentation.append(option(policy.key, policy.name));
    if (presentations.some(policy => policy.key === "exploration-map")) presentation.value = "exploration-map";

    const renderProcedureSummary = async (): Promise<void> => {
        const mechanics = required<HTMLElement>(form, "[data-procedure-mechanics]");
        mechanics.replaceChildren();
        if (!procedure.value) {
            required<HTMLElement>(form, "[data-procedure-summary]").textContent = "No runnable exploration ruleset is available.";
            return;
        }
        const choice = readProcedureStartChoice(procedure.value);
        required<HTMLElement>(form, "[data-procedure-summary]").textContent =
            startChoiceSummary(choice, savedProcedures, presets);
        if (choice.kind === "preset") {
            const preset = selectedPreset(presets, choice.presetKey);
            renderProcedureMechanicList(mechanics, preset.procedure);
            return;
        }

        const saved = await composerApi.getProcedure(choice.procedureId, choice.revision);
        for (const module of saved.modules) {
            const item = document.createElement("li");
            item.textContent = `${module.displayName}: ${module.mechanic.displayName}`;
            mechanics.append(item);
        }
    };
    const renderPresentationSummary = (policy: PresentationProfile): void => {
        const automation = policy.automationMode === "DmControlled" ? "DM controls every reveal" : policy.markEnteredHexKnown ? "explored hexes become known" : "no travel-based reveal";
        required<HTMLElement>(form, "[data-presentation-summary]").textContent =
            `${policy.playerGrid.toLowerCase()} player grid · terrain ${policy.terrainMode.replace(/([A-Z])/g, " $1").trim().toLowerCase()} · ${automation}`;
    };

    procedure.addEventListener("change", () => void renderProcedureSummary());
    presentation.addEventListener("change", () => renderPresentationSummary(selectedPresentation(presentations, presentation.value)));

    if (procedure.value) await renderProcedureSummary();
    renderPresentationSummary(selectedPresentation(presentations, presentation.value));

    const error = root.querySelector<HTMLElement>("[data-error]");
    const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    submit.disabled = !procedure.value;
    form.addEventListener("submit", event => {
        event.preventDefault();
        if (form.dataset.pending === "true") return;
        form.dataset.pending = "true";
        submit.disabled = true;
        submit.textContent = "Starting…";
        if (error) clearUiError(error);
        void (async () => {
            try {
                const choice = readProcedureStartChoice(procedure.value);
                const expedition = await api.startConfiguredExpedition(worldId, applyWorldProcedureChoice({
                    name: input(form, "name").value.trim(),
                    presentationKey: presentation.value,
                    startHex: { q: integer(numberInput(form, "q")), r: integer(numberInput(form, "r")) }
                }, choice));
                navigate(`/worlds/${worldId}/expeditions/${expedition.id}`);
            } catch (value) {
                if (error) showUiError(error, value);
            } finally {
                form.dataset.pending = "false";
                submit.disabled = !procedure.value;
                submit.textContent = "Start expedition";
            }
        })();
    });

    return () => form.remove();
}

function selectedPreset(presets: ProcedurePreset[], key: string): ProcedurePreset {
    const preset = presets.find(candidate => candidate.presetKey === key);
    if (!preset) throw new Error(`Unknown procedure preset ${key}.`);
    return preset;
}

function selectedPresentation(policies: PresentationProfile[], key: string): PresentationProfile {
    const policy = policies.find(candidate => candidate.key === key);
    if (!policy) throw new Error(`Unknown presentation preset ${key}.`);
    return policy;
}

function option(value: string, label: string): HTMLOptionElement {
    const result = document.createElement("option");
    result.value = value;
    result.textContent = label;
    return result;
}

function required<T extends Element>(root: ParentNode, selector: string): T {
    const value = root.querySelector<T>(selector);
    if (!value) throw new Error(`Missing ${selector}`);
    return value;
}

function input(root: ParentNode, name: string): HTMLInputElement {
    const value = root.querySelector<HTMLInputElement>(`input[name="${name}"]`);
    if (!value) throw new Error(`Missing input ${name}`);
    return value;
}

function numberInput(root: ParentNode, name: string): HTMLInputElement {
    return input(root, name);
}

function select(root: ParentNode, name: string): HTMLSelectElement {
    const value = root.querySelector<HTMLSelectElement>(`select[name="${name}"]`);
    if (!value) throw new Error(`Missing select ${name}`);
    return value;
}

function numeric(element: HTMLInputElement): number {
    const value = Number(element.value);
    if (!Number.isFinite(value)) throw new Error(`${element.name} must be a finite number.`);
    return value;
}

function integer(element: HTMLInputElement): number {
    const value = numeric(element);
    if (!Number.isInteger(value)) throw new Error(`${element.name} must be an integer.`);
    return value;
}
