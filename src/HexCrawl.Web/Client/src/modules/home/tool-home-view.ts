import type { HexCrawlApi } from "../../api";
import { ProcedureComposerApi } from "../../procedure-composer-api";
import { customUnitFieldsVisible } from "../worlds/world-form";
import type { DistanceUnitKind } from "../worlds/world-form";
import type { ExpeditionSummary, OverworldSummary, ProcedurePreset, StartStandaloneCrawlSessionInput } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { campaignProcedureSummary, renderProcedureMechanicList } from "../../campaign-procedure-view";
import { input, integer, numeric, option, required, select } from "../../ui/dom";
import { applyGuidedExperience, attachFieldHelp, guidancePreferenceButton } from "../../ui/guidance";
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
                    <p>Run expeditions with campaign-owned exploration procedures, spatial maps when needed, and focused GM utilities.</p>
                </div>
                <nav class="hc-button-row" data-guidance-controls></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <section class="hc-guided-callout hc-guided-only" aria-labelledby="hc-guided-start-title">
                <div>
                    <span class="hc-sheet-kicker">Guided</span>
                    <h2 id="hc-guided-start-title">New to hex crawls? Start here.</h2>
                    <p>A hex crawl turns exploration into a repeatable table procedure. Hex Crawl keeps the current state and next action visible so you do not have to memorize the procedure first.</p>
                </div>
                <ol class="hc-guided-steps">
                    <li><strong>Pick a procedure.</strong> If you are unsure, use a familiar preset instead of building one from scratch.</li>
                    <li><strong>Pick the expedition context.</strong> Use an authored world/map when you have one, an abstract hex grid for mapless spatial travel, or a non-spatial session for journey procedures that do not use hexes.</li>
                    <li><strong>Start the expedition.</strong> During play, follow the Next action card; it tells you what the current procedure needs before play can continue.</li>
                    <li><strong>Open details only when needed.</strong> Party, movement, navigation, encounters, resources, and journey state remain available without becoming separate workflows.</li>
                </ol>
                <details class="hc-guided-lesson">
                    <summary>Travel basics and terms</summary>
                    <dl class="hc-guided-terms">
                        <dt>Procedure</dt><dd>The rules that define how this expedition handles time, movement, navigation, encounters, survival, and journeys.</dd>
                        <dt>Hex center distance</dt><dd>The game-world distance from the center of one hex to the center of an adjacent hex. A 6-mile value means one adjacent hex represents 6 miles center-to-center.</dd>
                        <dt>Travel period / watch</dt><dd>A repeating chunk of travel time defined by the selected procedure. Not every procedure uses watches.</dd>
                        <dt>Course</dt><dd>The direction the party intends to travel. Selecting a course does not move the party; travel resolves only when you take the travel action.</dd>
                        <dt>Navigation</dt><dd>The procedure that determines whether the party follows its intended course, becomes lost, recognizes that problem, or reorients.</dd>
                        <dt>Pace / travel mode</dt><dd>A reusable travel choice such as normal, slow, or fast when the selected procedure defines those choices.</dd>
                        <dt>q / r</dt><dd>Axial coordinates used to identify hexes. For a new abstract grid, 0 / 0 is a normal starting point.</dd>
                    </dl>
                </details>
                <details class="hc-guided-lesson">
                    <summary>When do I need advanced setup?</summary>
                    <p>Usually you do not. Start with a preset and ordinary map/context values. Advanced grid alignment, custom units, Advanced procedure editing, and JSON exist for unusual maps, house rules, imports, or exact technical control.</p>
                </details>
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
                    <p class="hc-muted">Choose the procedure and only the spatial context that procedure needs.</p>
                    <form class="hc-form" data-start-mapless>
                        <label>Expedition name <input name="name" required value="Expedition" autocomplete="off"></label>
                        <label>Procedure <select name="procedure"></select></label>
                        <p class="hc-hint" data-procedure-summary></p>
                        <details class="hc-optional-reference"><summary>Procedure details</summary><ul data-procedure-mechanics></ul></details>
                        <label>Expedition context <select name="context"></select></label>
                        <div class="hc-form" data-abstract-context hidden>
                            <label>Context name <input name="contextName" value="Mapless hex crawl" autocomplete="off"></label>
                            <label>Hex orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                            <label>Hex center distance <input name="scale" type="number" min="0.001" step="any" placeholder="required"></label>
                            <label>Distance unit <select name="unit"><option value="">Select distance unit</option><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                            <div class="hc-form" data-custom-unit hidden>
                                <label>Custom symbol <input name="symbol" autocomplete="off"></label>
                                <label>Custom meters per unit <input name="meters" type="number" min="0.001" step="any"></label>
                            </div>
                            <div class="hc-inline">
                                <label>Start q <input name="q" type="number" step="1" value="0"></label>
                                <label>Start r <input name="r" type="number" step="1" value="0"></label>
                            </div>
                            <p class="hc-hint">Abstract hex stores crawl-scale context without creating a world or source map.</p>
                        </div>
                        <div data-nonspatial-context hidden>
                            <label>Context name <input name="nonSpatialName" value="Procedure session" autocomplete="off"></label>
                            <p class="hc-hint">Non-spatial expeditions persist procedure and history state without fabricating coordinates, distance, course, pace, or map state.</p>
                        </div>
                        <p class="hc-hint">Saved procedures use the selected revision. A preset creates a new saved procedure when play begins.</p>
                        <button type="submit" class="hc-primary-action" data-start-button>Start expedition</button>
                    </form>
                </section>
            </div>
            <section class="hc-mode-section" aria-label="Hex Crawl management">
                <div class="hc-mode-grid hc-home-three-column-grid">
                    <article class="hc-mode-card">
                        <h2>Procedures</h2>
                        <p>Create, inspect, and manage the exploration procedures used by expeditions.</p>
                        <button type="button" class="hc-primary-action" data-procedures>Manage procedures</button>
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
    required<HTMLElement>(root, "[data-guidance-controls]").append(guidancePreferenceButton(root));
    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-expedition-list]");
    const count = required<HTMLElement>(root, "[data-expedition-count]");
    const form = required<HTMLFormElement>(root, "[data-start-mapless]");
    const context = select(form, "context");
    const procedure = select(form, "procedure");
    const unit = select(form, "unit");
    const abstractContext = required<HTMLElement>(form, "[data-abstract-context]");
    const nonSpatialContext = required<HTMLElement>(form, "[data-nonspatial-context]");
    const customUnit = required<HTMLElement>(form, "[data-custom-unit]");
    const startButton = required<HTMLButtonElement>(form, "[data-start-button]");
    attachFieldHelp(
        procedure,
        "Procedure",
        "The procedure is the expedition ruleset. It decides which travel, navigation, encounter, survival, and journey steps exist.",
        "If you are new to hex crawling, choose a preset first; you can edit the saved campaign copy later.");
    attachFieldHelp(
        context,
        "Expedition context",
        "The context tells Hex Crawl whether this expedition uses an authored world map, an abstract mathematical hex grid, or no spatial grid at all.");
    attachFieldHelp(
        select(form, "orientation"),
        "Hex orientation",
        "Pointy-top and flat-top describe how the hexes are drawn. Orientation alone does not define north or change the distance represented by a hex.");
    attachFieldHelp(
        input(form, "scale"),
        "Hex center distance",
        "This is the game-world distance from the center of one hex to the center of an adjacent hex.",
        "If adjacent hexes on your map represent 6 miles of travel, enter 6 and choose Miles.");
    attachFieldHelp(
        unit,
        "Distance unit",
        "This is the unit used by the hex center distance and spatial travel calculations.");
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
        input(form, "contextName").required = abstract;
        input(form, "scale").required = abstract;
        unit.required = abstract;
        input(form, "q").required = abstract;
        input(form, "r").required = abstract;
        input(form, "nonSpatialName").required = nonSpatial;
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
        if (!procedure.value) {
            required<HTMLElement>(form, "[data-procedure-summary]").textContent = "No runnable procedure is available.";
            return;
        }
        const choice = readProcedureStartChoice(procedure.value);
        required<HTMLElement>(form, "[data-procedure-summary]").textContent =
            startChoiceSummary(choice, savedProcedures, presets);
        if (choice.kind === "preset") {
            const preset = presets.find(candidate => candidate.presetKey === choice.presetKey);
            if (preset) {
                required<HTMLElement>(form, "[data-procedure-summary]").textContent =
                    `${preset.description} · ${campaignProcedureSummary(preset.procedure)} · creates a new saved procedure when play begins`;
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
        for (const world of worlds) context.append(option(world.id, `World: ${world.name}`));
        context.append(
            option(ABSTRACT_CONTEXT, "Abstract hex (no Overworld)"),
            option(NON_SPATIAL_CONTEXT, "Non-spatial procedure session")
        );
        if (worlds.length === 0) context.value = ABSTRACT_CONTEXT;

        syncExpeditionCount(count, expeditions.length);
        renderExpeditions(list, expeditions, worlds, api, error, count, navigate);
        syncContext();
        await syncProcedure();
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
                    if (!context.value) throw new Error("A crawl context is required.");
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
                    card.remove();
                    const remaining = host.querySelectorAll(".hc-expedition-card").length;
                    syncExpeditionCount(count, remaining);
                    if (remaining === 0) renderEmptyExpeditions(host);
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
    if (kind === "WorldBound") return "World";
    if (kind === "AbstractHex") return "Abstract hex";
    return "Non-spatial";
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
