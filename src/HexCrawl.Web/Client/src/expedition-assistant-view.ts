import type { HexCrawlApi } from "./api";
import { manualEntryResolutionSources } from "./expedition-input-policy";
import { assistantEncounterCheckDue } from "./expedition-workflow";
import { directionLabel, formatDistance, formatHours } from "./runtime-view";
import type {
    EncounterCadenceAssistantRequest,
    ExpeditionDetail,
    NavigationAssistantRequest,
    ResolutionSource,
    TravelWatchAssistantRequest
} from "./types";
import { clearUiError, showUiError } from "./ui-error";

export type ExpeditionAssistantMode = "travel" | "navigation" | "encounters";

export async function renderExpeditionAssistant(
    root: HTMLElement,
    api: HexCrawlApi,
    expeditionId: string,
    mode: ExpeditionAssistantMode,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    let runtime = await api.getExpedition(expeditionId);
    let disposed = false;
    let pending = false;

    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div>
                    <h1 data-title></h1>
                    <p><span data-mode></span> · <span data-context></span></p>
                </div>
                <nav>
                    <button type="button" data-home>DM tools</button>
                    <button type="button" data-tracker>Expedition tracker</button>
                    <button type="button" data-map>Full map</button>
                </nav>
            </header>
            <div class="hc-view-switcher" aria-label="Focused assistants">
                <button type="button" data-travel>Travel / watch</button>
                <button type="button" data-navigation>Navigation</button>
                <button type="button" data-encounters>Encounter cadence</button>
            </div>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-assistant-grid">
                <section class="hc-panel">
                    <h2 data-assistant-heading></h2>
                    <p class="hc-muted" data-assistant-description></p>
                    <div class="hc-status-grid" data-status></div>
                    <div data-active-watch-warning hidden class="hc-assistant-warning"></div>
                    <form class="hc-form hc-assistant-form" data-form></form>
                </section>
                <section class="hc-panel">
                    <h2>Relevant history</h2>
                    <ol class="hc-history" data-history></ol>
                </section>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const form = required<HTMLFormElement>(root, "[data-form]");
    const warning = required<HTMLElement>(root, "[data-active-watch-warning]");

    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    required<HTMLButtonElement>(root, "[data-tracker]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}`));
    required<HTMLButtonElement>(root, "[data-map]").addEventListener("click", () => navigate(`/worlds/${runtime.overworldId}/expeditions/${runtime.id}`));
    required<HTMLButtonElement>(root, "[data-travel]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/travel`));
    required<HTMLButtonElement>(root, "[data-navigation]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/navigation`));
    required<HTMLButtonElement>(root, "[data-encounters]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/encounters`));

    const apply = (next: ExpeditionDetail): void => {
        runtime = next;
        required<HTMLElement>(root, "[data-title]").textContent = next.name;
        required<HTMLElement>(root, "[data-context]").textContent = `Crawl context: ${next.context.name}`;
        required<HTMLElement>(root, "[data-mode]").textContent = modeLabel(mode);
        renderStatus();
        renderHistory();
        renderForm();
    };

    const renderStatus = (): void => {
        const state = runtime.expedition;
        const cells = mode === "travel"
            ? [
                statusCell("Elapsed travel", formatHours(state.elapsedTravelHours)),
                statusCell("Distance", formatDistance(state.distanceTraveled)),
                statusCell("Current hex", `${state.currentHex.q}, ${state.currentHex.r}`),
                statusCell("Completed watches", String(state.completedWatches))
            ]
            : mode === "navigation"
                ? [
                    statusCell("Navigation", state.isLost ? "Lost" : "Oriented"),
                    statusCell("Veer", `${state.veerSteps} (${state.veerDegrees}°)`),
                    statusCell("Intended course", directionLabel(state.intendedDirection)),
                    statusCell("Actual course", directionLabel(state.actualDirection))
                ]
                : [
                    statusCell("Cadence", prettyEnum(runtime.profile.encounterCadence)),
                    statusCell("Current day", String(state.currentDay)),
                    statusCell("Upcoming watch", String(state.completedWatches + 1)),
                    statusCell("Check due", assistantEncounterCheckDue(runtime) ? "Yes" : "No")
                ];
        required<HTMLElement>(root, "[data-status]").replaceChildren(...cells);
    };

    const renderHistory = (): void => {
        const relevant = runtime.history.filter(event => relevantEvent(mode, event.kind, event.message)).reverse().slice(0, 30);
        const host = required<HTMLOListElement>(root, "[data-history]");
        host.replaceChildren();
        if (relevant.length === 0) {
            const item = document.createElement("li");
            item.textContent = "No focused-assistant history yet.";
            host.append(item);
            return;
        }
        for (const event of relevant) {
            const item = document.createElement("li");
            item.textContent = `#${event.sequence} · ${formatHours(event.expeditionElapsedHours)} · ${event.message}`;
            host.append(item);
        }
    };

    const renderForm = (): void => {
        const state = runtime.expedition;
        const activeFullWatch = state.activeWatchNumber !== null;
        warning.hidden = !activeFullWatch;
        warning.textContent = activeFullWatch
            ? `A full-workbench watch is active. Finish or resume watch ${state.activeWatchNumber} in the expedition tracker before using independent assistant bookkeeping.`
            : "";

        required<HTMLElement>(root, "[data-assistant-heading]").textContent = heading(mode);
        required<HTMLElement>(root, "[data-assistant-description]").textContent = description(mode);
        form.innerHTML = mode === "travel" ? travelForm(runtime) : mode === "navigation" ? navigationForm(runtime) : encounterForm(runtime);
        populateSources(form);
        const submit = required<HTMLButtonElement>(form, 'button[type="submit"]');
        submit.disabled = activeFullWatch;
        form.querySelectorAll<HTMLInputElement | HTMLSelectElement>("input, select").forEach(control => control.disabled = activeFullWatch);
        if (mode === "navigation") {
            const sync = (): void => {
                const lost = checkbox(form, "isLost").checked;
                input(form, "veerSteps").disabled = activeFullWatch || !lost;
                if (!lost) input(form, "veerSteps").value = "0";
                else if (input(form, "veerSteps").value === "0") input(form, "veerSteps").value = "1";
            };
            checkbox(form, "isLost").addEventListener("change", sync);
            sync();
        }
    };

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (pending || runtime.expedition.activeWatchNumber !== null) return;
        void (async () => {
            clearUiError(error);
            pending = true;
            const submit = required<HTMLButtonElement>(form, 'button[type="submit"]');
            submit.disabled = true;
            const idle = submit.textContent ?? "Save";
            submit.textContent = "Saving…";
            try {
                const next = mode === "travel"
                    ? await api.recordTravelAssistant(runtime.id, travelRequest(form, runtime))
                    : mode === "navigation"
                        ? await api.recordNavigationAssistant(runtime.id, navigationRequest(form, runtime))
                        : await api.recordEncounterAssistant(runtime.id, encounterRequest(form, runtime));
                if (!disposed) apply(next);
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

    apply(runtime);
    return () => { disposed = true; };
}

function travelForm(runtime: ExpeditionDetail): string {
    const state = runtime.expedition;
    const scale = runtime.context.hexCenterDistance.value;
    const travelInput = runtime.profile.travelResolution === "HexSteps"
        ? `<label>Resolved hex steps <input name="hexSteps" type="number" min="0" step="1" value="1"></label>`
        : `<label>Distance traveled (${runtime.context.hexCenterDistance.unit.symbol}) <input name="distance" type="number" min="0" step="any" value="${scale}"></label>`;
    const progress = runtime.profile.tracksIntraHexProgress
        ? `<label>Resulting intra-hex progress (${runtime.context.hexCenterDistance.unit.symbol}) <input name="hexProgress" type="number" min="0" step="any" value="${state.hexProgress.value}"></label>`
        : "";
    return `
        <label>Elapsed travel hours <input name="elapsedHours" type="number" min="0" step="any" value="${runtime.profile.watchHours}"></label>
        ${travelInput}
        <div class="hc-inline">
            <label>Resulting hex q <input name="q" type="number" step="1" value="${state.currentHex.q}"></label>
            <label>Resulting hex r <input name="r" type="number" step="1" value="${state.currentHex.r}"></label>
        </div>
        ${progress}
        <label>Intended direction <select name="intendedDirection">${directionOptions(state.intendedDirection ?? 0)}</select></label>
        <label>Actual direction override <select name="actualDirection"><option value="">Derive from navigation state</option>${directionOptions(state.actualDirection)}</select></label>
        <label><input name="completeWatch" type="checkbox" checked> Mark one watch complete</label>
        ${provenanceFields("travel")}
        <button type="submit" class="hc-primary-action">Record travel / watch</button>`;
}

function navigationForm(runtime: ExpeditionDetail): string {
    const state = runtime.expedition;
    return `
        <label>Intended direction <select name="intendedDirection">${directionOptions(state.intendedDirection ?? 0)}</select></label>
        <label><input name="isLost" type="checkbox" ${state.isLost ? "checked" : ""}> Expedition is lost</label>
        <label>Veer steps <input name="veerSteps" type="number" step="1" value="${state.isLost ? state.veerSteps : 0}"></label>
        ${provenanceFields("navigation")}
        <button type="submit" class="hc-primary-action">Record navigation state</button>`;
}

function encounterForm(runtime: ExpeditionDetail): string {
    return `
        <p class="hc-hint">${assistantEncounterCheckDue(runtime) ? "The configured cadence indicates a check is due." : "The configured cadence does not currently indicate a new check is due. You may still record a manual check."}</p>
        <label>Outcome <select name="outcome">
            <option value="None">No encounter</option>
            <option value="WanderingEncounter">Wandering encounter</option>
            <option value="ManualCustom">Manual / custom encounter</option>
        </select></label>
        <label>Encounter note <input name="note" placeholder="optional"></label>
        ${provenanceFields("encounter")}
        <button type="submit" class="hc-primary-action">Record encounter check</button>`;
}

function provenanceFields(prefix: string): string {
    return `
        <label>Result source <select name="source" data-source></select></label>
        <label>Source note <input name="resolutionNote" placeholder="optional"></label>
        ${prefix === "encounter" ? "" : '<label>Bookkeeping note <input name="note" placeholder="optional"></label>'}`;
}

function populateSources(form: HTMLFormElement): void {
    const source = select(form, "source");
    for (const value of manualEntryResolutionSources) source.append(option(value, sourceLabel(value)));
    source.value = "ManualRoll";
}

function travelRequest(form: HTMLFormElement, runtime: ExpeditionDetail): TravelWatchAssistantRequest {
    const request: TravelWatchAssistantRequest = {
        expectedVersion: runtime.version,
        elapsedHours: numeric(input(form, "elapsedHours")),
        resultingHex: { q: integer(input(form, "q")), r: integer(input(form, "r")) },
        intendedDirection: integer(select(form, "intendedDirection")),
        completeWatch: checkbox(form, "completeWatch").checked,
        resolutionSource: select(form, "source").value as ResolutionSource,
        resolutionNote: optionalText(input(form, "resolutionNote")),
        note: optionalText(input(form, "note"))
    };
    if (runtime.profile.travelResolution === "HexSteps") request.hexSteps = integer(input(form, "hexSteps"));
    else request.distance = numeric(input(form, "distance"));
    if (runtime.profile.tracksIntraHexProgress) request.hexProgress = numeric(input(form, "hexProgress"));
    const actual = select(form, "actualDirection").value;
    if (actual) request.actualDirection = Number(actual);
    return request;
}

function navigationRequest(form: HTMLFormElement, runtime: ExpeditionDetail): NavigationAssistantRequest {
    return {
        expectedVersion: runtime.version,
        isLost: checkbox(form, "isLost").checked,
        veerSteps: integer(input(form, "veerSteps")),
        intendedDirection: integer(select(form, "intendedDirection")),
        resolutionSource: select(form, "source").value as ResolutionSource,
        resolutionNote: optionalText(input(form, "resolutionNote")),
        note: optionalText(input(form, "note"))
    };
}

function encounterRequest(form: HTMLFormElement, runtime: ExpeditionDetail): EncounterCadenceAssistantRequest {
    return {
        expectedVersion: runtime.version,
        outcome: select(form, "outcome").value as EncounterCadenceAssistantRequest["outcome"],
        resolutionSource: select(form, "source").value as ResolutionSource,
        resolutionNote: optionalText(input(form, "resolutionNote")),
        note: optionalText(input(form, "note"))
    };
}

function relevantEvent(mode: ExpeditionAssistantMode, kind: string, message: string): boolean {
    if (kind === "ResolutionProvenanceRecorded") return message.includes(`${mode === "travel" ? "travel" : mode === "navigation" ? "navigation" : "encounter"}-assistant=`);
    if (mode === "travel") return ["TravelResolved", "DistanceTraveled", "HexExited", "HexEntered", "WatchCompleted"].includes(kind);
    if (mode === "navigation") return ["NavigationCheckResolved", "ExpeditionBecameLost", "ExpeditionReoriented", "VeerChanged", "VeerReset"].includes(kind);
    return ["EncounterCheckPerformed", "EncounterTriggered"].includes(kind);
}

function modeLabel(mode: ExpeditionAssistantMode): string {
    return mode === "travel" ? "Travel / watch assistant" : mode === "navigation" ? "Navigation assistant" : "Encounter cadence assistant";
}

function heading(mode: ExpeditionAssistantMode): string {
    return mode === "travel" ? "Travel / watch bookkeeping" : mode === "navigation" ? "Navigation / lost / veer" : "Encounter cadence";
}

function description(mode: ExpeditionAssistantMode): string {
    if (mode === "travel") return "Record elapsed travel, resolved distance or hex steps, resulting hex/progress, and watch completion without running navigation or encounter automation.";
    if (mode === "navigation") return "Record authoritative oriented/lost state and veer without advancing travel or resolving encounters.";
    return "Record encounter-cadence checks and non-geographic outcomes without advancing travel. Keyed-location discovery remains part of world/map composition.";
}

function directionOptions(selected: number | null): string {
    return [0, 1, 2, 3, 4, 5]
        .map(value => `<option value="${value}" ${selected === value ? "selected" : ""}>${value}</option>`)
        .join("");
}

function statusCell(label: string, value: string): HTMLElement {
    const cell = document.createElement("div");
    const strong = document.createElement("strong");
    strong.textContent = label;
    const span = document.createElement("span");
    span.textContent = value;
    cell.append(strong, span);
    return cell;
}

function option(value: string, label: string): HTMLOptionElement {
    const result = document.createElement("option");
    result.value = value;
    result.textContent = label;
    return result;
}

function sourceLabel(source: ResolutionSource): string {
    switch (source) {
        case "ProcedureDefault": return "Procedure / default";
        case "AutomaticRoll": return "Automatic helper roll";
        case "ManualRoll": return "Manual roll / result";
        case "ExternalSystem": return "External system";
        case "DmOverride": return "DM override";
    }
}

function prettyEnum(value: string): string {
    return value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/_/g, " ").toLowerCase();
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

function checkbox(root: ParentNode, name: string): HTMLInputElement {
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

function integer(element: HTMLInputElement | HTMLSelectElement): number {
    const value = Number(element.value);
    if (!Number.isInteger(value)) throw new Error(`${element.getAttribute("name") ?? "value"} must be an integer.`);
    return value;
}

function optionalText(element: HTMLInputElement): string | undefined {
    const value = element.value.trim();
    return value || undefined;
}
