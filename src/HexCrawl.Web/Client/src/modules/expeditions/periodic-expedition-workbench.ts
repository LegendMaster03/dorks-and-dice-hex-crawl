
import type { HexCrawlApi } from "../../api";
import { MapSurface } from "../../map-surface";
import { blockInitiativeHandoffHref } from "../../encounter-handoff";
import type {
    CellTraversalContext, CellProcedureResolutionHelperResult, CellWatchRequest, ExpeditionDetail, Overworld,
    WorldCellGeometry, WorldPoint, ToolHostContext
} from "../../types";
import { showUiError, clearUiError } from "../../ui-error";
import { currentPeriodicCellAdjacency } from "./current-cell-topology";
import { renderCurrentCellNavigator } from "./cell-navigator";
import { adjacencyForIntent } from "./spatial-adjacency";
import { guidancePreferenceButton } from "../../ui/guidance";
import { openWorkspaceDrawer, statAction, textElement, type WorkspaceDrawer } from "../../ui/workspace";
import { renderPhase15NextAction } from "./phase15-next-action";
import { nextCellExpeditionAction } from "./phase15-cell-action";
import { navigationResolutionDue } from "./expedition-workflow";
import type { ExpeditionJourneyState } from "../../journey-types";
import type { ExpeditionEffectState } from "../../effect-types";
import type { SurvivalResources } from "../../survival-types";
import { polygonContainsSubject, uniquelyAdjacentInterface } from "./phase20-cell-ui-policy";
import { discoveredSubjectIds } from "../../runtime-view";
import { encounterLocationId } from "./keyed-location-encounter";
import { mountPeriodicGameplayPanels } from "./periodic-gameplay-panels";
import { publishExpeditionRuntimeChanged } from "./expedition-runtime-events";
import { resolvePeriodicPace } from "./periodic-travel-pace";
import { movementCompositionLedger } from "./expedition-movement-composition-view";
import { encounterScheduleConfiguration } from "./encounter-schedule-presentation";

/**
 * Generalized view of the same expedition entity and authoritative watch runtime.
 * It reuses the map and current-cell navigator, not square/triangle/hex variants.
 * The backend remains responsible for every crossing, pause and travel budget.
 */
export async function renderPeriodicExpeditionWorkbench(
    root: HTMLElement, api: HexCrawlApi, initial: ExpeditionDetail,
    initialWorld: Overworld | null, navigate: (route: string, replace?: boolean) => void,
    toolContext: ToolHostContext | null = null
): Promise<() => void> {
    if (!initial.expedition.isSpatial || !initial.expedition.currentCell)
        throw new Error("A generalized expedition requires authoritative current-cell state.");
    let world = initialWorld;
    let runtime = initial;
    let topology = await api.getCellTopology(initial.id);
    let selectedExit = topology.selectedExitInterfaceIndex;
    let disposed = false;
    let busy = false;
    let refreshGeneration = 0;
    let generated: CellProcedureResolutionHelperResult | null = null;
    let displayedWatchNumber: number | null | undefined = undefined;
    let inspectedGeometry: WorldCellGeometry | null = null;
    let inspectedTilingRevision: number | null = null;
    let survivalAttention: SurvivalResources | null = null;
    let effectsAttention: ExpeditionEffectState | null = null;
    let journeyAttention: ExpeditionJourneyState | null = null;
    let attentionStatus: "loading" | "ready" | "error" = "loading";

    root.innerHTML = `
        <section class="hc-page hc-workspace hc-expedition hc-periodic-expedition hc-phase15-expedition">
            <header class="hc-page-header">
                <div><h1 data-title></h1><p data-context-label></p></div>
                <nav class="hc-button-row">
                    <button type="button" data-home>Hex Crawl home</button>
                    <button type="button" data-world>World authoring</button>
                    <button type="button" data-ruleset>Ruleset reference</button>
                    <button type="button" data-reload>Reload expedition</button>
                    <span data-guidance-controls></span>
                </nav>
            </header>
            <div data-error class="hc-error" role="alert" hidden></div>
            <div data-phase15-action></div>
            <section class="hc-phase15-runtime-summary">
                <div class="hc-stat-action-grid" data-phase15-stats></div>
            </section>
            <div class="hc-workspace-grid">
                <div class="hc-panel hc-map-panel" aria-label="Expedition map">
                    <h2 data-map-title>Expedition map</h2>
                    <div class="hc-map-frame"><div class="hc-map-host" data-map></div></div>
                    <p class="hc-hint" data-inspection>Click a uniquely adjacent cell to set travel intent; distant or ambiguous cells are inspection-only. Clicking never moves the party.</p>
                    <section class="hc-stack" data-cell-details aria-label="Selected cell and player knowledge"></section>
                </div>
                <aside class="hc-panel hc-sidebar hc-table-rail" aria-label="At the table">
                    <h2>At the table</h2>
                    <section class="hc-current-travel" aria-label="Current travel">
                        <h3>Current travel</h3><p data-status></p><p data-actual></p>
                        <div data-cell-navigator></div>
                        <p class="hc-hint" data-course>Choose a boundary to set intended travel.</p>
                        <button type="button" data-open-travel>Resolve travel / watch</button>
                    </section>
                    <details><summary>Party and movement summary</summary><div data-party></div><div data-movement></div></details>
                    <details data-gameplay-disclosure>
                        <summary>Party and gameplay</summary>
                        <section class="hc-stack hc-gameplay-controls" data-gameplay-panels
                            aria-label="Shared party and gameplay editors">
                            <p class="hc-hint">The existing party, environment, journey, effects and survival editors remain available.</p>
                        </section>
                    </details>
                    <details data-history-disclosure><summary>Journey events</summary><ol data-history></ol></details>
                </aside>
            </div>
            <section class="hc-gm-tools">
                <details><summary>GM Tools</summary>
                    <div class="hc-button-row">
                        <button type="button" data-gm-travel>Travel and navigation</button>
                        <button type="button" data-gm-party>Party and travel order</button>
                        <button type="button" data-gm-environment>Environment</button>
                        <button type="button" data-gm-survival>Survival and resources</button>
                        <button type="button" data-gm-journey>Journey and challenges</button>
                        <button type="button" data-gm-history>Expedition history</button>
                        <button type="button" data-gm-reposition>Teleport party to selected cell</button>
                    </div>
                </details>
            </section>
            <div data-travel-staging hidden>
                <section data-travel-content class="hc-focused-watch-workspace">
                    <h2>Travel and watch resolution</h2>
                    <p class="hc-hint">Choosing a course does not move the party. The server resolves every boundary and actual movement after the table confirms inputs.</p>
                    <form class="hc-form" data-travel-form>
                            <div class="hc-inline"><label>Heading X <input name="headingX" type="number" step="any" required></label>
                                <label>Heading Y <input name="headingY" type="number" step="any" required></label></div>
                            <label data-pace-row>Pace <select name="pace"></select></label>
                            <p class="hc-hint" data-fixed-pace aria-live="polite"></p>
                            <div data-distance-fields class="hc-inline">
                                <label>Expected distance <input name="expected" type="number" min="0" step="any"></label>
                                <label>Resolved distance <input name="actual" type="number" min="0" step="any"></label>
                            </div>
                            <label data-cell-step-field>Resolved cell steps <input name="steps" type="number" min="0" step="1" value="1"></label>
                            <p data-distance-unit class="hc-hint"></p>
                            <label>Navigation resolution
                                <select name="navigation">
                                    <option value="">No resolution supplied</option>
                                    <option value="Succeeded">Succeeded (DM adjudicated)</option>
                                    <option value="Failed">Failed (DM adjudicated)</option>
                                </select>
                            </label>
                            <label data-veer-row hidden>Failure veer (degrees) <input name="veer" type="number" step="any" value="0"></label>
                            <details data-helper-section class="hc-optional-reference" hidden>
                                <summary>Automatic ruleset resolution</summary>
                                <p class="hc-hint">The server rolls configured ruleset helpers and records their provenance. Adjusting inputs afterward discards this generation; generate again before advancing.</p>
                                <div class="hc-inline">
                                    <label>Navigation DC <input name="helperDc" type="number" step="1" placeholder="DM difficulty"></label>
                                    <label>Navigation modifier <input name="helperModifier" type="number" step="1" value="0"></label>
                                    <label>Veer on failed check (degrees) <input name="helperVeer" type="number" step="any" placeholder="Required if navigation fails"></label>
                                </div>
                                <button type="button" data-generate-helper>Roll available ruleset inputs</button>
                                <p class="hc-hint" data-helper-result role="status" aria-live="polite"></p>
                            </details>
                            <details class="hc-optional-reference"><summary>Encounter and continued travel</summary>
                                <label>Encounter resolution
                                    <select name="encounter"><option value="">No resolution supplied</option>
                                        <option value="None">No encounter (DM adjudicated)</option>
                                        <option value="WanderingEncounter">Wandering encounter</option>
                                        <option value="KeyedLocationDiscovery">Keyed location</option>
                                        <option value="ManualCustom">Custom encounter</option>
                                    </select></label>
                                <p data-encounter-cadence class="hc-hint" aria-live="polite"></p>
                                <label data-keyed-location-row hidden>Keyed world location
                                    <select name="keyedLocation" aria-label="Saved keyed world location"></select>
                                    <span class="hc-hint">Choose any saved location in this world, including a destination cell. The server verifies that the party reaches the selected location's cell before its encounter triggers. Add new locations in the world editor first.</span>
                                </label>
                                <label>Encounter time within watch (hours) <input name="encounterHours" type="number" min="0" step="any" value="0"></label>
                                <label>Encounter note <input name="encounterNote"></label>
                                <label><input name="continueAcross" type="checkbox"> Continue across cell boundaries</label>
                                <label><input name="doubleBack" type="checkbox"> Deliberately double back through the entry boundary</label>
                                <label><input name="recognizedLost" type="checkbox"> Party recognized its lost course</label>
                                <label><input name="reorient" type="checkbox"> Reorient after recognizing the error</label>
                            </details>
                            <button type="submit" class="hc-primary-action" data-advance>Resolve and advance watch</button>
                        </form>
                        <div data-course-decision hidden><p>Multiple interfaces meet the resolved course. Choose a valid atomic exit and confirm the heading.</p>
                            <button type="button" data-confirm-course>Resolve selected exit</button></div>
                        <div data-encounter-decision hidden><p data-encounter-description></p>
                            <label>Resolution note <input data-encounter-result></label>
                            <div class="hc-button-row">
                                <button type="button" data-handoff-encounter>Open in Block Initiative</button>
                                <button type="button" data-resolve-encounter>Mark encounter resolved</button>
                            </div></div>
                </section>
            </div>
        </section>`;
    const required = <T extends Element>(selector: string): T => {
        const element = root.querySelector<T>(selector);
        if (!element) throw new Error(`Missing generalized expedition control ${selector}.`);
        return element;
    };
    const text = (selector: string, value: string): void => {
        required<HTMLElement>(selector).textContent = value;
    };
    const error = required<HTMLElement>("[data-error]");
    const form = required<HTMLFormElement>("[data-travel-form]");
    const travelContent = required<HTMLElement>("[data-travel-content]");
    const travelDock = required<HTMLElement>("[data-travel-staging]");
    let travelDrawer: WorkspaceDrawer | null = null;
    let historyDrawer: WorkspaceDrawer | null = null;
    const captureReturnFocus = (): (() => HTMLElement | null) => {
        const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        const statLabel = opener?.querySelector(".hc-stat-action-label")?.textContent;
        const action = opener?.hasAttribute("data-current-action-button");
        const interfaceId = opener?.dataset.adjacencyInterfaceId;
        return () => {
            if (opener?.isConnected) return opener;
            if (action) return root.querySelector<HTMLElement>("[data-current-action-button]");
            if (statLabel) return [...root.querySelectorAll<HTMLElement>(".hc-stat-action")]
                .find(button => button.querySelector(".hc-stat-action-label")?.textContent === statLabel) ?? null;
            if (interfaceId) return [...root.querySelectorAll<HTMLElement>("[data-adjacency-interface-id]")]
                .find(button => button.dataset.adjacencyInterfaceId === interfaceId) ?? null;
            return null;
        };
    };
    const openTravelDrawer = (focus?: "encounter" | "course" | "lost" | "navigation"): void => {
        if (!travelDrawer) {
            travelDrawer = openWorkspaceDrawer(root, "Travel / watch",
                body => { body.append(travelContent); },
                captureReturnFocus(),
                () => {
                    travelDock.append(travelContent);
                    travelDrawer = null;
                });
        }
        const target = focus === "encounter" ? required<HTMLElement>("[data-resolve-encounter]")
            : focus === "course" ? required<HTMLElement>("[data-confirm-course]")
                : focus === "lost" ? input("recognizedLost")
                    : focus === "navigation" ? choice("navigation")
                        : required<HTMLElement>("[data-advance]");
        const disclosure = target.closest("details");
        if (disclosure) disclosure.open = true;
        target.focus();
    };
    required<HTMLButtonElement>("[data-open-travel]").addEventListener("click",
        () => openTravelDrawer());
    required<HTMLButtonElement>("[data-gm-travel]").addEventListener("click",
        () => openTravelDrawer());
    const openGameplayPanel = (selector: string, actionLabel?: string): void => {
        const disclosure = required<HTMLDetailsElement>("[data-gameplay-disclosure]");
        disclosure.open = true;
        const panel = required<HTMLDetailsElement>(selector);
        panel.open = true;
        panel.scrollIntoView({ block: "nearest" });
        const controls = Array.from(panel.querySelectorAll<HTMLButtonElement>("button"));
        const target = actionLabel
            ? controls.find(button => button.textContent?.trim() === actionLabel) : null;
        (target ?? panel.querySelector<HTMLElement>("summary"))?.focus();
    };
    required<HTMLButtonElement>("[data-gm-party]").addEventListener("click", () =>
        openGameplayPanel("[data-periodic-party]"));
    required<HTMLButtonElement>("[data-gm-environment]").addEventListener("click", () =>
        openGameplayPanel(".hc-environment-panel"));
    required<HTMLButtonElement>("[data-gm-survival]").addEventListener("click", () =>
        openGameplayPanel("[data-survival-resources-panel]"));
    required<HTMLButtonElement>("[data-gm-journey]").addEventListener("click", () =>
        openGameplayPanel("[data-journey-panel]"));
    const openHistory = (): void => {
        const disclosure = required<HTMLDetailsElement>("[data-history-disclosure]");
        const history = required<HTMLOListElement>("[data-history]");
        if (!historyDrawer) historyDrawer = openWorkspaceDrawer(root, "Expedition history",
            body => { body.append(history); }, captureReturnFocus(), () => {
                disclosure.append(history);
                historyDrawer = null;
            });
        historyDrawer.element.querySelector<HTMLElement>("button")?.focus();
    };
    required<HTMLButtonElement>("[data-gm-history]").addEventListener("click", openHistory);
    const canReposition = (): boolean => {
        const state = currentState();
        return !busy && !!inspectedGeometry
            && state.activeWatchNumber === null && state.pendingEncounter === null;
    };
    const openRepositionDrawer = (): void => {
        if (!canReposition() || !inspectedGeometry) return;
        const target = inspectedGeometry;
        openWorkspaceDrawer(root, "Teleport party", (body, close) => {
            body.append(textElement("p",
                "This moves the party to the selected authoritative cell without spending travel time. It is not a movement or discovery resolution."));
            body.append(textElement("p",
                `Target: ${target.id.address.motifCellId} (${target.id.address.translation.u}, ${target.id.address.translation.v}).`));
            const form = document.createElement("form");
            form.className = "hc-form";
            const note = document.createElement("input");
            note.placeholder = "Optional reason or correction note";
            note.maxLength = 2000;
            const confirm = document.createElement("button");
            confirm.type = "submit";
            confirm.className = "hc-primary-action";
            confirm.textContent = "Confirm teleport";
            form.append(note, confirm);
            form.addEventListener("submit", event => {
                event.preventDefault();
                void progress(async () => {
                    const result = await api.repositionCellExpedition(runtime.id, {
                        expectedVersion: runtime.version,
                        targetCell: target.id,
                        position: target.center,
                        note: note.value.trim() || undefined
                    });
                    close();
                    inspectedGeometry = null;
                    return result;
                });
            });
            body.append(form);
        });
    };
    required<HTMLButtonElement>("[data-gm-reposition]").addEventListener("click", openRepositionDrawer);
    required<HTMLButtonElement>("[data-home]").addEventListener("click", () => navigate("/"));
    required<HTMLButtonElement>("[data-ruleset]").addEventListener("click", () =>
        navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`));
    const input = (name: string): HTMLInputElement => {
        const element = form.querySelector<HTMLInputElement>(`input[name="${name}"]`);
        if (!element) throw new Error(`Missing travel field ${name}.`);
        return element;
    };
    const choice = (name: string): HTMLSelectElement => {
        const element = form.querySelector<HTMLSelectElement>(`select[name="${name}"]`);
        if (!element) throw new Error(`Missing travel selection ${name}.`);
        return element;
    };
    const keyedChoices = choice("keyedLocation");
    const updateKeyedLocations = (): void => {
        // A watch can cross multiple cells before its scheduled encounter.
        // Present all saved world locations; the server validates membership
        // against the authoritative cell at the actual encounter trigger.
        const selected = keyedChoices.value;
        keyedChoices.replaceChildren();
        const placeholder = document.createElement("option");
        placeholder.value = "";
        placeholder.textContent = (world?.locations.length ?? 0)
            ? "Choose a saved world location"
            : "No saved locations — add one in the world editor";
        keyedChoices.append(placeholder);
        for (const location of world?.locations ?? []) {
            const option = document.createElement("option");
            option.value = location.id;
            option.textContent = `${location.name} · ${location.category}`;
            keyedChoices.append(option);
        }
        if (world?.locations.some(location => location.id === selected))
            keyedChoices.value = selected;
    };
    updateKeyedLocations();
    const showKeyedLocation = (): void => {
        required<HTMLElement>("[data-keyed-location-row]").hidden =
            !world || choice("encounter").value !== "KeyedLocationDiscovery";
    };
    // Mapless sessions have no authored locations and must not offer a
    // discovery result that the cell application service cannot accept.
    const keyedEncounterOption = choice("encounter")
        .querySelector<HTMLOptionElement>('option[value="KeyedLocationDiscovery"]');
    if (keyedEncounterOption && !world) {
        keyedEncounterOption.disabled = true;
        keyedEncounterOption.hidden = true;
    }
    choice("encounter").addEventListener("change", showKeyedLocation);
    showKeyedLocation();

    const map = new MapSurface(required<HTMLElement>("[data-map]"), () => world, undefined, api);
    // Mapless geometry has no world origin to center on automatically.
    if (!world) map.centerOn(topology.position);
    required<HTMLButtonElement>("[data-world]").hidden = world === null;
    required<HTMLElement>("[data-guidance-controls]").append(guidancePreferenceButton(root));
    required<HTMLButtonElement>("[data-world]").addEventListener("click", () => {
        if (world) navigate(`/worlds/${world.id}`);
    });
    required<HTMLButtonElement>("[data-reload]").addEventListener("click", () => void refresh());
    const setInspectedGeometry = (geometry: WorldCellGeometry | null): void => {
        inspectedGeometry = geometry;
        inspectedTilingRevision = geometry ? world?.tiling?.revision ?? null : null;
        map.renderer.selectedWorldCell = geometry;
        const adjacent = geometry ? uniquelyAdjacentInterface(geometry.id, topology.interfaces) : null;
        if (adjacent !== null && !busy) {
            if (generated) invalidateGenerated();
            selectHeading(adjacent);
            render();
        }
        text("[data-inspection]", geometry
            ? adjacent !== null
                ? "Adjacent cell selected as travel intent. The party has not moved."
                : `Selected cell at ${geometry.center.x.toFixed(2)}, ${geometry.center.y.toFixed(2)}. Inspection does not change travel intent.`
            : "Select a cell on the map to inspect its player knowledge and GM actions.");
        renderSelectedCellDetails();
        map.requestRender();
    };
    map.setCellSelectionHandler(cell => {
        setInspectedGeometry(cell);
    });

    const readNumber = (name: string, integer = false): number => {
        const field = input(name);
        const value = Number(field.value);
        if (!field.value.trim() || !Number.isFinite(value) || value < 0 && name !== "headingX" && name !== "headingY"
            || integer && !Number.isSafeInteger(value)) {
            throw new Error(`Enter a valid finite ${name} value.`);
        }
        return value;
    };
    const selectHeading = (interfaceIndex: number | null): void => {
        selectedExit = interfaceIndex;
        if (interfaceIndex === null) {
            // Clearing a proposed boundary must not silently submit the old
            // manually chosen heading as a new travel intent.
            input("headingX").value = "";
            input("headingY").value = "";
            return;
        }
        const edge = topology.interfaces.find(item => item.interfaceIndex === interfaceIndex);
        if (!edge) return;
        const target = {
            x: (edge.start.x + edge.end.x) / 2,
            y: (edge.start.y + edge.end.y) / 2
        };
        const direction = { x: target.x - topology.position.x, y: target.y - topology.position.y };
        const fallback = { x: target.x - topology.center.x, y: target.y - topology.center.y };
        const heading = Math.hypot(direction.x, direction.y) > 1e-12 ? direction : fallback;
        input("headingX").value = String(heading.x);
        input("headingY").value = String(heading.y);
    };
    const selectedHeading = (): WorldPoint => {
        const heading = { x: readNumber("headingX"), y: readNumber("headingY") };
        if (!(Math.hypot(heading.x, heading.y) > 0)) throw new Error("Choose a nonzero travel heading.");
        return heading;
    };
    const currentState = () => {
        if (!runtime.expedition.isSpatial || !runtime.expedition.currentCell)
            throw new Error("The expedition no longer has generalized cell state.");
        return runtime.expedition;
    };
    const gameplay = mountPeriodicGameplayPanels(
        required<HTMLElement>("[data-gameplay-panels]"), api, initial.id,
        toolContext,
        () => runtime,
        next => {
            if (disposed) return;
            runtime = next;
            generated = null; // Any other aggregate mutation supersedes this version-bound grant.
            publishExpeditionRuntimeChanged(root, runtime);
        },
        async (control, action) => {
            if (busy || disposed) return;
            busy = true;
            refreshGeneration++;
            if (control) control.disabled = true;
            clearUiError(error);
            try {
                await action();
                if (!disposed) await refresh(true);
            } catch (failure) {
                if (!disposed) {
                    showUiError(error, failure);
                    if (failure instanceof Error && /version|conflict|stale/i.test(failure.message))
                        await refresh(true);
                }
            } finally {
                busy = false;
                if (!disposed) {
                    if (control) control.disabled = false;
                    render();
                }
            }
        },
        failure => {
            if (!disposed) showUiError(error, failure);
        },
        (journey, survival, effects, failed) => {
            if (disposed) return;
            journeyAttention = journey;
            survivalAttention = survival;
            effectsAttention = effects;
            attentionStatus = failed ? "error" : "ready";
            render();
        }
    );

    const progress = async (action: () => Promise<ExpeditionDetail>): Promise<void> => {
        if (busy || disposed) return;
        busy = true;
        refreshGeneration++;
        clearUiError(error);
        form.querySelectorAll<HTMLButtonElement>("button").forEach(button => button.disabled = true);
        try {
            const updated = await action();
            if (disposed) return;
            runtime = updated;
            generated = null;
            attentionStatus = "loading";
            survivalAttention = null;
            effectsAttention = null;
            journeyAttention = null;
            topology = await api.getCellTopology(runtime.id);
            selectedExit = topology.selectedExitInterfaceIndex;
            if (!world) map.centerOn(topology.position);
            render();
            gameplay.sync();
            publishExpeditionRuntimeChanged(root, runtime);
        } catch (failure) {
            if (disposed) return;
            showUiError(error, failure);
            if (failure instanceof Error && /version|conflict|stale/i.test(failure.message)) {
                await refresh(true);
            }
        } finally {
            busy = false;
            // Reapply the authoritative pause and encounter gates instead of
            // unconditionally enabling an advance button after completion.
            if (!disposed) render();
        }
    };
    const refresh = async (duringMutation = false): Promise<void> => {
        if (disposed || busy && !duringMutation) return;
        const generation = ++refreshGeneration;
        try {
            const [next, nextTopology, nextWorld] = await Promise.all([
                api.getExpedition(runtime.id), api.getCellTopology(runtime.id),
                world ? api.getOverworld(world.id) : Promise.resolve(null)
            ]);
            if (disposed || generation !== refreshGeneration) return;
            runtime = next;
            generated = null;
            attentionStatus = "loading";
            survivalAttention = null;
            effectsAttention = null;
            journeyAttention = null;
            world = nextWorld;
            required<HTMLButtonElement>("[data-world]").hidden = world === null;
            topology = nextTopology;
            selectedExit = topology.selectedExitInterfaceIndex;
            if (!world) map.centerOn(topology.position);
            render();
            gameplay.sync();
            publishExpeditionRuntimeChanged(root, runtime);
        } catch (failure) {
            if (!disposed && generation === refreshGeneration) showUiError(error, failure);
        }
    };

    form.addEventListener("submit", event => {
        event.preventDefault();
        void progress(async () => {
            const state = currentState();
            const mode = resolvePeriodicPace(
                runtime.movementComposition.policy.travelModeKeys,
                choice("pace").value, state.activePaceKey).paceKey;
            const resolution = runtime.procedure.runtime?.travelResolution;
            if (!resolution) throw new Error("This expedition has no executable movement rules.");
            const unit = runtime.movementComposition.suggestedExpectedDistance?.unit
                ?? runtime.movementComposition.effectiveDistanceUnit
                ?? runtime.context.tiling?.physicalDistancePerWorldUnit?.unit;
            const travel: CellWatchRequest["inputs"]["travel"] = {
                provenance: { source: "DmOverride", note: "DM-confirmed travel resolution" }
            };
            if (resolution === "CellSteps") {
                travel.cellSteps = readNumber("steps", true);
            } else {
                if (!unit) throw new Error(
                    "This movement mode needs a valid distance unit and a calibrated world.");
                travel.expectedDistance = { value: readNumber("expected"), unit };
                travel.actualDistance = { value: readNumber("actual"), unit };
            }
            const navigationChoice = choice("navigation").value;
            const nav = navigationChoice
                ? {
                    outcome: navigationChoice as "Succeeded" | "Failed",
                    veerDegreesOnFailure: navigationChoice === "Failed"
                        ? Number(input("veer").value) : null,
                    provenance: { source: "DmOverride" as const, note: "DM-resolved navigation" }
                }
                : undefined;
            if (nav?.outcome === "Failed" && !Number.isFinite(nav.veerDegreesOnFailure))
                throw new Error("Enter a finite navigation veer angle.");
            const encounterChoice = choice("encounter").value;
            const encounter = encounterChoice
                ? {
                    kind: encounterChoice as "None" | "WanderingEncounter" | "KeyedLocationDiscovery" | "ManualCustom",
                    occursAt: encounterChoice === "None"
                        ? null : timespanFromHours(readNumber("encounterHours")),
                    locationId: encounterLocationId(
                        encounterChoice, keyedChoices.value, world?.locations ?? []),
                    note: input("encounterNote").value.trim() || null,
                    provenance: { source: "DmOverride" as const, note: "DM-resolved encounter" }
                }
                : undefined;
            const boundaryDecision = state.activeWatchPendingDecision === "LostRecognitionRequired"
                ? {
                    recognizedLost: input("recognizedLost").checked,
                    reorient: input("reorient").checked,
                    provenance: { source: "DmOverride" as const, note: "DM-recognized lost-course decision" }
                }
                : undefined;
            return await api.advanceCellWatch(runtime.id, {
                expectedVersion: runtime.version,
                plan: {
                    intendedHeading: selectedHeading(),
                    deliberateDoubleBack: input("doubleBack").checked,
                    continueAcrossBoundaries: input("continueAcross").checked,
                    mode: {
                        paceKey: state.activeWatchNumber === null ? mode : state.activePaceKey ?? mode,
                        activityAssignments: state.activeWatchNumber === null
                            ? runtime.party.activityAssignments : state.activeActivityAssignments
                    },
                    navigationAid: {
                        key: state.activeWatchNumber === null ? "none" : state.activeNavigationAidKey ?? "none",
                        suppressesNavigationCheck: state.activeSuppressesNavigationCheck,
                        resetsVeerAtBoundary: state.activeResetsVeerAtBoundary
                    }
                },
                inputs: {
                    travel: generated?.travel ?? travel,
                    navigation: generated?.navigation ?? nav,
                    encounter: generated?.encounter ?? encounter,
                    boundaryDecision,
                    dmOverrideNote: "DM confirmed generalized watch inputs"
                },
                generatedProcedureResolutionId: generated?.generatedResolutionId ?? undefined
            });
        });
    });
    // Any direct change to travel intent or resolution values invalidates the
    // displayed automatic result. Programmatic helper field fill does not fire
    // input/change events, so a generation remains usable until a real edit.
    form.addEventListener("input", () => {
        if (generated) invalidateGenerated();
    });
    form.addEventListener("change", () => {
        if (generated) invalidateGenerated();
    });
    const invalidateGenerated = (): void => {
        generated = null;
        text("[data-helper-result]", "Travel intent or DM input changed. Generate again to use automatic results.");
    };
    required<HTMLButtonElement>("[data-generate-helper]").addEventListener("click", () => {
        void (async () => {
            if (busy || disposed) return;
            busy = true;
            refreshGeneration++;
            clearUiError(error);
            const button = required<HTMLButtonElement>("[data-generate-helper]");
            button.disabled = true;
            generated = null;
            try {
                const unit = runtime.movementComposition.suggestedExpectedDistance?.unit
                    ?? runtime.movementComposition.effectiveDistanceUnit
                    ?? runtime.context.tiling?.physicalDistancePerWorldUnit?.unit;
                const dc = input("helperDc").value.trim();
                const veer = input("helperVeer").value.trim();
                const result = await api.resolveCellProcedureInputs(runtime.id, {
                    expectedVersion: runtime.version,
                    expectedDistance: unit && input("expected").value.trim()
                        ? { value: readNumber("expected"), unit } : undefined,
                    suppressesNavigationCheck: currentState().activeSuppressesNavigationCheck,
                    deliberateDoubleBack: input("doubleBack").checked,
                    navigationDifficultyClass: dc ? readNumber("helperDc", true) : undefined,
                    navigationModifier: Number(input("helperModifier").value),
                    failureVeerDegrees: veer ? Number(veer) : undefined,
                    keyedLocationId: keyedChoices.value || undefined
                });
                if (disposed) return;
                if (result.generatedResolutionId) {
                    const next = await api.getExpedition(runtime.id);
                    if (next.version !== result.expeditionVersion)
                        throw new Error("The expedition changed while automatic resolution was being generated.");
                    runtime = next;
                    generated = result;
                    if (result.travel?.expectedDistance && result.travel.actualDistance) {
                        input("expected").value = String(result.travel.expectedDistance.value);
                        input("actual").value = String(result.travel.actualDistance.value);
                    }
                    if (result.navigation) {
                        choice("navigation").value = result.navigation.outcome === "NotRequired"
                            ? "" : result.navigation.outcome;
                        input("veer").value = String(result.navigation.veerDegreesOnFailure ?? 0);
                        required<HTMLElement>("[data-veer-row]").hidden =
                            result.navigation.outcome !== "Failed";
                    }
                    if (result.encounter) {
                        choice("encounter").value = result.encounter.kind;
                        if (result.encounter.locationId) keyedChoices.value = result.encounter.locationId;
                        input("encounterNote").value = result.encounter.note ?? "";
                        showKeyedLocation();
                    }
                    gameplay.sync();
                    publishExpeditionRuntimeChanged(root, runtime);
                }
                const dice = result.rolls.map(roll =>
                    `${roll.purpose}: ${roll.formula} = ${roll.total}`).join("; ");
                text("[data-helper-result]", [dice, ...result.notes,
                    result.generatedResolutionId ? "Verified automatic inputs ready to submit." :
                        "No applicable automatic input was generated."].filter(Boolean).join(" · "));
            } catch (failure) {
                if (!disposed) {
                    showUiError(error, failure);
                    await refresh(true);
                }
            } finally {
                busy = false;
                if (!disposed) {
                    render();
                    button.disabled = !runtime.procedure.runtime?.resolutionHelpers;
                }
            }
        })();
    });
    choice("navigation").addEventListener("change", () => {
        required<HTMLElement>("[data-veer-row]").hidden = choice("navigation").value !== "Failed";
    });
    required<HTMLButtonElement>("[data-confirm-course]").addEventListener("click", () => void progress(
        () => {
            if (selectedExit === null) throw new Error("Choose a specific exit interface first.");
            return api.resolveCellCourse(runtime.id, {
                expectedVersion: runtime.version,
                decision: {
                    intendedHeading: selectedHeading(),
                    selectedExitInterfaceIndex: selectedExit,
                    provenance: { source: "DmOverride", note: "DM-selected atomic exit" }
                }
            });
        }));
    required<HTMLButtonElement>("[data-handoff-encounter]").addEventListener("click", () => {
        const pending = currentState().pendingEncounter;
        if (!pending || busy) return;
        const handoffControl = required<HTMLButtonElement>("[data-handoff-encounter]");
        void (async () => {
            busy = true;
            handoffControl.disabled = true;
            clearUiError(error);
            try {
                const pathname = window.location.pathname;
                const returnPath = pathname === "/tools/hex-crawl"
                    || pathname.startsWith("/tools/hex-crawl/")
                    ? `${pathname}${window.location.search}${window.location.hash}` : null;
                const handoff = await api.createEncounterHandoff(runtime.id, {
                    expectedVersion: runtime.version,
                    handoffId: crypto.randomUUID(),
                    returnPath,
                    runtimeEncounterSequence: pending.triggerSequence,
                    journeyEventOccurrenceId: null
                });
                if (!disposed) window.location.assign(blockInitiativeHandoffHref(handoff));
            } catch (failure) {
                if (!disposed) showUiError(error, failure);
            } finally {
                busy = false;
                if (!disposed) render();
            }
        })();
    });
    required<HTMLButtonElement>("[data-resolve-encounter]").addEventListener("click", () => void progress(
        () => {
            const pending = currentState().pendingEncounter;
            if (!pending) throw new Error("No encounter is pending.");
            return api.resolveCellEncounter(runtime.id, pending.id, runtime.version,
                required<HTMLInputElement>("[data-encounter-result]").value.trim());
        }));

    /**
     * Phase 15 is the presentation owner. V2 topology supplies spatial
     * positions and atomic actions without recreating the retired hex watch
     * controller or representing a square/triangle as a six-sided cell.
     */
    function renderPhase15Chrome(): void {
        const state = currentState();
        const pending = state.pendingEncounter;
        const pause = state.activeWatchPendingDecision ?? runtime.pauseReason;
        const action = nextCellExpeditionAction({
            pendingEncounter: !!pending,
            pauseReason: pause,
            executable: !!runtime.procedure.runtime,
            hasSelectedCourse: selectedExit !== null || state.intendedHeading !== null,
            activeWatchNumber: state.activeWatchNumber,
            survival: survivalAttention,
            effects: effectsAttention,
            journey: journeyAttention,
            navigationDue: navigationResolutionDue(runtime,
                state.activeSuppressesNavigationCheck, state.activeDeliberateDoubleBack),
            attentionStatus
        });
        const guide = document.createElement("details");
        guide.className = "hc-guided-action-guide hc-guided-only";
        const summary = document.createElement("summary");
        summary.textContent = "What this action needs and changes";
        guide.append(summary,
            textElement("p", `Input: ${action.detail}`),
            textElement("p", "After resolution: the server stores the confirmed watch, position, cell boundary and any pending navigation or encounter outcomes."),
            textElement("p", `Saved position: ${topology.currentCell.address.motifCellId} (${topology.currentCell.address.translation.u}, ${topology.currentCell.address.translation.v})`),
            textElement("p", `Time: day ${state.currentDay}, ${state.completedWatches} completed watches.`));
        if (runtime.history.length > 0) {
            guide.append(textElement("p",
                `Last recorded event: ${runtime.history[runtime.history.length - 1].message}`));
        }
        const activate = (): void => {
            if (action.kind === "procedure") {
                navigate(`/procedures/${encodeURIComponent(runtime.procedure.procedureId)}/revisions/${runtime.procedure.revision}/reference`);
            } else if (action.kind === "survival") {
                openGameplayPanel("[data-survival-resources-panel]",
                    survivalAttention?.forcedTravel.checkDue
                        ? "Record forced-travel result" : "Apply resolved resource change");
            } else if (action.kind === "effects") {
                openGameplayPanel("[data-effects-panel]", "Record resolution");
            } else if (action.kind === "journey") {
                openGameplayPanel("[data-journey-panel]",
                    journeyAttention?.eventOccurrences.some(item => item.status === "ResolutionRequired")
                        ? "Resolve event" : "Resolve attempt");
            } else if (action.label === "Choose travel direction") {
                const navigatorButton = root.querySelector<HTMLButtonElement>(
                    "[data-cell-navigator] .hc-adjacency-interface:not(:disabled)");
                if (navigatorButton) navigatorButton.focus();
                else openTravelDrawer();
            } else if (action.label === "Loading gameplay state" || action.label === "Review gameplay state") {
                void refresh();
            } else {
                openTravelDrawer(action.kind === "encounter" ? "encounter"
                    : action.kind === "navigation" ? "navigation"
                        : action.kind === "boundary" && pause === "CellCourseAdjudicationRequired" ? "course"
                            : action.kind === "boundary" && pause === "LostRecognitionRequired" ? "lost"
                                : undefined);
            }
        };
        const content = renderPhase15NextAction(
            action, { label: action.label, detail: action.detail }, activate, guide,
            "The next action comes from the saved procedure and current server-authored expedition state.");
        required<HTMLElement>("[data-phase15-action]").replaceChildren(content);

        const address = topology.currentCell.address;
        const position = `Cell ${address.motifCellId} · ${address.translation.u}, ${address.translation.v}`;
        const openParty = (): void => openGameplayPanel("[data-periodic-party]");
        const stats = [
            statAction("Time",
                state.activeWatchNumber === null
                    ? `Day ${state.currentDay} · ${state.completedWatches} watches complete`
                    : `Day ${state.currentDay} · watch ${state.activeWatchNumber}`,
                state.activeWatchNumber === null ? "Ready for next watch" : "Travel period in progress",
                openHistory),
            statAction("Position", position,
                world ? "Mapped spatial position" : "Mapless spatial position",
                () => root.querySelector<HTMLElement>("[data-cell-navigator] button")?.focus(),
                pause ? "warning" : "neutral"),
            statAction("Movement", runtime.movementComposition.status,
                runtime.movementComposition.effectiveValue === null ? "Review travel conditions"
                    : `${runtime.movementComposition.effectiveValue} ${runtime.movementComposition.effectiveUnit ?? ""}`,
                () => openTravelDrawer(),
                runtime.movementComposition.missingInputs.length ? "warning" : "neutral")
        ];
        if (runtime.party.members.length || runtime.party.activityAssignments.length)
            stats.push(statAction("Party",
                `${runtime.party.members.length} members`, "Party and activities", openParty));
        if (runtime.procedure.runtime?.usesNavigationChecks || state.isLost)
            stats.push(statAction("Navigation",
                state.isLost ? "Lost / off course" : "Course known",
                pause === "LostRecognitionRequired" ? "Recognition decision required" : null,
                () => openTravelDrawer(pause === "LostRecognitionRequired" ? "lost" : "navigation"),
                state.isLost ? "warning" : "neutral"));
        if (pending)
            stats.push(statAction("Encounters", "Encounter active",
                "Resolve before more movement", () => openTravelDrawer("encounter"), "danger"));
        if (survivalAttention?.forcedTravel.checkDue || survivalAttention?.pendingResourceConsequences.length)
            stats.push(statAction("Survival", survivalAttention.forcedTravel.checkDue
                ? "Forced-travel check due" : "Consequence pending",
                "Resolution needed", () => openGameplayPanel("[data-survival-resources-panel]",
                    survivalAttention?.forcedTravel.checkDue
                        ? "Record forced-travel result" : "Apply resolved resource change"), "warning"));
        if (journeyAttention?.eventOccurrences.some(item => item.status === "ResolutionRequired")
            || journeyAttention?.activeProcesses.some(item => item.status === "ResolutionRequired"
                || item.pendingActions.length > 0))
            stats.push(statAction("Journey", "Resolution needed", "Review the pending event or stage",
                () => openGameplayPanel("[data-journey-panel]"), "warning"));
        required<HTMLElement>("[data-phase15-stats]").replaceChildren(...stats);
        required<HTMLButtonElement>("[data-open-travel]").disabled = busy || !runtime.procedure.runtime;
    }

    function renderSelectedCellDetails(): void {
        const panel = required<HTMLElement>("[data-cell-details]");
        panel.replaceChildren();
        required<HTMLButtonElement>("[data-gm-reposition]").disabled = !canReposition();
        if (!inspectedGeometry || !world) return;
        const selected = inspectedGeometry;
        const row = document.createElement("div");
        row.className = "hc-button-row";
        const teleport = document.createElement("button");
        teleport.type = "button";
        teleport.textContent = "Teleport party here";
        teleport.disabled = !canReposition();
        teleport.addEventListener("click", openRepositionDrawer);
        row.append(teleport);
        panel.append(row);
        if (currentState().activeWatchNumber !== null || currentState().pendingEncounter !== null)
            panel.append(textElement("p",
                "Resolve the active watch or pending encounter before teleporting the party.",
                "hc-muted"));
        const known = discoveredSubjectIds(runtime);
        const subjects = [
            ...world.locations.flatMap(location =>
                polygonContainsSubject(selected.polygon, location.position)
                    ? [{ id: location.id, name: location.name, type: "Location" as const }] : []),
            ...world.features.flatMap(feature =>
                feature.kind === "Point" && feature.position !== null
                    && polygonContainsSubject(selected.polygon, feature.position)
                    ? [{ id: feature.id, name: feature.name, type: "Feature" as const }] : [])
        ];
        if (subjects.length === 0) {
            panel.append(textElement("p", "No authored point locations or features are registered in this cell.", "hc-muted"));
            return;
        }
        panel.append(textElement("h3", "Player knowledge"));
        for (const subject of subjects) {
            const item = document.createElement("div");
            item.className = "hc-discovery-row";
            item.append(textElement("span", `${subject.name} · ${subject.type}`));
            if (known.has(subject.id)) {
                item.append(textElement("span", "Known to players", "hc-ux-badge hc-ux-badge-good"));
            } else {
                const reveal = document.createElement("button");
                reveal.type = "button";
                reveal.textContent = "Reveal / discover";
                reveal.disabled = busy;
                reveal.addEventListener("click", () => {
                    void progress(() => api.discover(runtime.id, runtime.version, subject.id, subject.type));
                });
                item.append(reveal);
            }
            panel.append(item);
        }
    }

    function render(): void {
        if (disposed) return;
        const state = currentState();
        text("[data-title]", runtime.name);
        text("[data-context-label]",
            `${runtime.context.name} · ${runtime.procedure.name}`);
        text("[data-map-title]", world ? "Expedition map" : "Mapless exploration");
        const place = state.position;
        text("[data-status]", `Day ${state.currentDay}, ${state.completedWatches} completed watches · ${state.activeWatchNumber === null ? "Ready for next watch" : `Watch ${state.activeWatchNumber} in progress`}`);
        text("[data-actual]", place
            ? `Actual position: ${place.x.toFixed(3)}, ${place.y.toFixed(3)} · ${state.isLost ? "Lost course" : "Course known"}`
            : "Actual position unavailable.");
        const adjacencies = currentPeriodicCellAdjacency(topology);
        const selected = selectedExit === null
            ? { ...adjacencies, selectedAdjacencyId: null }
            : { ...adjacencies,
                selectedAdjacencyId: adjacencyForIntent(adjacencies, selectedExit)?.id ?? null };
        const navigation = renderCurrentCellNavigator(selected, {
            selectable: !busy,
            actualIntent: topology.selectedExitInterfaceIndex,
            onToggle: edge => {
                // The navigator is outside the travel form. A new atomic
                // course still invalidates all previously generated rolls.
                if (generated) invalidateGenerated();
                selectHeading(edge.intentValue === selectedExit ? null : edge.intentValue);
                render();
                // Render replaces the navigator buttons; keep keyboard focus
                // on the same atomic interface for repeated decisions.
                queueMicrotask(() => {
                    if (disposed) return;
                    const controls = root.querySelectorAll<HTMLButtonElement>(
                        "[data-cell-navigator] .hc-adjacency-interface");
                    [...controls].find(control =>
                        control.dataset.adjacencyInterfaceId === edge.id)?.focus();
                });
            }
        });
        required<HTMLElement>("[data-cell-navigator]").replaceChildren(navigation);
        const selectedEdge = topology.interfaces.find(edge => edge.interfaceIndex === selectedExit);
        renderPhase15Chrome();
        text("[data-course]", selectedEdge
            ? `Intended boundary ${selectedExit! + 1}; adjacent cell selected. Travel has not been resolved.`
            : "Select an adjacent boundary for intended travel. This does not move the party.");
        if (input("headingX").value === "" || input("headingY").value === "") {
            if (state.intendedHeading) {
                input("headingX").value = String(state.intendedHeading.x);
                input("headingY").value = String(state.intendedHeading.y);
            } else if (selectedExit !== null) selectHeading(selectedExit);
        }
        // When reopening or entering a saved watch, carry forward its
        // resolved plan instead of presenting unchecked fallback toggles.
        if (displayedWatchNumber !== state.activeWatchNumber) {
            displayedWatchNumber = state.activeWatchNumber;
            input("doubleBack").checked = state.activeWatchNumber !== null
                && state.activeDeliberateDoubleBack;
            input("continueAcross").checked = state.activeWatchNumber !== null
                && state.activeContinueAcrossBoundaries;
        }
        const resolutions = runtime.procedure.runtime?.travelResolution;
        const helpers = runtime.procedure.runtime?.resolutionHelpers;
        required<HTMLElement>("[data-helper-section]").hidden =
            !helpers || ![helpers.travel, helpers.navigation, helpers.encounter].some(Boolean);
        required<HTMLButtonElement>("[data-generate-helper]").disabled = busy
            || !!state.pendingEncounter || state.activeWatchPendingDecision === "CellCourseAdjudicationRequired"
            || !helpers;
        const isSteps = resolutions === "CellSteps";
        required<HTMLElement>("[data-cell-step-field]").hidden = !isSteps;
        required<HTMLElement>("[data-distance-fields]").hidden = isSteps;
        const units = runtime.movementComposition.suggestedExpectedDistance?.unit
            ?? runtime.movementComposition.effectiveDistanceUnit
            ?? runtime.context.tiling?.physicalDistancePerWorldUnit?.unit;
        if (!isSteps && !generated && !input("expected").value.trim()
            && runtime.movementComposition.suggestedExpectedDistance) {
            input("expected").value = String(runtime.movementComposition.suggestedExpectedDistance.value);
        }
        text("[data-distance-unit]", isSteps ? "Cell transitions are resolved by the server."
            : units ? `Distance is entered in ${units.symbol}; the server resolves actual movement.`
                : "This procedure requires a calibrated distance unit before travel can be resolved.");
        // Reload the saved-world selection list when the world metadata changes.
        updateKeyedLocations();
        const cadence = runtime.procedure.runtime?.encounterCadence ?? "None";
        const encounterControl = choice("encounter");
        if (cadence === "None") encounterControl.value = "";
        encounterControl.disabled = busy || cadence === "None"
            || state.activeWatchNumber !== null;
        const contextualSchedule = encounterScheduleConfiguration(runtime.procedure);
        const cadenceLabel = cadence === "PerWatch" ? "Every watch"
            : cadence === "PerDay" ? "Every day" : "None";
        const humanize = (value: string): string =>
            value.replace(/[-_]/g, " ").replace(/\b\w/g, letter => letter.toUpperCase());
        const contextualLabel = contextualSchedule
            ? " Contextual schedule: "
                + humanize(contextualSchedule.scheduleModel ?? "unspecified")
                + "; " + (contextualSchedule.travelChecksPerInterval ?? "unspecified") + " per interval"
                + "; Camp check: " + (contextualSchedule.campCheck === null ? "unspecified" : contextualSchedule.campCheck ? "Yes" : "No")
                + "; " + humanize(contextualSchedule.terrainProbabilityModel ?? "unspecified")
                + ". Contextual schedule does not imply an automatic encounter cadence."
            : "";
        const instruction = cadence === "None"
            ? "This pinned ruleset does not schedule automatic encounter checks. Resolve travel without an automatic encounter result."
            : state.activeWatchNumber !== null
                ? "The encounter decision was captured when the watch started. Complete or resolve the current watch first."
                : "Supply an encounter result only when the pinned ruleset calls for a check in this watch.";
        text("[data-encounter-cadence]",
            `Encounter check schedule: Automatic cadence: ${cadenceLabel}.${contextualLabel} ${instruction}`);
        showKeyedLocation();
        const modes = runtime.movementComposition.policy.travelModeKeys;
        const pace = choice("pace");
        const effective = resolvePeriodicPace(modes, pace.value, state.activePaceKey);
        pace.replaceChildren();
        // Even without an editable selector, a fixed pace must have an exact
        // value to submit to the shared server movement contract.
        for (const mode of modes.length ? modes : [effective.paceKey]) {
            const option = document.createElement("option");
            option.value = mode;
            option.textContent = mode.replace(/[-_]/g, " ");
            pace.append(option);
        }
        pace.value = effective.paceKey;
        required<HTMLElement>("[data-pace-row]").hidden = !effective.needsChoice;
        text("[data-fixed-pace]", modes.length === 0
            ? "Fixed pace: normal (the server default). This ruleset does not require a pace choice."
            : modes.length === 1
                ? `Fixed pace: ${modes[0].replace(/[-_]/g, " ")}.`
                : "");
        pace.disabled = busy || state.activeWatchNumber !== null;
        const pausedForCourse = state.activeWatchPendingDecision === "CellCourseAdjudicationRequired";
        const pendingEncounter = state.pendingEncounter;
        required<HTMLElement>("[data-course-decision]").hidden = !pausedForCourse;
        required<HTMLElement>("[data-encounter-decision]").hidden = !pendingEncounter;
        text("[data-encounter-description]", pendingEncounter
            ? `Encounter at watch ${pendingEncounter.watchNumber}. Resolve it before continuing.` : "");
        required<HTMLButtonElement>("[data-advance]").disabled =
            busy || pausedForCourse || !!pendingEncounter || !resolutions;
        required<HTMLButtonElement>("[data-confirm-course]").disabled =
            busy || !pausedForCourse || selectedExit === null;
        required<HTMLButtonElement>("[data-resolve-encounter]").disabled =
            busy || !pendingEncounter;
        required<HTMLButtonElement>("[data-handoff-encounter]").disabled =
            busy || !pendingEncounter;
        const mapPoint = topology.position;
        map.renderer.currentWorldCell = {
            id: topology.currentCell,
            polygon: topology.polygon,
            center: topology.center
        };
        map.renderer.expeditionPosition = { ...mapPoint };
        map.renderer.discoveredSubjectIds = discoveredSubjectIds(runtime);
        if (inspectedGeometry
            && (inspectedGeometry.id.tilingId !== topology.currentCell.tilingId
                || inspectedTilingRevision !== (world?.tiling?.revision ?? null))) {
            inspectedGeometry = null;
            inspectedTilingRevision = null;
        }
        map.renderer.selectedWorldCell = inspectedGeometry;
        renderSelectedCellDetails();
        map.requestRender();
        const party = required<HTMLElement>("[data-party]");
        party.replaceChildren();
        const heading = document.createElement("p");
        heading.textContent = runtime.party.members.length === 0
            ? "No party members assigned." : runtime.party.members.map(member => member.name).join(", ");
        party.append(heading);
        const movement = required<HTMLElement>("[data-movement]");
        movement.replaceChildren(movementCompositionLedger(runtime));
        const history = required<HTMLElement>("[data-history]");
        history.replaceChildren();
        for (const entry of runtime.history.slice(-30).reverse()) {
            const li = document.createElement("li");
            li.textContent = entry.message;
            history.append(li);
        }

    }
    render();
    gameplay.sync();
    return () => {
        disposed = true;
        refreshGeneration++;
        travelDrawer?.close();
        historyDrawer?.close();
        gameplay.dispose();
        map.dispose();
        root.classList.remove("hc-phase15");
    };
}

function timespanFromHours(hours: number): string {
    if (!Number.isFinite(hours) || hours < 0 || hours > 100000)
        throw new Error("Encounter time must be a finite, nonnegative hour value.");
    const totalMilliseconds = Math.round(hours * 3600000);
    const hh = Math.floor(totalMilliseconds / 3600000);
    const mm = Math.floor((totalMilliseconds % 3600000) / 60000);
    const ss = Math.floor((totalMilliseconds % 60000) / 1000);
    const ms = totalMilliseconds % 1000;
    return `${hh.toString().padStart(2, "0")}:${mm.toString().padStart(2, "0")}:${ss.toString().padStart(2, "0")}`
        + (ms > 0 ? `.${ms.toString().padStart(3, "0")}` : "");
}
