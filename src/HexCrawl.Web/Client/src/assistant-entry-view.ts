import type { HexCrawlApi } from "./api";
import { customUnitFieldsVisible } from "./world-form";
import type { DistanceUnitKind } from "./world-form";
import type { ExpeditionAssistant } from "./tool-route";
import type {
    ExpeditionSummary,
    RuntimeProfile,
    StartStandaloneCrawlSessionInput
} from "./types";
import { clearUiError, showUiError } from "./ui-error";

export async function renderAssistantEntry(
    root: HTMLElement,
    api: HexCrawlApi,
    assistant: ExpeditionAssistant,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    let disposed = false;
    let pending = false;

    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div>
                    <h1>${title(assistant)}</h1>
                    <p>${subtitle(assistant)}</p>
                </div>
                <nav><button type="button" data-home>DM tools</button></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-columns">
                <section class="hc-panel">
                    <div class="hc-panel-heading">
                        <div>
                            <h2>Continue a saved session</h2>
                            <p class="hc-muted">${existingHint(assistant)}</p>
                        </div>
                        <span class="hc-muted" data-count></span>
                    </div>
                    <div class="hc-expedition-list" data-sessions></div>
                </section>
                <section class="hc-panel">
                    <h2>${createHeading(assistant)}</h2>
                    <p class="hc-muted">${createHint(assistant)}</p>
                    <form class="hc-form" data-create>
                        <label>Session name <input name="name" required autocomplete="off"></label>
                        <label>Procedure preset <select name="procedure"></select></label>
                        <p class="hc-hint" data-procedure-summary></p>
                        ${assistant === "travel" ? `
                            <label>Bookkeeping mode
                                <select name="mode">
                                    <option value="nonspatial">Watch / time only</option>
                                    <option value="abstract">Spatial travel on an abstract hex grid</option>
                                </select>
                            </label>
                        ` : ""}
                        <div data-nonspatial>
                            <label>Context name <input name="nonSpatialName" value="${defaultContextName(assistant, false)}" autocomplete="off"></label>
                            <p class="hc-hint">No Overworld, grid, coordinates, direction, or distance scale will be created.</p>
                        </div>
                        <div class="hc-form" data-abstract hidden>
                            <label>Context name <input name="contextName" value="${defaultContextName(assistant, true)}" autocomplete="off"></label>
                            <label>Hex orientation
                                <select name="orientation">
                                    <option value="PointyTop">Pointy top</option>
                                    <option value="FlatTop">Flat top</option>
                                </select>
                            </label>
                            <label>Hex center distance <input name="scale" type="number" min="0.001" step="any" value="12"></label>
                            <label>Distance unit
                                <select name="unit">
                                    <option value="Mile">Miles</option>
                                    <option value="Kilometer">Kilometers</option>
                                    <option value="Custom">Custom</option>
                                </select>
                            </label>
                            <div class="hc-form" data-custom-unit hidden>
                                <label>Custom symbol <input name="symbol" value="u" autocomplete="off"></label>
                                <label>Custom meters per unit <input name="meters" type="number" min="0.001" step="any" value="1"></label>
                            </div>
                            <div class="hc-inline">
                                <label>Start q <input name="q" type="number" step="1" value="0"></label>
                                <label>Start r <input name="r" type="number" step="1" value="0"></label>
                            </div>
                            <p class="hc-hint">This creates only mathematical hex context. It does not create or load an Overworld.</p>
                        </div>
                        <button type="submit" class="hc-primary-action" data-submit>${createButton(assistant)}</button>
                    </form>
                </section>
            </div>
        </section>`;

    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    const error = required<HTMLElement>(root, "[data-error]");
    const list = required<HTMLElement>(root, "[data-sessions]");
    const count = required<HTMLElement>(root, "[data-count]");
    const form = required<HTMLFormElement>(root, "[data-create]");
    const procedure = select(form, "procedure");
    const abstractPanel = required<HTMLElement>(form, "[data-abstract]");
    const nonSpatialPanel = required<HTMLElement>(form, "[data-nonspatial]");
    const customUnitPanel = required<HTMLElement>(form, "[data-custom-unit]");
    const submit = required<HTMLButtonElement>(form, "[data-submit]");
    input(form, "name").value = defaultSessionName(assistant);

    const [sessions, profiles] = await Promise.all([
        api.listExpeditions(),
        api.getRuntimeProfiles()
    ]);
    if (disposed) return () => {};

    const compatible = sessions.filter(session => compatibleSession(assistant, session));
    renderSessions(list, compatible, assistant, navigate);
    count.textContent = compatible.length === 1 ? "1 compatible session" : `${compatible.length} compatible sessions`;

    for (const profile of profiles) procedure.append(option(profile.key, profile.name));
    chooseDefaultProcedure(procedure, profiles, assistant);

    const mode = form.querySelector<HTMLSelectElement>('select[name="mode"]');
    const unit = select(form, "unit");

    const usesAbstract = (): boolean =>
        assistant === "navigation" || (assistant === "travel" && mode?.value === "abstract");

    const syncMode = (): void => {
        const abstract = usesAbstract();
        abstractPanel.hidden = !abstract;
        nonSpatialPanel.hidden = abstract;
        input(form, "contextName").required = abstract;
        input(form, "scale").required = abstract;
        input(form, "q").required = abstract;
        input(form, "r").required = abstract;
        input(form, "nonSpatialName").required = !abstract;
        syncUnit();
    };

    const syncUnit = (): void => {
        const custom = usesAbstract() && customUnitFieldsVisible(unit.value as DistanceUnitKind);
        customUnitPanel.hidden = !custom;
        input(form, "symbol").required = custom;
        input(form, "meters").required = custom;
    };

    const syncProcedure = (): void => {
        const profile = profiles.find(candidate => candidate.key === procedure.value);
        required<HTMLElement>(form, "[data-procedure-summary]").textContent = profile
            ? profileSummary(profile)
            : "";
    };

    mode?.addEventListener("change", syncMode);
    unit.addEventListener("change", syncUnit);
    procedure.addEventListener("change", syncProcedure);
    syncMode();
    syncProcedure();

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (pending) return;
        void (async () => {
            clearUiError(error);
            pending = true;
            submit.disabled = true;
            const idle = submit.textContent ?? "Create";
            submit.textContent = "Creating…";
            try {
                if (!procedure.value) throw new Error("A procedure preset is required.");
                const request = buildRequest(form, assistant, usesAbstract(), procedure.value);
                const session = await api.startStandaloneSession(request);
                if (!disposed) navigate(`/expeditions/${session.id}/${assistant}`);
            } catch (value) {
                if (!disposed) showUiError(error, value);
            } finally {
                pending = false;
                if (!disposed) {
                    submit.disabled = false;
                    submit.textContent = idle;
                }
            }
        })();
    });

    return () => { disposed = true; };
}

function renderSessions(
    host: HTMLElement,
    sessions: ExpeditionSummary[],
    assistant: ExpeditionAssistant,
    navigate: (route: string, replace?: boolean) => void): void {
    host.replaceChildren();
    if (sessions.length === 0) {
        const empty = document.createElement("div");
        empty.className = "hc-empty-state";
        empty.innerHTML = "<strong>No compatible saved sessions.</strong><span>Create the minimum context here and enter the assistant immediately.</span>";
        host.append(empty);
        return;
    }

    for (const session of sessions) {
        const card = document.createElement("article");
        card.className = "hc-expedition-card";
        const copy = document.createElement("div");
        const heading = document.createElement("h3");
        heading.textContent = session.name;
        const meta = document.createElement("p");
        meta.className = "hc-muted";
        meta.textContent = `${contextLabel(session)} · ${session.procedureName}`;
        copy.append(heading, meta);

        const actions = document.createElement("div");
        actions.className = "hc-button-row";
        const open = document.createElement("button");
        open.type = "button";
        open.className = "hc-primary-action";
        open.textContent = openLabel(assistant, session);
        open.addEventListener("click", () => navigate(`/expeditions/${session.id}/${assistant}`));
        actions.append(open);

        card.append(copy, actions);
        host.append(card);
    }
}

function compatibleSession(assistant: ExpeditionAssistant, session: ExpeditionSummary): boolean {
    if (assistant === "navigation") return session.context.kind !== "NonSpatial";
    return true;
}

function buildRequest(
    form: HTMLFormElement,
    assistant: ExpeditionAssistant,
    abstract: boolean,
    procedureKey: string): StartStandaloneCrawlSessionInput {
    const name = input(form, "name").value.trim();
    if (!name) throw new Error("Session name is required.");

    if (!abstract) {
        return {
            name,
            procedureKey,
            context: {
                kind: "NonSpatial",
                name: input(form, "nonSpatialName").value.trim()
            }
        };
    }

    const unitKind = select(form, "unit").value as DistanceUnitKind;
    return {
        name,
        procedureKey,
        context: {
            kind: "AbstractHex",
            name: input(form, "contextName").value.trim(),
            orientation: select(form, "orientation").value === "FlatTop" ? "FlatTop" : "PointyTop",
            hexCenterDistance: positive(input(form, "scale")),
            distanceUnit: distanceUnit(unitKind, form)
        },
        startHex: {
            q: integer(input(form, "q")),
            r: integer(input(form, "r"))
        }
    };
}

function distanceUnit(kind: DistanceUnitKind, form: HTMLFormElement) {
    if (kind === "Mile") return { kind, symbol: "mi", metersPerUnit: 1609.344 };
    if (kind === "Kilometer") return { kind, symbol: "km", metersPerUnit: 1000 };
    return {
        kind,
        symbol: input(form, "symbol").value.trim(),
        metersPerUnit: positive(input(form, "meters"))
    };
}

function chooseDefaultProcedure(
    selectElement: HTMLSelectElement,
    profiles: RuntimeProfile[],
    assistant: ExpeditionAssistant): void {
    const preferred = assistant === "travel" ? "simple-fixed-distance" : "alexandrian-advanced";
    if (profiles.some(profile => profile.key === preferred)) selectElement.value = preferred;
}

function profileSummary(profile: RuntimeProfile): string {
    return `${profile.watchHours}h watches · ${profile.travelResolution === "HexSteps" ? "hex-step travel" : profile.actualDistanceResolution === "Fixed" ? "fixed distance" : "resolved variable distance"} · ${profile.usesNavigationChecks ? "navigation checks" : "no navigation checks"} · encounters ${profile.encounterCadence.toLowerCase()}`;
}

function title(assistant: ExpeditionAssistant): string {
    if (assistant === "travel") return "Travel / Watch Assistant";
    if (assistant === "navigation") return "Navigation Assistant";
    return "Encounter Cadence Assistant";
}

function subtitle(assistant: ExpeditionAssistant): string {
    if (assistant === "travel") return "Start with generic watch/time bookkeeping, or opt into abstract-hex spatial travel. No Overworld is required.";
    if (assistant === "navigation") return "Use an existing spatial crawl or create only the abstract hex context required for direction and navigation.";
    return "Record encounter cadence against a saved procedure session without requiring a world, grid, or distance scale.";
}

function existingHint(assistant: ExpeditionAssistant): string {
    return assistant === "navigation"
        ? "World-bound and abstract-hex sessions are compatible. Non-spatial sessions are excluded because navigation needs direction."
        : "Open a compatible saved session directly; no world-editor navigation is required.";
}

function createHeading(assistant: ExpeditionAssistant): string {
    if (assistant === "navigation") return "Create abstract navigation context";
    if (assistant === "encounters") return "Create encounter procedure session";
    return "Create watch / travel session";
}

function createButton(assistant: ExpeditionAssistant): string {
    if (assistant === "navigation") return "Create and open navigation";
    if (assistant === "encounters") return "Create and open encounter cadence";
    return "Create and open assistant";
}

function createHint(assistant: ExpeditionAssistant): string {
    if (assistant === "navigation") return "Creates an AbstractHex session only. No Overworld or map authoring is involved.";
    if (assistant === "encounters") return "Creates a NonSpatial session with procedure/history state only.";
    return "Watch/time defaults to NonSpatial. Choose abstract hex only when you actually want spatial travel bookkeeping.";
}

function defaultSessionName(assistant: ExpeditionAssistant): string {
    if (assistant === "travel") return "Watch / travel session";
    if (assistant === "navigation") return "Navigation session";
    return "Encounter cadence session";
}

function defaultContextName(assistant: ExpeditionAssistant, spatial: boolean): string {
    if (spatial) return assistant === "navigation" ? "Abstract navigation grid" : "Abstract travel grid";
    return assistant === "encounters" ? "Encounter procedure" : "Watch procedure";
}

function contextLabel(session: ExpeditionSummary): string {
    if (session.context.kind === "WorldBound") return `World-bound: ${session.context.name}`;
    if (session.context.kind === "AbstractHex") return `Abstract hex: ${session.context.name}`;
    return `Non-spatial: ${session.context.name}`;
}

function openLabel(assistant: ExpeditionAssistant, session: ExpeditionSummary): string {
    if (assistant === "travel" && session.context.kind === "NonSpatial") return "Open watch / time";
    if (assistant === "travel") return "Open travel / watch";
    if (assistant === "navigation") return "Open navigation";
    return "Open encounter cadence";
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

function positive(element: HTMLInputElement): number {
    const value = numeric(element);
    if (value <= 0) throw new Error(`${element.name} must be positive.`);
    return value;
}

function integer(element: HTMLInputElement): number {
    const value = numeric(element);
    if (!Number.isInteger(value)) throw new Error(`${element.name} must be an integer.`);
    return value;
}
