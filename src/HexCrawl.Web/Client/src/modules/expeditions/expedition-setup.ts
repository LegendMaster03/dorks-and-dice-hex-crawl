import type { HexCrawlApi } from "../../api";
import type { PresentationProfile, ProcedurePreset } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { campaignProcedureSummary, renderProcedureMechanicList } from "../../campaign-procedure-view";

export async function enhanceExpeditionSetup(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    const previous = root.querySelector<HTMLFormElement>("[data-expedition-form]");
    if (!previous) return () => {};

    const [presets, presentations] = await Promise.all([
        api.getProcedurePresets(),
        api.getPresentationProfiles()
    ]);
    const form = document.createElement("form");
    form.className = "hc-form";
    form.dataset.expeditionForm = "";
    form.innerHTML = `
        <label>Name <input name="name" required value="Expedition"></label>
        <label>Procedure preset <select name="procedure"></select></label>
        <p class="hc-hint" data-procedure-summary></p>
        <details class="hc-optional-reference"><summary>Materialized procedure</summary><ul data-procedure-mechanics></ul></details>
        <label>Map presentation <select name="presentation"></select></label>
        <p class="hc-hint" data-presentation-summary></p>
        <div class="hc-inline"><label>Start q <input name="q" type="number" step="1" value="0"></label><label>Start r <input name="r" type="number" step="1" value="0"></label></div>
        <p class="hc-hint">Procedure customization moves to the Procedure Composer. This setup materializes the selected preset exactly and stores that campaign-owned snapshot.</p>
        <button type="submit" class="hc-primary-action">Start expedition</button>`;
    previous.replaceWith(form);

    const procedure = select(form, "procedure");
    for (const preset of presets) procedure.append(option(preset.presetKey, preset.displayName));
    const presentation = select(form, "presentation");
    for (const policy of presentations) presentation.append(option(policy.key, policy.name));
    if (presentations.some(policy => policy.key === "exploration-map")) presentation.value = "exploration-map";

    const renderProcedureSummary = (preset: ProcedurePreset): void => {
        required<HTMLElement>(form, "[data-procedure-summary]").textContent =
            `${preset.description} · ${campaignProcedureSummary(preset.procedure)}`;
        renderProcedureMechanicList(
            required<HTMLElement>(form, "[data-procedure-mechanics]"),
            preset.procedure);
    };
    const renderPresentationSummary = (policy: PresentationProfile): void => {
        const automation = policy.automationMode === "DmControlled" ? "DM controls every reveal" : policy.markEnteredHexKnown ? "explored hexes become known" : "no travel-based reveal";
        required<HTMLElement>(form, "[data-presentation-summary]").textContent =
            `${policy.playerGrid.toLowerCase()} player grid · terrain ${policy.terrainMode.replace(/([A-Z])/g, " $1").trim().toLowerCase()} · ${automation}`;
    };

    procedure.addEventListener("change", () => renderProcedureSummary(selectedPreset(presets, procedure.value)));
    presentation.addEventListener("change", () => renderPresentationSummary(selectedPresentation(presentations, presentation.value)));

    if (presets.length > 0) renderProcedureSummary(selectedPreset(presets, procedure.value));
    renderPresentationSummary(selectedPresentation(presentations, presentation.value));

    const error = root.querySelector<HTMLElement>("[data-error]");
    const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    form.addEventListener("submit", event => {
        event.preventDefault();
        if (form.dataset.pending === "true") return;
        form.dataset.pending = "true";
        submit.disabled = true;
        submit.textContent = "Starting…";
        if (error) clearUiError(error);
        void (async () => {
            try {
                const preset = selectedPreset(presets, procedure.value);
                const expedition = await api.startConfiguredExpedition(worldId, {
                    name: input(form, "name").value.trim(),
                    procedureKey: preset.presetKey,
                    presentationKey: presentation.value,
                    startHex: { q: integer(numberInput(form, "q")), r: integer(numberInput(form, "r")) }
                });
                navigate(`/worlds/${worldId}/expeditions/${expedition.id}`);
            } catch (value) {
                if (error) showUiError(error, value);
            } finally {
                form.dataset.pending = "false";
                submit.disabled = false;
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
