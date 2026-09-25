import { pauseInstruction, watchPhase } from "./expedition-workflow";
import { directionLabel, formatDistance, formatHours } from "../../runtime-view";
import type { ExpeditionDetail, Overworld, SpatialRuntimeExpedition } from "../../types";
import { prettyEnum, required, statusCell } from "../../ui/dom";

export function renderExpeditionStatus(root: HTMLElement, runtime: ExpeditionDetail): void {
    const state = spatialState(runtime);
    const status = required<HTMLElement>(root, "[data-status]");
    const watch = state.activeWatchNumber === null
        ? `Ready for watch ${state.completedWatches + 1}`
        : `Watch ${state.activeWatchNumber} · ${formatHours(state.activeWatchElapsedHours ?? 0)} / ${formatHours(state.activeWatchTotalHours ?? runtime.profile.watchHours)}`;
    const navigation = state.isLost
        ? `Lost · veer ${state.veerSteps > 0 ? "+" : ""}${state.veerSteps} (${state.veerDegrees}°)`
        : "Oriented";
    const cells = [
        statusCell("Day", String(state.currentDay)),
        statusCell("Watch", watch),
        statusCell("Current hex", `${state.currentHex.q}, ${state.currentHex.r}`),
        statusCell("Entry", directionLabel(state.entryDirection)),
        statusCell("Intended course", directionLabel(state.intendedDirection)),
        statusCell("Actual course", directionLabel(state.actualDirection)),
        statusCell("Navigation", navigation),
        statusCell("Distance traveled", formatDistance(state.distanceTraveled)),
        statusCell("Elapsed travel", formatHours(state.elapsedTravelHours)),
        statusCell(
            "State",
            watchPhase(runtime) === "paused"
                ? `Paused · ${runtime.pauseReason}`
                : watchPhase(runtime) === "active"
                    ? "Watch active"
                    : "Ready")
    ];
    if (runtime.profile.tracksIntraHexProgress) {
        cells.push(statusCell(
            "Intra-hex progress",
            state.exitRequirement
                ? `${formatDistance(state.hexProgress)} / ${formatDistance(state.exitRequirement)}`
                : formatDistance(state.hexProgress)));
    }
    if (state.activeWatchNumber !== null) {
        cells.push(statusCell(
            "Watch remaining",
            formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)));
    }
    status.replaceChildren(...cells);
}

export function renderExpeditionPause(root: HTMLElement, runtime: ExpeditionDetail): void {
    const panel = required<HTMLElement>(root, "[data-pause-panel]");
    const instruction = pauseInstruction(runtime);
    panel.hidden = instruction === null;
    if (instruction === null) {
        panel.replaceChildren();
        return;
    }
    panel.className = "hc-status-section";
    const heading = document.createElement("h2");
    heading.textContent = "Pending decision";
    const text = document.createElement("p");
    text.textContent = instruction;
    panel.replaceChildren(heading, text);
}

export function renderExpeditionHistory(root: HTMLElement, runtime: ExpeditionDetail): void {
    const host = required<HTMLElement>(root, "[data-history]");
    host.replaceChildren();
    const recent = [...runtime.history].reverse().slice(0, 30);
    if (recent.length === 0) {
        const empty = document.createElement("p");
        empty.className = "hc-sheet-empty";
        empty.textContent =
            "No watch entries yet. The first recorded result will appear here.";
        host.append(empty);
        return;
    }

    const table = document.createElement("table");
    table.className = "hc-watch-ledger";
    const head = document.createElement("thead");
    const headRow = document.createElement("tr");
    for (const label of ["Watch", "Elapsed", "Hex", "Record"]) {
        const cell = document.createElement("th");
        cell.scope = "col";
        cell.textContent = label;
        headRow.append(cell);
    }
    head.append(headRow);

    const body = document.createElement("tbody");
    for (const event of recent) {
        const row = document.createElement("tr");
        const watch = document.createElement("td");
        watch.textContent = String(event.watchNumber);
        const elapsed = document.createElement("td");
        elapsed.textContent = formatHours(event.expeditionElapsedHours);
        const hex = document.createElement("td");
        hex.textContent = event.hex ? `${event.hex.q}, ${event.hex.r}` : "—";
        const record = document.createElement("td");
        record.textContent = event.message;
        row.append(watch, elapsed, hex, record);
        body.append(row);
    }
    table.append(head, body);
    host.append(table);
}

export function renderPlayerKnowledgePreview(
    root: HTMLElement,
    runtime: ExpeditionDetail,
    world: Overworld): void {
    const host = required<HTMLElement>(root, "[data-player-preview]");
    host.replaceChildren();
    const presentationPolicy = runtime.presentation;
    if (!presentationPolicy) return;

    const summary = document.createElement("p");
    summary.textContent =
        `${presentationPolicy.name}: player grid ${presentationPolicy.playerGrid.toLowerCase()}, terrain ${prettyEnum(presentationPolicy.terrainMode)}, ${runtime.knownHexes.length} explored/known hex(es), ${runtime.knowledge.length} known subject(s).`;
    host.append(summary);

    if (runtime.knownHexes.length > 0) {
        const knownHexes = document.createElement("p");
        knownHexes.className = "hc-hint";
        knownHexes.textContent =
            `Known hexes: ${runtime.knownHexes.map(hex => `${hex.q},${hex.r}`).join(" · ")}`;
        host.append(knownHexes);
    }

    const list = document.createElement("ul");
    for (const entry of runtime.knowledge) {
        const subject = subjectLabel(world, entry.subjectId);
        const item = document.createElement("li");
        item.textContent =
            `${subject} · ${entry.subjectType} · ${entry.state}${entry.source ? ` · ${entry.source}` : ""}`;
        list.append(item);
    }
    if (list.childElementCount === 0) {
        const item = document.createElement("li");
        item.textContent =
            "No authored locations or map features are currently known to the players.";
        list.append(item);
    }
    host.append(list);

    const warning = document.createElement("p");
    warning.className = "hc-hint";
    warning.textContent =
        "This is a knowledge-state preview, not a player renderer. GM source maps are not treated as player-visible merely because this policy is open.";
    host.append(warning);
}

export function renderExpeditionSnapshots(
    root: HTMLElement,
    runtime: ExpeditionDetail,
    showMap: boolean): void {
    const host = required<HTMLElement>(root, "[data-snapshots]");
    host.replaceChildren();

    const procedure = document.createElement("p");
    procedure.textContent =
        `${runtime.profile.name} (${runtime.profile.key}) · ${runtime.profile.watchHours}h watch · ${prettyEnum(runtime.profile.travelResolution)} · ${prettyEnum(runtime.profile.actualDistanceResolution)} · encounters ${prettyEnum(runtime.profile.encounterCadence)} · navigation ${runtime.profile.usesNavigationChecks ? "enabled" : "disabled"} · veer ${runtime.profile.usesPersistentVeer ? "persistent" : "non-persistent"}.`;
    const note = document.createElement("p");
    note.className = "hc-hint";

    if (showMap && runtime.presentation) {
        const presentation = document.createElement("p");
        presentation.textContent =
            `${runtime.presentation.name} (${runtime.presentation.key}) · grid ${runtime.presentation.playerGrid.toLowerCase()} · terrain ${prettyEnum(runtime.presentation.terrainMode)} · automation ${prettyEnum(runtime.presentation.automationMode)}.`;
        note.textContent =
            "Both are stored snapshots for this expedition. Catalog changes do not reconstruct active expedition behavior.";
        host.append(procedure, presentation, note);
    } else {
        note.textContent =
            "The tracker uses the persisted crawl procedure snapshot. Map presentation remains owned by the full map workbench.";
        host.append(procedure, note);
    }
}

export function renderNonSpatialTracker(
    root: HTMLElement,
    runtime: ExpeditionDetail,
    navigate: (route: string, replace?: boolean) => void): () => void {
    const state = runtime.expedition;
    if (state.isSpatial) throw new Error("Expected non-spatial crawl session.");

    root.innerHTML = `
        <section class="hc-page">
            <header class="hc-page-header">
                <div>
                    <h1>${escapeHtml(runtime.name)}</h1>
                    <p>Non-spatial crawl session · ${escapeHtml(runtime.context.name)}</p>
                </div>
                <nav>
                    <button type="button" data-home>DM tools</button>
                </nav>
            </header>
            <div class="hc-columns">
                <section class="hc-panel hc-running-sheet">
                    <header class="hc-sheet-heading">
                        <div><span class="hc-sheet-kicker">Running sheet</span><h2>Procedure state</h2></div>
                        <span class="hc-sheet-note">Non-spatial</span>
                    </header>
                    <div class="hc-status-grid hc-sheet-status">
                        <div><strong>Day</strong><span>${state.currentDay}</span></div>
                        <div><strong>Watch</strong><span>${state.activeWatchNumber === null ? `Ready for watch ${state.completedWatches + 1}` : `Watch ${state.activeWatchNumber}`}</span></div>
                        <div><strong>Watch length</strong><span>${formatHours(state.activeWatchTotalHours ?? runtime.profile.watchHours)}</span></div>
                        <div><strong>Watch elapsed</strong><span>${formatHours(state.activeWatchElapsedHours ?? 0)}</span></div>
                        <div><strong>Watch remaining</strong><span>${formatHours(state.activeWatchRemainingHours ?? runtime.profile.watchHours)}</span></div>
                        <div><strong>Completed watches</strong><span>${state.completedWatches}</span></div>
                        <div><strong>Total elapsed</strong><span>${formatHours(state.elapsedTravelHours)}</span></div>
                        <div><strong>Context</strong><span>Non-spatial</span></div>
                    </div>
                    <p class="hc-hint">This running sheet intentionally omits map-only information. Use only the watch/time and encounter tools that apply to your procedure.</p>
                    <section class="hc-sheet-ledger" aria-labelledby="hc-nonspatial-watch-log">
                        <div class="hc-sheet-ledger-heading">
                            <h3 id="hc-nonspatial-watch-log">Watch log</h3>
                            <span>Watch · Elapsed · Hex · Record</span>
                        </div>
                        <div data-history></div>
                    </section>
                    <p class="hc-hint">${escapeHtml(runtime.profile.name)} · encounters ${escapeHtml(prettyEnum(runtime.profile.encounterCadence))}</p>
                </section>
                <section class="hc-panel">
                    <h2>Optional tools</h2>
                    <p class="hc-muted">Open only the bookkeeping surface you need. The running sheet remains usable without either helper.</p>
                    <div class="hc-button-row">
                        <button type="button" class="hc-primary-action" data-watch>Watch / time</button>
                        <button type="button" data-encounters>Encounter cadence</button>
                    </div>
                </section>
            </div>
        </section>`;

    renderExpeditionHistory(root, runtime);

    required<HTMLButtonElement>(root, "[data-home]")
        .addEventListener("click", () => navigate("/"));
    required<HTMLButtonElement>(root, "[data-watch]")
        .addEventListener("click", () => navigate(`/expeditions/${runtime.id}/travel`));
    required<HTMLButtonElement>(root, "[data-encounters]")
        .addEventListener("click", () => navigate(`/expeditions/${runtime.id}/encounters`));
    return () => {};
}

function spatialState(runtime: ExpeditionDetail): SpatialRuntimeExpedition {
    if (!runtime.expedition.isSpatial) {
        throw new Error("This operation requires a spatial crawl session.");
    }
    return runtime.expedition;
}

function subjectLabel(world: Overworld, id: string): string {
    return world.locations.find(item => item.id === id)?.name
        ?? world.features.find(item => item.id === id)?.name
        ?? id;
}

function escapeHtml(value: string): string {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}
