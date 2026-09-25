import type { HexCrawlApi } from "../../api";
import { manualEntryResolutionSources } from "../expeditions/expedition-input-policy";
import { assistantEncounterCheckDue } from "../expeditions/expedition-workflow";
import { directionLabel, formatDistance, formatHours } from "../../runtime-view";
import type {
    EncounterCadenceAssistantRequest,
    ExpeditionDetail,
    NavigationAssistantRequest,
    NonSpatialWatchAssistantRequest,
    ResolutionSource,
    TravelWatchAssistantRequest,
    SpatialRuntimeExpedition
} from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { checkbox, input, integer, numeric, option, optionalText, prettyEnum, required, select, sourceLabel, statusCell } from "../../ui/dom";

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
    const mapButton = required<HTMLButtonElement>(root, "[data-map]");
    mapButton.hidden = runtime.overworldId === null;
    mapButton.addEventListener("click", () => {
        if (runtime.overworldId) navigate(`/worlds/${runtime.overworldId}/expeditions/${runtime.id}`);
    });
    required<HTMLButtonElement>(root, "[data-travel]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/travel`));
    required<HTMLButtonElement>(root, "[data-navigation]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/navigation`));
    required<HTMLButtonElement>(root, "[data-encounters]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/encounters`));

    const apply = (next: ExpeditionDetail): void => {
        runtime = next;
        required<HTMLElement>(root, "[data-title]").textContent = next.name;
        required<HTMLElement>(root, "[data-context]").textContent = `Crawl context: ${next.context.name}`;
        required<HTMLElement>(root, "[data-mode]").textContent = modeLabel(mode, next.expedition.isSpatial);
        const travelButton = required<HTMLButtonElement>(root, "[data-travel]");
        travelButton.hidden = false;
        travelButton.textContent = next.expedition.isSpatial ? "Travel / watch" : "Watch / time";
        required<HTMLButtonElement>(root, "[data-navigation]").hidden = !next.expedition.isSpatial;
        mapButton.hidden = next.overworldId === null;
        renderStatus();
        renderHistory();
        renderForm();
    };

    const renderStatus = (): void => {
        const state = runtime.expedition;
        const cells = mode === "travel"
            ? state.isSpatial
                ? travelStatus(state)
                : nonSpatialWatchStatus(runtime)
            : mode === "navigation"
                ? navigationStatus(spatialState(runtime))
                : [
                    statusCell("Cadence", prettyEnum(runtime.profile.encounterCadence)),
                    statusCell("Current day", String(state.currentDay)),
                    statusCell("Upcoming watch", String(state.completedWatches + 1)),
                    statusCell("Check due", assistantEncounterCheckDue(runtime) ? "Yes" : "No"),
                    statusCell("Context", runtime.context.kind === "NonSpatial" ? "Non-spatial" : runtime.context.kind === "AbstractHex" ? "Abstract hex" : "World-bound")
                ];
        required<HTMLElement>(root, "[data-status]").replaceChildren(...cells);
    };

    const renderHistory = (): void => {
        const relevant = runtime.history.filter(event => relevantEvent(mode, event.kind, event.message)).reverse().slice(0, 30);
        const host = required<HTMLOListElement>(root, "[data-history]");
        host.replaceChildren();
        if (relevant.length === 0) {
            const item = document.createElement("li");
            item.textContent = "No relevant history yet. Record the first result in this assistant to create history.";
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
        const activeFullWatch = state.isSpatial && state.activeWatchNumber !== null;
        warning.hidden = !activeFullWatch;
        warning.textContent = activeFullWatch
            ? `A full-workbench watch is active. Finish or resume watch ${state.activeWatchNumber} in the expedition tracker before using independent assistant bookkeeping.`
            : "";

        required<HTMLElement>(root, "[data-assistant-heading]").textContent = heading(mode, state.isSpatial);
        required<HTMLElement>(root, "[data-assistant-description]").textContent = description(mode, state.isSpatial);
        if (!state.isSpatial && mode === "navigation") {
            form.innerHTML = '<p class="hc-hint">Navigation requires a spatial crawl context. This session intentionally has no direction, position, or grid state.</p>';
            warning.hidden = true;
            return;
        }
        form.innerHTML = mode === "travel"
            ? state.isSpatial ? travelForm(runtime) : nonSpatialWatchForm(runtime)
            : mode === "navigation" ? navigationForm(runtime) : encounterForm(runtime);
        populateSources(form);
        if (!state.isSpatial && mode === "travel") {
            select(form, "source").value = "ProcedureDefault";
        }
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
        if (pending || (runtime.expedition.isSpatial && runtime.expedition.activeWatchNumber !== null)) return;
        void (async () => {
            clearUiError(error);
            pending = true;
            const submit = required<HTMLButtonElement>(form, 'button[type="submit"]');
            submit.disabled = true;
            const idle = submit.textContent ?? "Save";
            submit.textContent = "Saving…";
            try {
                const next = mode === "travel"
                    ? runtime.expedition.isSpatial
                        ? await api.recordTravelAssistant(runtime.id, travelRequest(form, runtime))
                        : await api.recordWatchAssistant(runtime.id, nonSpatialWatchRequest(form, runtime))
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
    const state = spatialState(runtime);
    const contextDistance = runtime.context.hexCenterDistance;
    if (!contextDistance) throw new Error("Spatial crawl context is missing its hex-center distance.");
    const travelInput = runtime.profile.travelResolution === "HexSteps"
        ? `<label>Resolved hex steps <input name="hexSteps" type="number" min="0" step="1" required></label>`
        : `<label>Distance traveled (${contextDistance.unit.symbol}) <input name="distance" type="number" min="0" step="any" required></label>`;
    const progress = runtime.profile.tracksIntraHexProgress
        ? `<label>Resulting intra-hex progress (${contextDistance.unit.symbol}) <input name="hexProgress" type="number" min="0" step="any" value="${state.hexProgress.value}"></label>`
        : "";
    return `
        <p class="hc-hint">Enter the movement result you already resolved. This assistant does not infer distance or hex steps from the map scale, procedure name, pace, or terrain.</p>
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

function nonSpatialWatchForm(runtime: ExpeditionDetail): string {
    const state = runtime.expedition;
    if (state.isSpatial) throw new Error("Expected a non-spatial crawl session.");
    const watchNumber = state.activeWatchNumber ?? state.completedWatches + 1;
    const total = state.activeWatchTotalHours ?? runtime.profile.watchHours;
    const elapsed = state.activeWatchElapsedHours ?? 0;
    const remaining = state.activeWatchRemainingHours ?? total;
    return `
        <p class="hc-hint">Watch ${watchNumber} · configured length ${formatHours(total)} · elapsed ${formatHours(elapsed)} · remaining ${formatHours(remaining)}.</p>
        <label>Elapsed time to record <input name="elapsedHours" type="number" min="0.001" max="${remaining}" step="any" value="${remaining}"></label>
        ${provenanceFields("watch")}
        <button type="submit" class="hc-primary-action">${state.activeWatchNumber === null ? "Start / record watch" : "Resume watch"}</button>`;
}

function navigationForm(runtime: ExpeditionDetail): string {
    const state = spatialState(runtime);
    return `
        <label>Intended direction <select name="intendedDirection">${directionOptions(state.intendedDirection ?? 0)}</select></label>
        <label><input name="isLost" type="checkbox" ${state.isLost ? "checked" : ""}> Expedition is lost</label>
        <label>Veer steps <input name="veerSteps" type="number" step="1" value="${state.isLost ? state.veerSteps : 0}"></label>
        ${provenanceFields("navigation")}
        <button type="submit" class="hc-primary-action">Record navigation state</button>`;
}

function encounterForm(runtime: ExpeditionDetail): string {
    const automaticHelperNote = runtime.profile.resolutionHelpers?.encounter
        ? '<p class="hc-hint">This procedure also configures an automatic encounter helper. This focused assistant does not apply it because the helper also resolves encounter timing; use this surface for manual, external, or DM-override bookkeeping.</p>'
        : "";
    return `
        <p class="hc-hint">${assistantEncounterCheckDue(runtime) ? "The configured cadence indicates a check is due." : "The configured cadence does not currently indicate a new check is due. You may still record a manual check."}</p>
        ${automaticHelperNote}
        <label>Outcome <select name="outcome" required>
            <option value="">Select resolved outcome</option>
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
        <label>Result source <select name="source" data-source required></select></label>
        <label>Source note <input name="resolutionNote" placeholder="optional"></label>
        ${prefix === "encounter" ? "" : '<label>Bookkeeping note <input name="note" placeholder="optional"></label>'}`;
}

function populateSources(form: HTMLFormElement): void {
    const source = select(form, "source");
    source.append(option("", "Select result source"));
    for (const value of manualEntryResolutionSources) source.append(option(value, sourceLabel(value)));
    source.value = "";
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

function nonSpatialWatchRequest(form: HTMLFormElement, runtime: ExpeditionDetail): NonSpatialWatchAssistantRequest {
    return {
        expectedVersion: runtime.version,
        elapsedHours: numeric(input(form, "elapsedHours")),
        resolutionSource: select(form, "source").value as ResolutionSource,
        resolutionNote: optionalText(input(form, "resolutionNote")),
        note: optionalText(input(form, "note"))
    };
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
    if (kind === "ResolutionProvenanceRecorded") {
        if (mode === "travel") return message.includes("travel-assistant=") || message.includes("watch-assistant=");
        return message.includes(`${mode === "navigation" ? "navigation" : "encounter"}-assistant=`);
    }
    if (mode === "travel") return ["WatchStarted", "WatchTimeAdvanced", "TravelResolved", "DistanceTraveled", "HexExited", "HexEntered", "WatchCompleted", "DmOverrideApplied"].includes(kind);
    if (mode === "navigation") return ["NavigationCheckResolved", "ExpeditionBecameLost", "ExpeditionReoriented", "VeerChanged", "VeerReset"].includes(kind);
    return ["EncounterCheckPerformed", "EncounterTriggered"].includes(kind);
}

function modeLabel(mode: ExpeditionAssistantMode, spatial: boolean): string {
    if (mode === "travel") return spatial ? "Travel / watch assistant" : "Watch / time assistant";
    return mode === "navigation" ? "Navigation assistant" : "Encounter cadence assistant";
}

function heading(mode: ExpeditionAssistantMode, spatial: boolean): string {
    if (mode === "travel") return spatial ? "Travel / watch bookkeeping" : "Watch / time bookkeeping";
    return mode === "navigation" ? "Navigation / lost / veer" : "Encounter cadence";
}

function description(mode: ExpeditionAssistantMode, spatial: boolean): string {
    if (mode === "travel") {
        return spatial
            ? "Record elapsed travel, resolved distance or hex steps, resulting hex/progress, and watch completion without running navigation or encounter automation."
            : "Record configured watch time, partial progress, resume state, completion, provenance, and notes without inventing spatial travel state.";
    }
    if (mode === "navigation") return "Record authoritative oriented/lost state and veer without advancing travel or resolving encounters.";
    return "Record encounter-cadence checks and non-geographic outcomes without advancing travel. Keyed-location discovery remains part of world/map composition.";
}

function spatialState(runtime: ExpeditionDetail): SpatialRuntimeExpedition {
    if (!runtime.expedition.isSpatial) {
        throw new Error("This assistant requires a spatial crawl session.");
    }
    return runtime.expedition;
}

function travelStatus(state: SpatialRuntimeExpedition): HTMLElement[] {
    return [
        statusCell("Elapsed travel", formatHours(state.elapsedTravelHours)),
        statusCell("Distance", formatDistance(state.distanceTraveled)),
        statusCell("Current hex", `${state.currentHex.q}, ${state.currentHex.r}`),
        statusCell("Completed watches", String(state.completedWatches))
    ];
}

function nonSpatialWatchStatus(runtime: ExpeditionDetail): HTMLElement[] {
    const state = runtime.expedition;
    if (state.isSpatial) throw new Error("Expected a non-spatial crawl session.");
    const watch = state.activeWatchNumber === null
        ? `Ready for watch ${state.completedWatches + 1}`
        : `Watch ${state.activeWatchNumber}`;
    return [
        statusCell("Watch", watch),
        statusCell("Configured length", formatHours(state.activeWatchTotalHours ?? runtime.profile.watchHours)),
        statusCell("Watch elapsed", formatHours(state.activeWatchElapsedHours ?? 0)),
        statusCell("Watch remaining", formatHours(state.activeWatchRemainingHours ?? runtime.profile.watchHours)),
        statusCell("Completed watches", String(state.completedWatches)),
        statusCell("Total elapsed", formatHours(state.elapsedTravelHours))
    ];
}

function navigationStatus(state: SpatialRuntimeExpedition): HTMLElement[] {
    return [
        statusCell("Navigation", state.isLost ? "Lost" : "Oriented"),
        statusCell("Veer", `${state.veerSteps} (${state.veerDegrees}°)`),
        statusCell("Intended course", directionLabel(state.intendedDirection)),
        statusCell("Actual course", directionLabel(state.actualDirection))
    ];
}

function directionOptions(selected: number | null): string {
    return [0, 1, 2, 3, 4, 5]
        .map(value => `<option value="${value}" ${selected === value ? "selected" : ""}>${directionLabel(value)}</option>`)
        .join("");
}

