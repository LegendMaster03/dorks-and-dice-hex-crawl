import type { HexCrawlApi } from "../../api";
import { worldToHex } from "../../hex-math";
import { MapSurface } from "../../map-surface";
import { discoveredSubjectIds, directionLabel } from "../../runtime-view";
import { canonicalExpeditionRoute } from "../../tool-route";
import type { ExpeditionDetail, Overworld, SpatialRuntimeExpedition } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { renderExpeditionHistory, renderExpeditionPause, renderExpeditionSnapshots, renderExpeditionStatus, renderNonSpatialTracker, renderPlayerKnowledgePreview } from "./expedition-presentation";
import { ExpeditionPartySheetController } from "./expedition-party-sheet";
import { ExpeditionWatchController } from "./expedition-watch-controller";
import { required } from "../../ui/dom";

export type ExpeditionViewMode = "map" | "tracker";

export async function renderExpedition(
    root: HTMLElement,
    api: HexCrawlApi,
    expeditionId: string,
    mode: ExpeditionViewMode,
    navigate: (route: string, replace?: boolean) => void,
    routeWorldId?: string): Promise<() => void> {
    let runtime: ExpeditionDetail = await api.getExpedition(expeditionId);
    const showMap = mode === "map";
    if (showMap && routeWorldId) {
        const canonicalRoute = canonicalExpeditionRoute(routeWorldId, runtime);
        if (canonicalRoute) {
            navigate(canonicalRoute, true);
            return () => {};
        }
    }

    if (!runtime.expedition.isSpatial) {
        return renderNonSpatialTracker(root, runtime, navigate);
    }
    if (showMap && runtime.overworldId === null) {
        navigate(`/expeditions/${runtime.id}`, true);
        return () => {};
    }

    const world: Overworld | null = showMap ? await api.getOverworld(runtime.overworldId!) : null;
    let disposed = false;

    const modeLabel = showMap ? "Running sheet + map" : "Running sheet";
    const mapMarkup = showMap ? '<div class="hc-map-host" data-map></div>' : "";
    const ledgerMarkup = `
        <section class="hc-sheet-ledger hc-running-ledger-panel" aria-labelledby="hc-watch-log-title">
            <div class="hc-sheet-ledger-heading">
                <h3 id="hc-watch-log-title">Watch log</h3>
                <span>Day · Watch · Route · Progress · Navigation · Encounter</span>
            </div>
            <div data-history></div>
        </section>`;
    const currentSheetMarkup = `
        <section class="hc-running-sheet" aria-labelledby="hc-expedition-state-title">
            <header class="hc-sheet-heading">
                <div>
                    <span class="hc-sheet-kicker">Running sheet</span>
                    <h2 id="hc-expedition-state-title">Current crawl record</h2>
                </div>
                <span class="hc-sheet-note">Authoritative session state</span>
            </header>
            <div class="hc-status-grid hc-sheet-status" data-status></div>
            <section data-pause-panel hidden></section>
            <section class="hc-party-register" aria-labelledby="hc-party-register-title">
                <div class="hc-sheet-ledger-heading">
                    <h3 id="hc-party-register-title">Party & travel order</h3>
                    <span>Persistent expedition reference</span>
                </div>
                <div data-party-summary></div>
            </section>
            ${showMap ? "" : ledgerMarkup}
        </section>`;
    const runSurfaceMarkup = showMap
        ? `
            <section class="hc-map-panel hc-run-column" aria-label="Running sheet and map">
                <div class="hc-map-sheet-top">
                    ${mapMarkup}
                    ${currentSheetMarkup}
                </div>
                ${ledgerMarkup}
            </section>`
        : `
            <section class="hc-panel hc-runtime-panel hc-run-column" aria-label="Running sheet">
                ${currentSheetMarkup}
            </section>`;
    const mapOnlyTools = showMap
        ? `
                    <details open><summary>Current-area discovery</summary><div data-discovery></div></details>
                    <details><summary>Player knowledge preview</summary><div data-player-preview></div></details>`
        : "";

    root.innerHTML = `
        <section class="hc-page hc-workspace">
            <header class="hc-page-header">
                <div><h1 data-title></h1><p><span data-mode-label></span> · <span data-context></span></p></div>
                <nav><button type="button" data-home>DM tools</button><button type="button" data-edit>World authoring</button><button type="button" data-worlds>Overworlds</button></nav>
            </header>
            <div class="hc-view-switcher hc-run-toolbar" aria-label="Expedition views">
                <button type="button" data-view-tracker>Running sheet</button>
                <button type="button" data-view-map>Map + sheet</button>
                <span class="hc-run-toolbar-divider" aria-hidden="true"></span>
                <span class="hc-run-toolbar-label">Optional focused tools</span>
                <button type="button" data-view-travel>Travel / watch</button>
                <button type="button" data-view-navigation>Navigation</button>
                <button type="button" data-view-encounters>Encounters</button>
            </div>
            <p class="hc-focus-hint">Run the crawl from the sheet, or open a focused tool when you only want that part of the procedure. None of the focused tools are required.</p>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="${showMap ? "hc-workspace-grid" : "hc-tracker-grid"}">
                ${runSurfaceMarkup}
                <aside class="hc-sidebar" aria-label="Expedition controls">
                    <details class="hc-party-editor-panel"><summary>Party & travel order</summary><div data-party-editor></div></details>
                    <details open class="hc-sheet-controls"><summary data-watch-summary>Run watch</summary>
                        <p class="hc-hint">Fill only the parts that apply to the procedure you are using. Hidden sections are not required.</p>
                        <div class="hc-form hc-watch-requirements" data-requirements></div>
                        <form class="hc-form" data-advance>
                            <fieldset data-plan-fields data-focus-group="travel navigation">
                                <legend>Orders for this watch</legend>
                                <label>Intended direction <select name="direction">${directionOptions()}</select></label>
                                <label>Pace / travel mode <input name="pace" value="normal"></label>
                                <label>Travel duties / activities <input name="activities" placeholder="navigate, forage, map, scout"></label>
                                <label>Navigation aid/context <input name="navigationAid" value="none"></label>
                                <label data-suppress-nav-row><input name="suppressNav" type="checkbox"> Navigation aid suppresses the check</label>
                                <label data-reset-veer-row><input name="resetVeer" type="checkbox"> Navigation aid resets veer at a boundary</label>
                                <label><input name="continueAcross" type="checkbox"> Continue automatically across unchanged boundaries</label>
                                <label data-double-back-row><input name="doubleBack" type="checkbox"> Deliberate single-hex double-back</label>
                                <p class="hc-hint" data-direction-hint></p>
                            </fieldset>

                            <fieldset data-resolution-helper hidden>
                                <legend>Automatic procedure resolution</legend>
                                <p class="hc-hint">Roll the procedure-defined inputs for this watch. Generated results are recorded immediately and remain separate from advancing the watch until you apply them.</p>
                                <div data-helper-travel>
                                    <p class="hc-hint">Travel uses the expected distance below as the situational input; the procedure supplies the configured roll and multiplier.</p>
                                </div>
                                <div data-helper-navigation class="hc-form">
                                    <label>Navigation DC <input name="helperNavigationDc" type="number" step="1" placeholder="DM-confirmed DC"></label>
                                    <label>Navigation modifier <input name="helperNavigationModifier" type="number" step="1" value="0"></label>
                                    <label>Failure veer <input name="helperFailureVeer" type="number" step="1" placeholder="+1 or -1"></label>
                                    <p class="hc-hint">The DC, situational modifier, and failed-check veer stay explicit because they are not inferred by the procedure profile.</p>
                                </div>
                                <div data-helper-encounter>
                                    <p class="hc-hint">When an encounter check is due, the procedure can roll both the check and the encounter time automatically.</p>
                                </div>
                                <button type="button" data-resolution-helper-button>Roll procedure inputs</button>
                                <p class="hc-hint" data-resolution-helper-result aria-live="polite"></p>
                            </fieldset>

                            <fieldset data-travel-resolution data-focus-group="travel">
                                <legend>Travel / progress</legend>
                                <p class="hc-hint">Supply the effective movement result for this watch segment. A compatible party movement reference may prefill the base distance; adjust it for the actual pace, terrain, route, mounts, vehicles, or other conditions. The runtime does not infer those modifiers.</p>
                                <div data-fixed-distance><label>Effective distance <input name="effectiveDistance" type="number" min="0" step="any"></label></div>
                                <div data-variable-distance><label>Expected distance <input name="expectedDistance" type="number" min="0" step="any"></label><label>Actual resolved distance <input name="actualDistance" type="number" min="0" step="any"></label></div>
                                <div data-step-distance><label>Resolved hex steps <input name="hexSteps" type="number" min="0" step="1"></label></div>
                                <label>Travel result source <select name="travelSource"></select></label>
                                <label>Travel source note <input name="travelNote" placeholder="optional"></label>
                            </fieldset>

                            <fieldset data-navigation-resolution data-focus-group="navigation">
                                <legend>Navigation / veer</legend>
                                <label>Result <select name="navigationOutcome"><option value="">Select resolved result</option><option value="Succeeded">Succeeded</option><option value="Failed">Failed / lost</option></select></label>
                                <label data-veer-row>Resolved veer steps <input name="veerSteps" type="number" step="1" placeholder="+1 or -1"></label>
                                <label>Navigation result source <select name="navigationSource"></select></label>
                                <label>Navigation source note <input name="navigationNote" placeholder="optional"></label>
                            </fieldset>

                            <fieldset data-encounter-resolution data-focus-group="encounters">
                                <legend>Encounter</legend>
                                <label>Resolved outcome <select name="encounterOutcome"><option value="">Select resolved outcome</option><option value="None">No encounter</option><option value="WanderingEncounter">Wandering encounter</option><option value="KeyedLocationDiscovery">Keyed location discovery</option><option value="ManualCustom">Manual / custom interruption</option></select></label>
                                <label data-encounter-hour>Occurs at hour within watch <input name="encounterHour" type="number" min="0" step="any"></label>
                                <label data-encounter-location>Keyed location <select name="locationId"><option value="">—</option></select></label>
                                <label>Encounter note <input name="encounterNote" placeholder="optional"></label>
                                <label>Encounter result source <select name="encounterSource"></select></label>
                                <label>Encounter source note <input name="encounterSourceNote" placeholder="optional"></label>
                            </fieldset>

                            <fieldset data-boundary-resolution data-focus-group="navigation">
                                <legend>Lost / boundary decision</legend>
                                <label><input name="recognizedLost" type="checkbox"> The party recognizes that it is lost</label>
                                <label><input name="reorient" type="checkbox"> The party reorients</label>
                                <label>Decision source <select name="boundarySource"></select></label>
                                <label>Decision source note <input name="boundaryNote" placeholder="optional"></label>
                            </fieldset>

                            <details><summary>DM authority / override</summary><div class="hc-form">
                                <label>Override note <input name="dmOverrideNote" placeholder="record unusual ruling or authoritative override"></label>
                                <p class="hc-hint">Choose DM Override as the source for the affected resolution when replacing a procedural result. The history records the override rather than silently rewriting prior state.</p>
                            </div></details>
                            <button type="submit" class="hc-primary-action" data-advance-button>Run watch</button>
                        </form>
                    </details>

                    ${mapOnlyTools}
                    <details class="hc-optional-reference"><summary>${showMap ? "Procedure and presentation reference" : "Procedure reference"}</summary><div data-snapshots></div></details>
                </aside>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const mapHost = root.querySelector<HTMLElement>("[data-map]");
    const map = mapHost && world ? new MapSurface(mapHost, () => world) : null;

    const mutate = async (
        control: HTMLButtonElement | null,
        action: () => Promise<void>): Promise<void> => {
        clearUiError(error);
        if (control?.disabled) return;
        const idleText = control?.textContent ?? "";
        if (control) control.disabled = true;
        try {
            await action();
        } catch (value) {
            if (!disposed) showUiError(error, value);
        } finally {
            if (control && !disposed) {
                control.disabled = false;
                control.textContent = idleText;
            }
        }
    };

    const watchController = new ExpeditionWatchController(
        root,
        api,
        world,
        () => runtime,
        next => apply(next),
        action => mutate(null, action));
    let partyController: ExpeditionPartySheetController;

    const apply = (next: ExpeditionDetail): void => {
        runtime = next;
        required<HTMLElement>(root, "[data-title]").textContent = showMap && world ? `${world.name}: ${next.name}` : next.name;
        required<HTMLElement>(root, "[data-mode-label]").textContent = modeLabel;
        required<HTMLElement>(root, "[data-context]").textContent = `Crawl context: ${next.context.name}`;
        const nextState = spatialState(next);
        if (map) {
            map.renderer.expeditionHex = nextState.currentHex;
            map.renderer.discoveredSubjectIds = discoveredSubjectIds(next);
            map.requestRender();
        }
        renderExpeditionStatus(root, runtime);
        renderExpeditionPause(root, runtime);
        renderExpeditionHistory(root, runtime);
        if (showMap) {
            renderDiscovery();
            if (world) renderPlayerKnowledgePreview(root, runtime, world);
        }
        renderExpeditionSnapshots(root, runtime, showMap);
        partyController.sync(next);
        watchController.sync(next);
    };

    partyController = new ExpeditionPartySheetController(
        root,
        api,
        () => runtime,
        next => apply(next),
        mutate);

    const renderDiscovery = (): void => {
        if (!world) return;
        const host = required<HTMLElement>(root, "[data-discovery]");
        host.replaceChildren();
        const discovered = discoveredSubjectIds(runtime);
        const current = spatialState(runtime).currentHex;
        const subjects = [
            ...world.locations
                .filter(item => sameHex(worldToHex(world.grid, item.position), current))
                .map(item => ({ id: item.id, name: item.name, category: item.category, type: "Location" as const })),
            ...world.features
                .filter(item => item.kind === "Point" && item.position && sameHex(worldToHex(world.grid, item.position), current))
                .map(item => ({ id: item.id, name: item.name, category: item.category, type: "Feature" as const }))
        ];
        if (subjects.length === 0) {
            const hint = document.createElement("p");
            hint.className = "hc-hint";
            hint.textContent = "No authored locations or point features are in the current hex. Add them in the World map editor if this area should contain something.";
            host.append(hint);
            return;
        }
        for (const subject of subjects) {
            const row = document.createElement("div");
            row.className = "hc-discovery-row";
            const label = document.createElement("span");
            label.textContent = `${subject.name} · ${subject.category} · ${subject.type}`;
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = discovered.has(subject.id) ? "Known to players" : "Reveal / discover";
            button.disabled = discovered.has(subject.id);
            button.addEventListener("click", () => void mutate(button, async () =>
                apply(await api.discover(runtime.id, runtime.version, subject.id, subject.type))));
            row.append(label, button);
            host.append(row);
        }
    };

    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    const editButton = required<HTMLButtonElement>(root, "[data-edit]");
    editButton.hidden = runtime.overworldId === null;
    editButton.addEventListener("click", () => {
        if (runtime.overworldId) navigate(`/worlds/${runtime.overworldId}/edit`);
    });
    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    const trackerButton = required<HTMLButtonElement>(root, "[data-view-tracker]");
    trackerButton.classList.toggle("hc-active-view", !showMap);
    trackerButton.setAttribute("aria-current", showMap ? "false" : "page");
    trackerButton.addEventListener("click", () => navigate(`/expeditions/${runtime.id}`));
    const mapButton = required<HTMLButtonElement>(root, "[data-view-map]");
    mapButton.classList.toggle("hc-active-view", showMap);
    mapButton.setAttribute("aria-current", showMap ? "page" : "false");
    mapButton.hidden = runtime.overworldId === null;
    mapButton.addEventListener("click", () => {
        if (runtime.overworldId) navigate(`/worlds/${runtime.overworldId}/expeditions/${runtime.id}`);
    });
    required<HTMLButtonElement>(root, "[data-view-travel]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/travel`));
    required<HTMLButtonElement>(root, "[data-view-navigation]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/navigation`));
    required<HTMLButtonElement>(root, "[data-view-encounters]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/encounters`));

    apply(runtime);
    return () => {
        disposed = true;
        watchController.dispose();
        map?.dispose();
    };


}

function directionOptions(): string {
    return [0, 1, 2, 3, 4, 5]
        .map(value => `<option value="${value}">${directionLabel(value)}</option>`)
        .join("");
}

function spatialState(runtime: ExpeditionDetail): SpatialRuntimeExpedition {
    if (!runtime.expedition.isSpatial) {
        throw new Error("This operation requires a spatial crawl session.");
    }
    return runtime.expedition;
}

function sameHex(left: { q: number; r: number }, right: { q: number; r: number }): boolean {
    return left.q === right.q && left.r === right.r;
}

