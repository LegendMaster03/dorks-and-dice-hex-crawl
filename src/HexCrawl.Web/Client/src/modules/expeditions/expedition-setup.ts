import type { HexCrawlApi } from "../../api";
import { ProcedureComposerApi } from "../../procedure-composer-api";
import type { SavedProcedureSummary } from "../../procedure-composer-types";
import type { PresentationProfile, ProcedurePreset, WorldCellId } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { campaignProcedureSummary, renderProcedureMechanicList } from "../../campaign-procedure-view";
import {
    applyCellProcedureChoice,
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
    const [world, presets, presentations, savedProcedures] = await Promise.all([
        api.getOverworld(worldId),
        api.getProcedurePresets(),
        api.getPresentationProfiles(),
        composerApi.listProcedures()
    ]);
    const tiling = world.tiling;
    if (!tiling || tiling.topology.motifCells.length === 0)
        throw new Error("The world requires authoritative periodic cell geometry before an expedition can start.");
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
        <div class="hc-inline">
            <label>Starting cell type <select name="motifCellId"></select></label>
            <label>East/west repeat <input name="u" type="number" step="1" value="0" required></label>
            <label>North/south repeat <input name="v" type="number" step="1" value="0" required></label>
        </div>
        <p class="hc-hint">Saved rulesets use the selected revision. A preset saves an editable campaign copy when play begins.</p>
        <button type="submit" class="hc-primary-action">Start expedition</button>`;
    previous.replaceWith(form);

    const procedure = select(form, "procedure");
    const geometricPresets = presets.filter(value =>
        ["alexandrian-advanced", "simple-fixed-distance", "simple-hex-step"].includes(value.presetKey)
        && value.procedure.isExecutable
        && value.procedure.tilingDsSymbol === tiling.topology.quotientDsSymbol);
    populateProcedureStartChoices(procedure, savedProcedures, geometricPresets);
    if (geometricPresets.length > 0)
        procedure.value = `preset:${geometricPresets[0].presetKey}`;
    let createCellRulesetButton: HTMLButtonElement | null = null;
    if (geometricPresets.length === 0) {
        createCellRulesetButton = document.createElement("button");
        createCellRulesetButton.type = "button";
        createCellRulesetButton.textContent = "Create a compatible cell-travel ruleset";
        createCellRulesetButton.className = "hc-secondary-action";
        createCellRulesetButton.title =
            "Creates an editable saved campaign ruleset using existing cell-step movement. Existing procedures are not changed.";
        required<HTMLElement>(form, "[data-procedure-summary]").after(createCellRulesetButton);
    }

    const selectCell = select(form, "motifCellId");
    tiling.topology.motifCells.forEach((cell, index) =>
        selectCell.append(option(cell.id, `Cell shape ${index + 1}`)));
    const selectionHint = document.createElement("p");
    selectionHint.className = "hc-hint";
    selectionHint.textContent =
        "Click a cell on the map to choose the expedition start, or enter its repeating cell position directly.";
    form.querySelector<HTMLElement>(".hc-inline")?.after(selectionHint);
    const acceptMapCell = (event: Event): void => {
        const cell = (event as CustomEvent<WorldCellId>).detail;
        if (!cell || cell.tilingId !== tiling.id) return;
        const selectCell = select(form, "motifCellId");
        if (![...selectCell.options].some(item => item.value === cell.address.motifCellId)) return;
        selectCell.value = cell.address.motifCellId;
        input(form, "u").value = String(cell.address.translation.u);
        input(form, "v").value = String(cell.address.translation.v);
    };
    root.addEventListener("hex-crawl:world-cell-selected", acceptMapCell);
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
        const automation = policy.automationMode === "DmControlled" ? "DM controls every reveal" : policy.markEnteredCellKnown ? "entered cells become known" : "no travel-based reveal";
        required<HTMLElement>(form, "[data-presentation-summary]").textContent =
            `${policy.playerGrid.toLowerCase()} player grid · terrain ${policy.terrainMode.replace(/([A-Z])/g, " $1").trim().toLowerCase()} · ${automation}`;
    };

    procedure.addEventListener("change", () => void renderProcedureSummary());
    presentation.addEventListener("change", () => renderPresentationSummary(selectedPresentation(presentations, presentation.value)));

    if (procedure.value) await renderProcedureSummary();
    renderPresentationSummary(selectedPresentation(presentations, presentation.value));

    const error = root.querySelector<HTMLElement>("[data-error]");
    const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    let readySavedSelection: string | null = null;
    let compatibilityGeneration = 0;
    const compatibilityStatus = document.createElement("p");
    compatibilityStatus.className = "hc-hint";
    compatibilityStatus.setAttribute("role", "status");
    required<HTMLElement>(form, "[data-procedure-summary]").after(compatibilityStatus);
    const canStart = (): boolean => !!procedure.value
        && procedure.value === readySavedSelection;
    const checkCompatibility = async (): Promise<void> => {
        const generation = ++compatibilityGeneration;
        const value = procedure.value;
        readySavedSelection = null;
        submit.disabled = true;
        if (!value) {
            compatibilityStatus.textContent = "Create or select a compatible saved ruleset.";
            return;
        }
        const selected = readProcedureStartChoice(value);
        if (selected.kind === "preset") {
            if (geometricPresets.some(item => item.presetKey === selected.presetKey)) {
                readySavedSelection = value;
                compatibilityStatus.textContent =
                    "This preset creates a native geometric ruleset. Movement uses authoritative world cells.";
            } else {
                compatibilityStatus.textContent =
                    "Choose a compatible saved cell-travel ruleset for this world.";
            }
            submit.disabled = !canStart();
            return;
        }
        compatibilityStatus.textContent = "Checking ruleset compatibility…";
        try {
            const assessment = await api.assessCellProcedure(
                worldId, selected.procedureId, selected.revision);
            if (generation !== compatibilityGeneration || procedure.value !== value) return;
            compatibilityStatus.textContent = assessment.explanation;
            if (assessment.status === "ready") readySavedSelection = value;
        } catch (failure) {
            if (generation !== compatibilityGeneration || procedure.value !== value) return;
            compatibilityStatus.textContent =
                "Unable to verify this saved ruleset. Reload the world and retry.";
            if (error) showUiError(error, failure);
        } finally {
            if (generation === compatibilityGeneration && procedure.value === value)
                submit.disabled = !canStart();
        }
    };
    procedure.addEventListener("change", () => void checkCompatibility());
    await checkCompatibility();
    submit.disabled = !canStart();
    createCellRulesetButton?.addEventListener("click", () => {
        if (!createCellRulesetButton || createCellRulesetButton.disabled) return;
        createCellRulesetButton.disabled = true;
        createCellRulesetButton.textContent = "Creating ruleset…";
        if (error) clearUiError(error);
        void (async () => {
            try {
                const created = await api.createCellTravelProcedure(worldId);
                const fresh = await composerApi.listProcedures();
                savedProcedures.splice(0, savedProcedures.length, ...fresh);
                populateProcedureStartChoices(procedure, savedProcedures, []);
                const value = `saved:${created.procedureId}:${created.revision}`;
                if (![...procedure.options].some(item => item.value === value)) {
                    throw new Error("The ruleset was created, but the saved-ruleset list is stale. Reload the world.");
                }
                procedure.value = value;
                await renderProcedureSummary();
                await checkCompatibility();
            } catch (failure) {
                if (error) showUiError(error, failure);
            } finally {
                if (createCellRulesetButton) {
                    createCellRulesetButton.disabled = false;
                    createCellRulesetButton.textContent = "Create a compatible cell-travel ruleset";
                }
            }
        })();
    });
    form.addEventListener("submit", event => {
        event.preventDefault();
        if (form.dataset.pending === "true") return;
        if (!canStart()) {
            if (error) showUiError(error, new Error(
                "Select a saved ruleset confirmed compatible with the current world."));
            return;
        }
        form.dataset.pending = "true";
        submit.disabled = true;
        submit.textContent = "Starting…";
        if (error) clearUiError(error);
        void (async () => {
            try {
                const choice = readProcedureStartChoice(procedure.value);
                const startCell = {
                    motifCellId: select(form, "motifCellId").value,
                    translation: {
                        u: integer(numberInput(form, "u")),
                        v: integer(numberInput(form, "v"))
                    }
                };
                const expedition = await api.startCellExpedition(worldId, applyCellProcedureChoice({
                    name: input(form, "name").value.trim(),
                    presentationKey: presentation.value,
                    startCell
                }, choice));
                navigate(`/worlds/${worldId}/expeditions/${expedition.id}`);
            } catch (value) {
                if (error) showUiError(error, value);
            } finally {
                form.dataset.pending = "false";
                submit.disabled = !canStart();
                submit.textContent = "Start expedition";
            }
        })();
    });

    return () => {
        compatibilityGeneration++;
        root.removeEventListener("hex-crawl:world-cell-selected", acceptMapCell);
        form.remove();
    };
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
