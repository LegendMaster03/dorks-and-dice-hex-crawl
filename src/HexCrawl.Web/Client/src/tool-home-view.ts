import type { HexCrawlApi } from "./api";
import { createOverworldInput, customUnitFieldsVisible } from "./world-form";
import type { DistanceUnitKind } from "./world-form";
import type { ExpeditionSummary, OverworldSummary, RuntimeProfile } from "./types";
import { clearUiError, showUiError } from "./ui-error";

const NEW_CONTEXT = "__new__";

export async function renderToolHome(
    root: HTMLElement,
    api: HexCrawlApi,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    let disposed = false;
    let startPending = false;
    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div>
                    <h1>Hex Crawl DM tools</h1>
                    <p>Use crawl bookkeeping with the rendered Hex Crawl map, another map, a physical map, or no rendered map.</p>
                </div>
                <nav><button type="button" data-worlds>Overworlds & maps</button></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-columns">
                <section class="hc-panel">
                    <div class="hc-panel-heading">
                        <div><h2>Expeditions</h2><p class="hc-muted">Persistent crawl procedure state is available independently of the map view.</p></div>
                        <span class="hc-muted" data-expedition-count></span>
                    </div>
                    <div class="hc-expedition-list" data-expedition-list></div>
                </section>
                <section class="hc-panel">
                    <h2>Start mapless expedition</h2>
                    <p class="hc-muted">Use an existing world as crawl context, or create a basic grid-only context without entering map authoring.</p>
                    <form class="hc-form" data-start-mapless>
                        <label>Expedition name <input name="name" required value="Expedition" autocomplete="off"></label>
                        <label>Procedure preset <select name="procedure"></select></label>
                        <p class="hc-hint" data-procedure-summary></p>
                        <label>Crawl context <select name="context"></select></label>
                        <div class="hc-form" data-new-context hidden>
                            <label>Context name <input name="contextName" value="Mapless crawl context" autocomplete="off"></label>
                            <label>Hex orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                            <label>Hex center distance <input name="scale" type="number" min="0.001" step="any" value="12"></label>
                            <label>Distance unit <select name="unit"><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                            <div class="hc-form" data-custom-unit hidden>
                                <label>Custom symbol <input name="symbol" value="u" autocomplete="off"></label>
                                <label>Custom meters per unit <input name="meters" type="number" min="0.001" step="any" value="1"></label>
                            </div>
                        </div>
                        <div class="hc-inline">
                            <label>Start q <input name="q" type="number" step="1" value="0"></label>
                            <label>Start r <input name="r" type="number" step="1" value="0"></label>
                        </div>
                        <p class="hc-hint">A basic context persists only the grid geometry needed by the current crawl runtime. It does not create a source map, locations, features, or a rendered map session.</p>
                        <button type="submit" class="hc-primary-action" data-start-button>Start tracker</button>
                    </form>
                </section>
            </div>
            <section class="hc-mode-section" aria-labelledby="hc-mode-title">
                <h2 id="hc-mode-title">Ways to use the crawl engine</h2>
                <div class="hc-mode-grid">
                    <article class="hc-mode-card">
                        <h3>Mapless expedition tracker</h3>
                        <p>Run watches, travel, navigation, encounter cadence, overrides, and history without constructing a rendered map surface.</p>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Full crawl workbench</h3>
                        <p>Compose the same expedition engine with an authored overworld, map rendering, discovery controls, and player-knowledge presentation.</p>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Focused assistants</h3>
                        <p>Open travel/watch, navigation, or encounter-focused views over the same authoritative expedition state.</p>
                    </article>
                </div>
            </section>
        </section>`;

    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-expedition-list]");
    const count = required<HTMLElement>(root, "[data-expedition-count]");
    const form = required<HTMLFormElement>(root, "[data-start-mapless]");
    const context = select(form, "context");
    const procedure = select(form, "procedure");
    const unit = select(form, "unit");
    const newContext = required<HTMLElement>(form, "[data-new-context]");
    const customUnit = required<HTMLElement>(form, "[data-custom-unit]");
    const startButton = required<HTMLButtonElement>(form, "[data-start-button]");
    let profiles: RuntimeProfile[] = [];

    const syncContext = (): void => {
        const creating = context.value === NEW_CONTEXT;
        newContext.hidden = !creating;
        input(form, "contextName").required = creating;
        input(form, "scale").required = creating;
        syncUnit();
    };
    const syncUnit = (): void => {
        const custom = context.value === NEW_CONTEXT && customUnitFieldsVisible(unit.value as DistanceUnitKind);
        customUnit.hidden = !custom;
        input(form, "symbol").required = custom;
        input(form, "meters").required = custom;
    };
    const syncProcedure = (): void => {
        const profile = profiles.find(candidate => candidate.key === procedure.value);
        required<HTMLElement>(form, "[data-procedure-summary]").textContent = profile
            ? `${profile.watchHours}h watches · ${profile.travelResolution === "HexSteps" ? "hex-step travel" : profile.actualDistanceResolution === "Fixed" ? "fixed distance" : "resolved variable distance"} · ${profile.usesNavigationChecks ? "navigation checks" : "no navigation checks"} · encounters ${profile.encounterCadence.toLowerCase()}`
            : "";
    };

    try {
        const [expeditions, worlds, runtimeProfiles] = await Promise.all([
            api.listExpeditions(),
            api.listOverworlds(),
            api.getRuntimeProfiles()
        ]);
        if (disposed) return () => {};

        profiles = runtimeProfiles;
        for (const profile of profiles) procedure.append(option(profile.key, profile.name));
        for (const world of worlds) context.append(option(world.id, world.name));
        context.append(option(NEW_CONTEXT, "New basic context (no rendered map)"));
        if (worlds.length === 0) context.value = NEW_CONTEXT;

        count.textContent = expeditions.length === 1 ? "1 expedition" : `${expeditions.length} expeditions`;
        renderExpeditions(list, expeditions, worlds, navigate);
        syncContext();
        syncProcedure();
    } catch (value) {
        if (!disposed) showUiError(error, value);
    }

    context.addEventListener("change", syncContext);
    unit.addEventListener("change", syncUnit);
    procedure.addEventListener("change", syncProcedure);

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (startPending) return;
        void (async () => {
            clearUiError(error);
            startPending = true;
            startButton.disabled = true;
            startButton.textContent = "Starting…";
            try {
                if (!procedure.value) throw new Error("A procedure preset is required.");
                let worldId = context.value;
                if (worldId === NEW_CONTEXT) {
                    const unitKind = unit.value as DistanceUnitKind;
                    const world = await api.createOverworld(createOverworldInput({
                        name: input(form, "contextName").value,
                        orientation: select(form, "orientation").value === "FlatTop" ? "FlatTop" : "PointyTop",
                        centerDistance: numeric(input(form, "scale")),
                        unitKind,
                        customSymbol: input(form, "symbol").value,
                        customMetersPerUnit: unitKind === "Custom" ? numeric(input(form, "meters")) : null,
                        origin: { x: 0, y: 0 },
                        rotationDegrees: 0,
                        hexRadiusWorldUnits: 1
                    }));
                    worldId = world.id;
                }
                if (!worldId) throw new Error("A crawl context is required.");

                const expedition = await api.startConfiguredExpedition(worldId, {
                    name: input(form, "name").value.trim(),
                    procedureKey: procedure.value,
                    presentationKey: "dm-controlled",
                    startHex: {
                        q: integer(input(form, "q")),
                        r: integer(input(form, "r"))
                    }
                });
                if (!disposed) navigate(`/expeditions/${expedition.id}`);
            } catch (value) {
                if (!disposed) showUiError(error, value);
            } finally {
                startPending = false;
                if (!disposed) {
                    startButton.disabled = false;
                    startButton.textContent = "Start tracker";
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
    navigate: (route: string, replace?: boolean) => void): void {
    host.replaceChildren();
    const worldNames = new Map(worlds.map(world => [world.id, world.name]));
    if (expeditions.length === 0) {
        const empty = document.createElement("div");
        empty.className = "hc-empty-state";
        empty.innerHTML = "<strong>No expeditions yet.</strong><span>Start a mapless tracker beside this list, or create one from an authored overworld.</span>";
        host.append(empty);
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
        meta.textContent = `${expedition.procedureName} · crawl context ${worldNames.get(expedition.overworldId) ?? expedition.overworldId} · updated ${formatTimestamp(expedition.updatedAt)}`;
        copy.append(title, meta);

        const actions = document.createElement("div");
        actions.className = "hc-button-row";
        actions.append(
            action("Open tracker", () => navigate(`/expeditions/${expedition.id}`), true),
            action("Full map", () => navigate(`/worlds/${expedition.overworldId}/expeditions/${expedition.id}`)),
            action("Travel / watch", () => navigate(`/expeditions/${expedition.id}/travel`)),
            action("Navigation", () => navigate(`/expeditions/${expedition.id}/navigation`)),
            action("Encounters", () => navigate(`/expeditions/${expedition.id}/encounters`))
        );

        card.append(copy, actions);
        host.append(card);
    }
}

function action(label: string, onClick: () => void, primary = false): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = label;
    if (primary) button.classList.add("hc-primary-action");
    button.addEventListener("click", onClick);
    return button;
}

function option(value: string, label: string): HTMLOptionElement {
    const result = document.createElement("option");
    result.value = value;
    result.textContent = label;
    return result;
}

function formatTimestamp(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.valueOf())) return value;
    return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(date);
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
