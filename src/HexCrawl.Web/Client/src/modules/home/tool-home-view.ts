import type { HexCrawlApi } from "../../api";
import { ProcedureComposerApi } from "../../procedure-composer-api";
import { customUnitFieldsVisible } from "../worlds/world-form";
import type { DistanceUnitKind } from "../worlds/world-form";
import type { ExpeditionSummary, OverworldSummary, ProcedurePreset, StartStandaloneCrawlSessionInput } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { campaignProcedureSummary, renderProcedureMechanicList } from "../../campaign-procedure-view";
import { input, integer, numeric, option, required, select } from "../../ui/dom";
import { applyGuidedExperience, attachFieldHelp, guidedExperienceEnabled, guidancePreferenceButton } from "../../ui/guidance";
import {
    applyStandaloneProcedureChoice,
    applyWorldProcedureChoice,
    populateProcedureStartChoices,
    readProcedureStartChoice,
    startChoiceSummary
} from "../expeditions/procedure-start-selection";

const ABSTRACT_CONTEXT = "__abstract__";
const NON_SPATIAL_CONTEXT = "__nonspatial__";

export async function renderToolHome(
    root: HTMLElement,
    api: HexCrawlApi,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    let disposed = false;
    let startPending = false;
    applyGuidedExperience(root);
    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div>
                    <h1>Hex Crawl</h1>
                    <p>Run expeditions with campaign-owned exploration rules, maps when needed, and focused GM utilities.</p>
                </div>
                <nav class="hc-button-row" data-guidance-controls></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <section class="hc-guided-callout hc-guided-only" aria-labelledby="hc-guided-start-title">
                <div>
                    <span class="hc-sheet-kicker">Guided setup</span>
                    <h2 id="hc-guided-start-title">Set up the expedition you want to run.</h2>
                    <p>Choose whether you are using a map, choose the exploration rules, then enter only the details that choice needs. Hide beginner help at any time to see the complete setup form.</p>
                </div>
            </section>
            <div class="hc-columns hc-home-three-column-grid hc-home-main-grid">
                <section class="hc-panel">
                    <div class="hc-panel-heading">
                        <div><h2>Expeditions</h2><p class="hc-muted">Open or resume the expedition you are running at the table.</p></div>
                        <span class="hc-muted" data-expedition-count></span>
                    </div>
                    <div class="hc-expedition-list" data-expedition-list></div>
                </section>
                <section class="hc-panel">
                    <h2>Start expedition</h2>
                    <p class="hc-muted">Use guided setup, or hide beginner help to edit all setup fields directly.</p>
                    <form class="hc-form hc-expedition-setup" data-start-mapless>
                        <p class="hc-guided-setup-status hc-guided-only" data-setup-status role="status"></p>

                        <section class="hc-setup-step" data-setup-step="0">
                            <h3>1. What do you want to run?</h3>
                            <label>Map or travel setup <select name="context" required></select></label>
                            <p class="hc-hint" data-context-summary></p>
                            <div class="hc-button-row hc-guided-only">
                                <button type="button" class="hc-primary-action" data-setup-next="1" disabled>Next: choose rules</button>
                            </div>
                        </section>

                        <section class="hc-setup-step" data-setup-step="1">
                            <h3>2. Choose exploration rules</h3>
                            <label>Exploration ruleset <select name="procedure"></select></label>
                            <p class="hc-hint" data-procedure-summary></p>
                            <details class="hc-optional-reference"><summary>View ruleset details</summary><ul data-procedure-mechanics></ul></details>
                            <div class="hc-button-row hc-guided-only">
                                <button type="button" data-setup-back="0">Back</button>
                                <button type="button" class="hc-primary-action" data-setup-next="2" disabled>Next: expedition details</button>
                            </div>
                        </section>

                        <section class="hc-setup-step" data-setup-step="2">
                            <h3>3. Expedition details</h3>
                            <label>Expedition name <input name="name" required value="Expedition" autocomplete="off"></label>
                            <div class="hc-form" data-abstract-context hidden>
                                <label>Setup name <input name="contextName" value="Mapless exploration" autocomplete="off"></label>
                                <details class="hc-optional-reference"><summary>Optional grid orientation</summary><label>Grid orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label><p class="hc-hint">Keep pointy top unless your grid uses flat-top cells.</p></details>
                                <fieldset class="hc-field-group hc-map-scale-field">
                                    <legend>Map scale</legend>
                                    <div class="hc-inline">
                                        <label>Distance <input name="scale" type="number" min="0.001" step="any" placeholder="required"></label>
                                        <label>Unit <select name="unit"><option value="">Select unit</option><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                                    </div>
                                    <p class="hc-hint">The game-world distance from the center of one cell to the center of an adjacent cell.</p>
                                </fieldset>
                                <div class="hc-form" data-custom-unit hidden>
                                    <label>Custom symbol <input name="symbol" autocomplete="off"></label>
                                    <label>Custom meters per unit <input name="meters" type="number" min="0.001" step="any"></label>
                                </div>
                                <p class="hc-hint">There is no authored map, but spatial positions and adjacent-cell travel are still tracked.</p>
                            </div>
                            <div data-spatial-start hidden>
                                <details class="hc-optional-reference">
                                    <summary>Optional starting position (defaults to cell 0, 0)</summary>
                                    <p class="hc-hint">Leave these coordinates unchanged to begin at the default cell. For an existing grid, enter the exact cell coordinates if you need another starting point.</p>
                                    <div class="hc-inline">
                                        <label>Cell coordinate q <input name="q" type="number" step="1" value="0"></label>
                                        <label>Cell coordinate r <input name="r" type="number" step="1" value="0"></label>
                                    </div>
                                </details>
                            </div>
                            <div data-nonspatial-context hidden>
                                <label>Setup name <input name="nonSpatialName" value="Journey" autocomplete="off"></label>
                                <p class="hc-hint">No grid, coordinates, map scale, or travel direction are created. The selected ruleset and journey state remain available.</p>
                            </div>
                            <p class="hc-hint">Saved rulesets use the selected revision. A preset saves an editable campaign copy when play begins.</p>
                            <div class="hc-button-row">
                                <button type="button" class="hc-guided-only" data-setup-back="1">Back</button>
                                <button type="submit" class="hc-primary-action" data-start-button>Start expedition</button>
                            </div>
                        </section>
                    </form>
                </section>
            </div>
            <section class="hc-mode-section" aria-label="Hex Crawl management">
                <div class="hc-mode-grid hc-home-three-column-grid">
                    <article class="hc-mode-card">
                        <h2>Exploration rulesets</h2>
                        <p>Create, inspect, and manage the exploration rules used by expeditions.</p>
                        <button type="button" class="hc-primary-action" data-procedures>Manage rulesets</button>
                    </article>
                    <article class="hc-mode-card">
                        <h2>Worlds / maps</h2>
                        <p>Create and manage authored worlds and their spatial map data.</p>
                        <button type="button" class="hc-primary-action" data-worlds>Manage worlds</button>
                    </article>
                </div>
            </section>
            <details class="hc-panel hc-home-optional-tools">
                <summary id="hc-assistants-title">GM utilities</summary>
                <p class="hc-muted">Focused utilities are secondary tools for a specific table task. Normal expedition play starts by opening the expedition.</p>
                <div class="hc-mode-grid hc-home-three-column-grid">
                    <article class="hc-mode-card">
                        <h3>Travel / time</h3>
                        <p>Use focused travel or interval bookkeeping independently of the full expedition workspace.</p>
                        <button type="button" data-assistant-travel>Open travel / time utility</button>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Navigation</h3>
                        <p>Resolve a focused spatial navigation task without treating navigation as a separate product mode.</p>
                        <button type="button" data-assistant-navigation>Open navigation utility</button>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Encounter cadence</h3>
                        <p>Resolve focused encounter cadence when that is the only table task you need.</p>
                        <button type="button" data-assistant-encounters>Open encounter utility</button>
                    </article>
                </div>
            </details>
        </section>`;

    required<HTMLButtonElement>(root, "[data-procedures]").addEventListener("click", () => navigate("/procedures"));
    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    required<HTMLButtonElement>(root, "[data-assistant-travel]").addEventListener("click", () => navigate("/assistants/travel"));
    required<HTMLButtonElement>(root, "[data-assistant-navigation]").addEventListener("click", () => navigate("/assistants/navigation"));
    required<HTMLButtonElement>(root, "[data-assistant-encounters]").addEventListener("click", () => navigate("/assistants/encounters"));
    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-expedition-list]");
    const count = required<HTMLElement>(root, "[data-expedition-count]");
    const form = required<HTMLFormElement>(root, "[data-start-mapless]");
    const context = select(form, "context");
    const procedure = select(form, "procedure");
    const unit = select(form, "unit");
    const abstractContext = required<HTMLElement>(form, "[data-abstract-context]");
    const spatialStart = required<HTMLElement>(form, "[data-spatial-start]");
    const nonSpatialContext = required<HTMLElement>(form, "[data-nonspatial-context]");
    const customUnit = required<HTMLElement>(form, "[data-custom-unit]");
    const startButton = required<HTMLButtonElement>(form, "[data-start-button]");
    const setupStatus = required<HTMLElement>(form, "[data-setup-status]");
    const setupSteps = Array.from(form.querySelectorAll<HTMLElement>("[data-setup-step]"));
    const contextNext = required<HTMLButtonElement>(form, '[data-setup-next="1"]');
    const rulesNext = required<HTMLButtonElement>(form, '[data-setup-next="2"]');
    let guidedStep = 0;

    const syncGuidedSetup = (): void => {
        const guided = guidedExperienceEnabled();
        setupStatus.hidden = !guided;
        setupStatus.textContent = guided ? `Step ${guidedStep + 1} of 3` : "";
        for (const step of setupSteps) {
            step.hidden = guided && Number(step.dataset.setupStep) !== guidedStep;
        }
    };
    const goToGuidedStep = (step: number): void => {
        guidedStep = Math.max(0, Math.min(2, step));
        syncGuidedSetup();
        const target = setupSteps.find(value => Number(value.dataset.setupStep) === guidedStep);
        target?.querySelector<HTMLElement>("select, input, button:not([disabled])")?.focus();
    };
    for (const control of form.querySelectorAll<HTMLButtonElement>("[data-setup-next]")) {
        control.addEventListener("click", () => goToGuidedStep(Number(control.dataset.setupNext)));
    }
    for (const control of form.querySelectorAll<HTMLButtonElement>("[data-setup-back]")) {
        control.addEventListener("click", () => goToGuidedStep(Number(control.dataset.setupBack)));
    }
    required<HTMLElement>(root, "[data-guidance-controls]").append(
        guidancePreferenceButton(root, () => syncGuidedSetup()));
    syncGuidedSetup();

    attachFieldHelp(
        procedure,
        "Exploration ruleset",
        "The exploration ruleset decides which travel, navigation, encounter, survival, and journey steps exist.",
        "If you are new to hex crawling, start from a preset; you can edit the saved campaign copy later.");
    attachFieldHelp(
        context,
        "Map or travel setup",
        "Choose an authored map, mapless spatial exploration, or a journey that does not use a grid.");
    attachFieldHelp(
        select(form, "orientation"),
        "Hex orientation",
        "Pointy-top and flat-top describe how the hexes are drawn. Orientation alone does not define north or change the distance represented by a hex.");
    attachFieldHelp(
        input(form, "scale"),
        "Map scale",
        "This is the game-world distance from the center of one cell to the center of an adjacent cell.",
        "If adjacent hexes on your map represent 6 miles of travel, enter 6 and choose Miles.");
    attachFieldHelp(
        unit,
        "Map scale unit",
        "This is the unit used by the map scale and spatial travel calculations.");
    attachFieldHelp(
        input(form, "meters"),
        "Custom meters per unit",
        "For a custom distance unit, this conversion tells Hex Crawl how large one custom unit is in meters. Leave it alone unless you selected Custom.");
    let presets: ProcedurePreset[] = [];
    let savedProcedures = await Promise.resolve([] as Awaited<ReturnType<ProcedureComposerApi["listProcedures"]>>);
    const composerApi = await ProcedureComposerApi.create(root);

    const syncContext = (): void => {
        const abstract = context.value === ABSTRACT_CONTEXT;
        const nonSpatial = context.value === NON_SPATIAL_CONTEXT;
        abstractContext.hidden = !abstract;
        nonSpatialContext.hidden = !nonSpatial;
        const spatial = Boolean(context.value) && !nonSpatial;
        spatialStart.hidden = !spatial;
        input(form, "contextName").required = abstract;
        input(form, "scale").required = abstract;
        unit.required = abstract;
        input(form, "q").required = spatial;
        input(form, "r").required = spatial;
        input(form, "nonSpatialName").required = nonSpatial;
        contextNext.disabled = !context.value;
        required<HTMLElement>(form, "[data-context-summary]").textContent = nonSpatial
            ? "Journey without a grid creates no coordinates, map scale, or travel direction. Choose a ruleset that can run without spatial travel."
            : abstract
                ? "Explore without a map. Spatial positions and adjacent destinations are still tracked, and the selected ruleset decides which travel mechanics apply."
                : context.value
                    ? "Use an authored map. The selected ruleset still decides which travel, navigation, and journey mechanics apply."
                    : "Choose how this expedition relates to a map.";
        syncUnit();
    };
    const syncUnit = (): void => {
        const custom = context.value === ABSTRACT_CONTEXT && customUnitFieldsVisible(unit.value as DistanceUnitKind);
        customUnit.hidden = !custom;
        input(form, "symbol").required = custom;
        input(form, "meters").required = custom;
    };
    const syncProcedure = async (): Promise<void> => {
        const mechanics = required<HTMLElement>(form, "[data-procedure-mechanics]");
        mechanics.replaceChildren();
        rulesNext.disabled = !procedure.value;
        if (!procedure.value) {
            required<HTMLElement>(form, "[data-procedure-summary]").textContent = "No runnable exploration ruleset is available.";
            return;
        }
        const choice = readProcedureStartChoice(procedure.value);
        required<HTMLElement>(form, "[data-procedure-summary]").textContent =
            startChoiceSummary(choice, savedProcedures, presets);
        if (choice.kind === "preset") {
            const preset = presets.find(candidate => candidate.presetKey === choice.presetKey);
            if (preset) {
                required<HTMLElement>(form, "[data-procedure-summary]").textContent =
                    `${preset.description} · ${campaignProcedureSummary(preset.procedure)} · saves an editable campaign copy when play begins`;
                renderProcedureMechanicList(mechanics, preset.procedure);
            }
            return;
        }
        const saved = await composerApi.getProcedure(choice.procedureId, choice.revision);
        for (const module of saved.modules) {
            const item = document.createElement("li");
            item.textContent = `${module.displayName}: ${module.mechanic.displayName}`;
            mechanics.append(item);
        }
    };

    try {
        const [expeditions, worlds, procedurePresets, procedures] = await Promise.all([
            api.listExpeditions(),
            api.listOverworlds(),
            api.getProcedurePresets(),
            composerApi.listProcedures()
        ]);
        if (disposed) return () => {};

        presets = procedurePresets;
        savedProcedures = procedures;
        populateProcedureStartChoices(procedure, savedProcedures, presets);
        context.append(option("", "Choose map or travel setup"));
        for (const world of worlds) context.append(option(world.id, `Use map: ${world.name}`));
        context.append(
            option(ABSTRACT_CONTEXT, "Explore without a map"),
            option(NON_SPATIAL_CONTEXT, "Journey without a grid")
        );
        context.value = "";

        syncExpeditionCount(count, expeditions.length);
        renderExpeditions(list, expeditions, worlds, api, error, count, navigate);
        syncContext();
        await syncProcedure();
        syncGuidedSetup();
        startButton.disabled = !procedure.value;
    } catch (value) {
        if (!disposed) showUiError(error, value);
    }

    context.addEventListener("change", syncContext);
    unit.addEventListener("change", syncUnit);
    procedure.addEventListener("change", () => void syncProcedure());

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (startPending) return;
        void (async () => {
            clearUiError(error);
            startPending = true;
            startButton.disabled = true;
            startButton.textContent = "Starting…";
            try {
                const choice = readProcedureStartChoice(procedure.value);
                const name = input(form, "name").value.trim();
                let expedition;
                if (context.value === ABSTRACT_CONTEXT) {
                    const unitKind = unit.value as DistanceUnitKind;
                    const base: Omit<StartStandaloneCrawlSessionInput, "procedureKey"> = {
                        name,
                        context: {
                            kind: "AbstractHex",
                            name: input(form, "contextName").value.trim(),
                            orientation: select(form, "orientation").value === "FlatTop" ? "FlatTop" : "PointyTop",
                            hexCenterDistance: numeric(input(form, "scale")),
                            distanceUnit: distanceUnit(unitKind, form)
                        },
                        startHex: {
                            q: integer(input(form, "q")),
                            r: integer(input(form, "r"))
                        }
                    };
                    expedition = await api.startStandaloneSession(applyStandaloneProcedureChoice(base, choice));
                } else if (context.value === NON_SPATIAL_CONTEXT) {
                    expedition = await api.startStandaloneSession(applyStandaloneProcedureChoice({
                        name,
                        context: {
                            kind: "NonSpatial",
                            name: input(form, "nonSpatialName").value.trim()
                        }
                    }, choice));
                } else {
                    if (!context.value) throw new Error("A map or travel setup is required.");
                    expedition = await api.startConfiguredExpedition(context.value, applyWorldProcedureChoice({
                        name,
                        presentationKey: "dm-controlled",
                        startHex: {
                            q: integer(input(form, "q")),
                            r: integer(input(form, "r"))
                        }
                    }, choice));
                }
                if (!disposed) navigate(`/expeditions/${expedition.id}`);
            } catch (value) {
                if (!disposed) showUiError(error, value);
            } finally {
                startPending = false;
                if (!disposed) {
                    startButton.disabled = !procedure.value;
                    startButton.textContent = "Start expedition";
                }
            }
        })();
    });

    return () => { disposed = true; };
}

function renderExpeditions(
    host: HTMLElement,
    expeditions: ExpeditionSummary[],
    worlds: OverworldSummary[],
    api: HexCrawlApi,
    error: HTMLElement,
    count: HTMLElement,
    navigate: (route: string, replace?: boolean) => void): void {
    host.replaceChildren();
    const worldNames = new Map(worlds.map(world => [world.id, world.name]));
    if (expeditions.length === 0) {
        renderEmptyExpeditions(host);
        return;
    }

    for (const expedition of expeditions) {
        const card = document.createElement("article");
        card.className = "hc-expedition-card";

        const copy = document.createElement("div");
        const title = document.createElement("h3");
        title.textContent = expedition.name;
        const meta = document.createElement("p");
        meta.className = "hc-muted";
        const contextName = expedition.context.kind === "WorldBound" && expedition.context.overworldId
            ? worldNames.get(expedition.context.overworldId) ?? expedition.context.name
            : expedition.context.name;
        meta.textContent = `${expedition.procedureName} · ${contextLabel(expedition.context.kind)}: ${contextName} · updated ${formatTimestamp(expedition.updatedAt)}`;
        copy.append(title, meta);

        const actions = document.createElement("div");
        actions.className = "hc-button-row";
        actions.append(action("Open expedition", () => navigate(`/expeditions/${expedition.id}`), true));

        const deleteButton = document.createElement("button");
        deleteButton.type = "button";
        deleteButton.className = "hc-danger-action";
        deleteButton.textContent = "Delete expedition";
        deleteButton.setAttribute("aria-label", `Delete expedition ${expedition.name}`);
        deleteButton.addEventListener("click", () => {
            const confirmed = window.confirm(
                `Delete expedition “${expedition.name}”?\n\nThis permanently deletes the saved expedition, including its history and expedition state. The overworld itself will not be deleted.`);
            if (!confirmed) return;
            void (async () => {
                clearUiError(error);
                deleteButton.disabled = true;
                deleteButton.textContent = "Deleting…";
                try {
                    await api.deleteExpedition(expedition.id, expedition.version);
                    // Never treat removal of a local card as proof that the
                    // expedition was deleted. Reconcile with an authoritative
                    // server list after the DELETE transaction has committed.
                    const remaining = await api.listExpeditions();
                    if (remaining.some(item => item.id === expedition.id)) {
                        throw new Error("The server still lists this expedition after deletion. Reload and retry; the expedition has not been confirmed deleted.");
                    }
                    syncExpeditionCount(count, remaining.length);
                    renderExpeditions(host, remaining, worlds, api, error, count, navigate);
                } catch (value) {
                    showUiError(error, value);
                    deleteButton.disabled = false;
                    deleteButton.textContent = "Delete expedition";
                }
            })();
        });
        actions.append(deleteButton);

        card.append(copy, actions);
        host.append(card);
    }
}

function renderEmptyExpeditions(host: HTMLElement): void {
    const empty = document.createElement("div");
    empty.className = "hc-empty-state";
    empty.innerHTML = "<strong>No expeditions yet.</strong><span>Start an expedition here, with or without an authored world.</span>";
    host.append(empty);
}

function syncExpeditionCount(count: HTMLElement, value: number): void {
    count.textContent = value === 1 ? "1 expedition" : `${value} expeditions`;
}

function distanceUnit(kind: DistanceUnitKind, form: HTMLFormElement) {
    if (kind === "Mile") return { kind, symbol: "mi", metersPerUnit: 1609.344 };
    if (kind === "Kilometer") return { kind, symbol: "km", metersPerUnit: 1000 };
    return {
        kind,
        symbol: input(form, "symbol").value.trim(),
        metersPerUnit: numeric(input(form, "meters"))
    };
}

function contextLabel(kind: ExpeditionSummary["context"]["kind"]): string {
    if (kind === "WorldBound") return "Map";
    if (kind === "AbstractHex") return "Mapless spatial";
    return "Journey / no grid";
}

function action(label: string, onClick: () => void, primary = false): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    if (primary) button.classList.add("hc-primary-action");
    button.addEventListener("click", onClick);
    return button;
}

function formatTimestamp(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.valueOf())) return value;
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
}
