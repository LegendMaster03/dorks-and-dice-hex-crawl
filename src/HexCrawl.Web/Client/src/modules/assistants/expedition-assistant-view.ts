import type { HexCrawlApi } from "../../api";
import { manualEntryResolutionSources } from "../expeditions/expedition-input-policy";
import { assistantEncounterCheckDue } from "../expeditions/expedition-workflow";
import {
    canUseFocusedNonSpatialWatch, focusedIntervalHours,
    focusedIntervalProcedurePresentation, focusedIntervalUnavailableMessage
} from "../expeditions/focused-interval-policy";
import { formatHours } from "../../runtime-view";
import type {
    EncounterCadenceAssistantRequest, ExpeditionDetail,
    NonSpatialWatchAssistantRequest, ResolutionSource
} from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { input, numeric, option, optionalText, prettyEnum, required, select, sourceLabel, statusCell } from "../../ui/dom";
import { blockInitiativeHandoffHref } from "../../encounter-handoff";

export type ExpeditionAssistantMode = "travel" | "navigation" | "encounters";

/**
 * Nonspatial bookkeeping only. All spatial journeys, regardless of polygon
 * type, use the normal expedition's authoritative cell watch and helpers.
 */
export async function renderExpeditionAssistant(
    root: HTMLElement,
    api: HexCrawlApi,
    expeditionId: string,
    mode: ExpeditionAssistantMode,
    navigate: (route: string, replace?: boolean) => void
): Promise<() => void> {
    let runtime = await api.getExpedition(expeditionId);
    if (runtime.expedition.isSpatial) {
        navigate(`/expeditions/${runtime.id}`, true);
        return () => {};
    }
    let disposed = false;
    let pending = false;
    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div><h1 data-title></h1><p><span data-mode></span> · <span data-context></span></p></div>
                <nav>
                    <button type="button" data-home>Expedition home</button>
                    <button type="button" data-expedition>Open expedition</button>
                </nav>
            </header>
            <div class="hc-view-switcher" aria-label="Focused assistants">
                <button type="button" data-travel>Time / interval</button>
                <button type="button" data-encounters>Encounter cadence</button>
            </div>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-assistant-grid">
                <section class="hc-panel">
                    <h2 data-assistant-heading></h2>
                    <p class="hc-muted" data-assistant-description></p>
                    <div class="hc-status-grid" data-status></div>
                    <form class="hc-form hc-assistant-form" data-form></form>
                    <div class="hc-encounter-handoff" data-encounter-handoff hidden></div>
                </section>
                <section class="hc-panel">
                    <h2>Relevant history</h2><ol class="hc-history" data-history></ol>
                </section>
            </div>
        </section>`;
    const error = required<HTMLElement>(root, "[data-error]");
    const form = required<HTMLFormElement>(root, "[data-form]");
    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    required<HTMLButtonElement>(root, "[data-expedition]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}`));
    required<HTMLButtonElement>(root, "[data-travel]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/travel`));
    required<HTMLButtonElement>(root, "[data-encounters]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/encounters`));

    const renderHistory = (): void => {
        const items = runtime.history.filter(event => {
            if (event.kind === "ResolutionProvenanceRecorded")
                return event.message.includes(mode === "travel" ? "watch-assistant=" : "encounter-assistant=");
            return mode === "travel"
                ? ["WatchStarted", "WatchTimeAdvanced", "WatchCompleted", "DmOverrideApplied"].includes(event.kind)
                : ["EncounterCheckPerformed", "EncounterTriggered"].includes(event.kind);
        }).reverse().slice(0, 30);
        const list = required<HTMLOListElement>(root, "[data-history]");
        list.replaceChildren();
        if (!items.length) {
            const li = document.createElement("li");
            li.textContent = "No relevant history yet.";
            list.append(li);
        } else {
            for (const item of items) {
                const li = document.createElement("li");
                li.textContent = `#${item.sequence} · ${formatHours(item.expeditionElapsedHours)} · ${item.message}`;
                list.append(li);
            }
        }
    };
    const renderEncounterHandoff = (): void => {
        const host = required<HTMLElement>(root, "[data-encounter-handoff]");
        host.replaceChildren();
        host.hidden = true;
        if (mode !== "encounters") return;
        const event = [...runtime.history].reverse().find(value => value.kind === "EncounterTriggered");
        if (!event) return;
        const message = document.createElement("span");
        message.textContent = "Open this recorded encounter in Block Initiative. Handoff does not resolve it.";
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = "Open in Block Initiative";
        button.addEventListener("click", () => {
            if (pending) return;
            pending = true;
            button.disabled = true;
            clearUiError(error);
            void (async () => {
                try {
                    const path = window.location.pathname;
                    const returnPath = path === "/tools/hex-crawl" || path.startsWith("/tools/hex-crawl/")
                        ? `${path}${window.location.search}${window.location.hash}` : null;
                    const handoff = await api.createEncounterHandoff(runtime.id, {
                        expectedVersion: runtime.version,
                        handoffId: crypto.randomUUID(),
                        returnPath,
                        runtimeEncounterSequence: event.sequence,
                        journeyEventOccurrenceId: null
                    });
                    if (!disposed) window.location.assign(blockInitiativeHandoffHref(handoff));
                } catch (failure) {
                    if (!disposed) showUiError(error, failure);
                } finally {
                    pending = false;
                    if (!disposed) button.disabled = false;
                }
            })();
        });
        host.append(message, button);
        host.hidden = false;
    };
    const render = (): void => {
        if (disposed) return;
        if (runtime.expedition.isSpatial) {
            navigate(`/expeditions/${runtime.id}`, true);
            return;
        }
        const state = runtime.expedition;
        const presentation = focusedIntervalProcedurePresentation(runtime);
        required<HTMLElement>(root, "[data-title]").textContent = runtime.name;
        required<HTMLElement>(root, "[data-context]").textContent = `Setup: ${runtime.context.name}`;
        required<HTMLElement>(root, "[data-mode]").textContent = mode === "travel"
            ? "Time / interval assistant" : mode === "navigation" ? "Navigation" : "Encounter cadence assistant";
        required<HTMLElement>(root, "[data-assistant-heading]").textContent = mode === "travel"
            ? "Time / interval bookkeeping" : mode === "navigation" ? "Navigation unavailable" : "Encounter cadence";
        required<HTMLElement>(root, "[data-assistant-description]").textContent = mode === "travel"
            ? "Record configured intervals, partial time, watch completion and provenance without spatial movement."
            : mode === "navigation" ? "Navigation requires a spatial journey and is managed by the cell expedition."
                : "Record an encounter-cadence result without changing elapsed travel time.";
        const cells = [
            statusCell("Ruleset", presentation.procedureLabel),
            runtime.procedure.runtime
                ? statusCell("Execution", presentation.executionLabel)
                : statusCell("Focused interval", canUseFocusedNonSpatialWatch(runtime) ? "Supported" : "Unavailable"),
            statusCell("Current day", String(state.currentDay)),
            statusCell("Completed watches", String(state.completedWatches))
        ];
        if (mode === "travel") {
            const length = focusedIntervalHours(runtime);
            cells.push(statusCell("Interval", length === null ? focusedIntervalUnavailableMessage(runtime)
                : formatHours(state.activeWatchTotalHours ?? length)));
            cells.push(statusCell("Remaining", length === null ? "Not configured"
                : formatHours(state.activeWatchRemainingHours ?? length)));
        } else if (mode === "encounters" && runtime.procedure.runtime) {
            cells.push(statusCell("Cadence", prettyEnum(runtime.procedure.runtime.encounterCadence)));
            cells.push(statusCell("Check due", assistantEncounterCheckDue(runtime) ? "Yes" : "No"));
        }
        required<HTMLElement>(root, "[data-status]").replaceChildren(...cells);
        renderHistory();
        renderEncounterHandoff();

        if (mode === "navigation") {
            form.innerHTML = '<p class="hc-hint">A nonspatial journey has no direction or location. Open a spatial cell expedition to resolve navigation.</p>';
            return;
        }
        if (mode === "travel" && !canUseFocusedNonSpatialWatch(runtime)) {
            form.innerHTML = '<p class="hc-hint">Focused interval bookkeeping is not available for this ruleset. Review its saved procedure in the expedition.</p>';
            return;
        }
        if (mode === "encounters" && runtime.procedure.runtime === null) {
            form.innerHTML = '<p class="hc-hint">This ruleset has no executable encounter-cadence procedure here.</p>';
            return;
        }
        if (mode === "travel") {
            const length = focusedIntervalHours(runtime)!;
            const remaining = state.activeWatchRemainingHours ?? length;
            form.innerHTML = `
                <p class="hc-hint">Watch ${state.activeWatchNumber ?? state.completedWatches + 1} ·
                    remaining ${formatHours(remaining)}. Partial watches can resume.</p>
                <label>Elapsed time to record
                    <input name="elapsedHours" type="number" min="0.001" max="${remaining}" step="any" value="${remaining}"></label>
                ${provenanceFields()}
                <button class="hc-primary-action" type="submit">Record interval</button>`;
        } else {
            const helperNote = runtime.procedure.runtime?.resolutionHelpers?.encounter
                ? '<p class="hc-hint">Automatic encounter helpers also resolve timing. This focused assistant records only independently resolved manual, external, or DM-override outcomes.</p>'
                : "";
            form.innerHTML = `
                <p class="hc-hint">${assistantEncounterCheckDue(runtime) ? "A check is due." : "No check is currently due; a DM may still record an outcome."}</p>
                ${helperNote}
                <label>Outcome <select name="outcome" required>
                    <option value="">Select resolved outcome</option>
                    <option value="None">No encounter</option>
                    <option value="WanderingEncounter">Wandering encounter</option>
                    <option value="ManualCustom">Manual / custom encounter</option>
                </select></label>
                <label>Encounter note <input name="note"></label>
                ${provenanceFields(true)}
                <button class="hc-primary-action" type="submit">Record encounter check</button>`;
        }
        const source = select(form, "source");
        source.append(option("", "Select result source"));
        for (const choice of manualEntryResolutionSources) source.append(option(choice, sourceLabel(choice)));
        source.value = mode === "travel" ? "ProcedureDefault" : "";
        required<HTMLButtonElement>(form, 'button[type="submit"]').disabled = pending;
    };
    form.addEventListener("submit", event => {
        event.preventDefault();
        if (pending || runtime.expedition.isSpatial || mode === "navigation") return;
        if (mode === "travel" && !canUseFocusedNonSpatialWatch(runtime)) return;
        if (mode === "encounters" && runtime.procedure.runtime === null) return;
        pending = true;
        clearUiError(error);
        const button = required<HTMLButtonElement>(form, 'button[type="submit"]');
        button.disabled = true;
        void (async () => {
            try {
                const source = select(form, "source").value as ResolutionSource;
                const base = {
                    expectedVersion: runtime.version, resolutionSource: source,
                    resolutionNote: optionalText(input(form, "resolutionNote")),
                    note: optionalText(input(form, "note"))
                };
                const result = mode === "travel"
                    ? await api.recordWatchAssistant(runtime.id, {
                        ...base, elapsedHours: numeric(input(form, "elapsedHours"))
                    } satisfies NonSpatialWatchAssistantRequest)
                    : await api.recordEncounterAssistant(runtime.id, {
                        ...base, outcome: select(form, "outcome").value as EncounterCadenceAssistantRequest["outcome"]
                    } satisfies EncounterCadenceAssistantRequest);
                if (!disposed) {
                    runtime = result;
                    render();
                }
            } catch (failure) {
                if (!disposed) showUiError(error, failure);
            } finally {
                pending = false;
                if (!disposed) {
                    const current = form.querySelector<HTMLButtonElement>('button[type="submit"]');
                    if (current) current.disabled = false;
                }
            }
        })();
    });
    render();
    return () => { disposed = true; };
}

function provenanceFields(encounter = false): string {
    return `
        <label>Result source <select name="source" data-source required></select></label>
        <label>Source note <input name="resolutionNote" placeholder="optional"></label>
        ${encounter ? "" : '<label>Bookkeeping note <input name="note" placeholder="optional"></label>'}`;
}
