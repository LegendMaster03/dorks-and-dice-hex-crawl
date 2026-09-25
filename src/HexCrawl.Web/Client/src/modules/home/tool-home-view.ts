import type { HexCrawlApi } from "../../api";
import { customUnitFieldsVisible } from "../worlds/world-form";
import type { DistanceUnitKind } from "../worlds/world-form";
import type { ExpeditionSummary, OverworldSummary, RuntimeProfile, StartStandaloneCrawlSessionInput } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { input, integer, numeric, option, required, select } from "../../ui/dom";

const ABSTRACT_CONTEXT = "__abstract__";
const NON_SPATIAL_CONTEXT = "__nonspatial__";

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
                    <h1>Hex Crawl</h1>
                    <p>Running sheets for traditional hex-crawl procedure, with optional automation and map tools when you want them.</p>
                </div>
                <nav><button type="button" data-worlds>Overworlds & maps</button></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <details class="hc-panel hc-home-optional-tools">
                <summary id="hc-assistants-title">Optional focused tools</summary>
                <p class="hc-muted">Use these independently when you want automation for one part of the crawl. They are not required to use a running sheet.</p>
                <div class="hc-mode-grid hc-home-three-column-grid">
                    <article class="hc-mode-card">
                        <h3>Travel / Watch Assistant</h3>
                        <p>Generic watch/time bookkeeping needs no grid. Spatial travel can opt into an abstract hex context.</p>
                        <button type="button" class="hc-primary-action" data-assistant-travel>Open Travel / Watch</button>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Navigation Assistant</h3>
                        <p>Use an existing spatial session or create the minimum abstract hex context required for direction.</p>
                        <button type="button" class="hc-primary-action" data-assistant-navigation>Open Navigation</button>
                    </article>
                    <article class="hc-mode-card">
                        <h3>Encounter Cadence Assistant</h3>
                        <p>Track encounter checks and outcomes with a non-spatial procedure session. No Overworld is required.</p>
                        <button type="button" class="hc-primary-action" data-assistant-encounters>Open Encounter Cadence</button>
                    </article>
                </div>
            </details>
            <div class="hc-columns hc-home-three-column-grid hc-home-main-grid">
                <section class="hc-panel">
                    <div class="hc-panel-heading">
                        <div><h2>Running sheets</h2><p class="hc-muted">Continue an existing crawl record. A sheet can be world-bound, abstract-hex, or non-spatial.</p></div>
                        <span class="hc-muted" data-expedition-count></span>
                    </div>
                    <div class="hc-expedition-list" data-expedition-list></div>
                </section>
                <section class="hc-panel">
                    <h2>New running sheet</h2>
                    <p class="hc-muted">Choose only the context your procedure needs. You can run without an Overworld or map.</p>
                    <form class="hc-form" data-start-mapless>
                        <label>Session name <input name="name" required value="Expedition" autocomplete="off"></label>
                        <label>Procedure preset <select name="procedure"></select></label>
                        <p class="hc-hint" data-procedure-summary></p>
                        <label>Crawl context <select name="context"></select></label>
                        <div class="hc-form" data-abstract-context hidden>
                            <label>Context name <input name="contextName" value="Mapless hex crawl" autocomplete="off"></label>
                            <label>Hex orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                            <label>Hex center distance <input name="scale" type="number" min="0.001" step="any" value="12"></label>
                            <label>Distance unit <select name="unit"><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                            <div class="hc-form" data-custom-unit hidden>
                                <label>Custom symbol <input name="symbol" value="u" autocomplete="off"></label>
                                <label>Custom meters per unit <input name="meters" type="number" min="0.001" step="any" value="1"></label>
                            </div>
                            <div class="hc-inline">
                                <label>Start q <input name="q" type="number" step="1" value="0"></label>
                                <label>Start r <input name="r" type="number" step="1" value="0"></label>
                            </div>
                            <p class="hc-hint">Abstract hex stores only crawl-scale context. It creates no Overworld, source map, location, feature, or world row.</p>
                        </div>
                        <div data-nonspatial-context hidden>
                            <label>Context name <input name="nonSpatialName" value="Procedure session" autocomplete="off"></label>
                            <p class="hc-hint">Non-spatial sessions persist procedure/history state without hex coordinates, distance scale, world position, or Overworld.</p>
                        </div>
                        <button type="submit" class="hc-primary-action" data-start-button>Start session</button>
                    </form>
                </section>
            </div>
            <section class="hc-mode-section" aria-labelledby="hc-mode-title">
                <h2 id="hc-mode-title">Available levels of map support</h2>
                <div class="hc-mode-grid hc-home-three-column-grid">
                    <article class="hc-mode-card"><h3>World-bound</h3><p>Uses an authored Overworld and can add map rendering, discovery, and player knowledge.</p></article>
                    <article class="hc-mode-card"><h3>Abstract hex</h3><p>Uses persisted hex scale and coordinates without creating or loading an Overworld.</p></article>
                    <article class="hc-mode-card"><h3>Non-spatial</h3><p>Uses procedure/session bookkeeping without inventing map state.</p></article>
                </div>
            </section>
        </section>`;

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
    const nonSpatialContext = required<HTMLElement>(form, "[data-nonspatial-context]");
    const customUnit = required<HTMLElement>(form, "[data-custom-unit]");
    const startButton = required<HTMLButtonElement>(form, "[data-start-button]");
    let profiles: RuntimeProfile[] = [];

    const syncContext = (): void => {
        const abstract = context.value === ABSTRACT_CONTEXT;
        const nonSpatial = context.value === NON_SPATIAL_CONTEXT;
        abstractContext.hidden = !abstract;
        nonSpatialContext.hidden = !nonSpatial;
        input(form, "contextName").required = abstract;
        input(form, "scale").required = abstract;
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
        for (const world of worlds) context.append(option(world.id, `World: ${world.name}`));
        context.append(
            option(ABSTRACT_CONTEXT, "Abstract hex (no Overworld)"),
            option(NON_SPATIAL_CONTEXT, "Non-spatial procedure session")
        );
        if (worlds.length === 0) context.value = ABSTRACT_CONTEXT;

        count.textContent = expeditions.length === 1 ? "1 session" : `${expeditions.length} sessions`;
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
                const name = input(form, "name").value.trim();
                let expedition;
                if (context.value === ABSTRACT_CONTEXT) {
                    const unitKind = unit.value as DistanceUnitKind;
                    const request: StartStandaloneCrawlSessionInput = {
                        name,
                        procedureKey: procedure.value,
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
                    expedition = await api.startStandaloneSession(request);
                } else if (context.value === NON_SPATIAL_CONTEXT) {
                    expedition = await api.startStandaloneSession({
                        name,
                        procedureKey: procedure.value,
                        context: {
                            kind: "NonSpatial",
                            name: input(form, "nonSpatialName").value.trim()
                        }
                    });
                } else {
                    if (!context.value) throw new Error("A crawl context is required.");
                    expedition = await api.startConfiguredExpedition(context.value, {
                        name,
                        procedureKey: procedure.value,
                        presentationKey: "dm-controlled",
                        startHex: {
                            q: integer(input(form, "q")),
                            r: integer(input(form, "r"))
                        }
                    });
                }
                if (!disposed) navigate(`/expeditions/${expedition.id}`);
            } catch (value) {
                if (!disposed) showUiError(error, value);
            } finally {
                startPending = false;
                if (!disposed) {
                    startButton.disabled = false;
                    startButton.textContent = "Start session";
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
        empty.innerHTML = "<strong>No crawl sessions yet.</strong><span>Start one without creating a world, or bind one to an authored Overworld.</span>";
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
        const contextName = expedition.context.kind === "WorldBound" && expedition.context.overworldId
            ? worldNames.get(expedition.context.overworldId) ?? expedition.context.name
            : expedition.context.name;
        meta.textContent = `${expedition.procedureName} · ${contextLabel(expedition.context.kind)}: ${contextName} · updated ${formatTimestamp(expedition.updatedAt)}`;
        copy.append(title, meta);

        const actions = document.createElement("div");
        actions.className = "hc-button-row";
        actions.append(action("Open running sheet", () => navigate(`/expeditions/${expedition.id}`), true));

        const spatial = expedition.context.kind !== "NonSpatial";
        if (expedition.context.kind === "WorldBound" && expedition.context.overworldId) {
            actions.append(action("Map + sheet", () => navigate(`/worlds/${expedition.context.overworldId}/expeditions/${expedition.id}`)));
        }
        if (spatial) {
            actions.append(
                action("Travel / watch", () => navigate(`/expeditions/${expedition.id}/travel`)),
                action("Navigation", () => navigate(`/expeditions/${expedition.id}/navigation`))
            );
        } else {
            actions.append(action("Watch / time", () => navigate(`/expeditions/${expedition.id}/travel`)));
        }
        actions.append(action("Encounters", () => navigate(`/expeditions/${expedition.id}/encounters`)));

        card.append(copy, actions);
        host.append(card);
    }
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

