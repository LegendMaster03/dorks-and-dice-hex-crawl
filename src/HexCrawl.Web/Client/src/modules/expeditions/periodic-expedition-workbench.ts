
import type { HexCrawlApi } from "../../api";
import { MapSurface } from "../../map-surface";
import type {
    CellTraversalContext, CellWatchRequest, ExpeditionDetail, Overworld,
    WorldCellId, WorldPoint, ToolHostContext
} from "../../types";
import { showUiError, clearUiError } from "../../ui-error";
import { currentPeriodicCellAdjacency } from "./current-cell-topology";
import { renderCurrentCellNavigator } from "./cell-navigator";
import { adjacencyForIntent } from "./spatial-adjacency";
import { guidancePreferenceButton } from "../../ui/guidance";
import { encounterLocationId } from "./keyed-location-encounter";
import { mountPeriodicGameplayPanels } from "./periodic-gameplay-panels";
import { publishExpeditionRuntimeChanged } from "./expedition-runtime-events";
import { resolvePeriodicPace } from "./periodic-travel-pace";

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
    let inspectedCell: WorldCellId | null = null;

    root.innerHTML = `
        <section class="hc-page hc-workspace hc-expedition hc-periodic-expedition">
            <header class="hc-page-header">
                <div><span class="hc-sheet-kicker">Expedition</span>
                    <h1 data-title></h1><p data-context-label></p></div>
                <nav class="hc-button-row">
                    <button type="button" data-world>World map</button>
                    <button type="button" data-reload>Reload expedition</button>
                    <span data-guidance-controls></span>
                </nav>
            </header>
            <div data-error class="hc-error" role="alert" hidden></div>
            <div class="hc-workspace-grid">
                <section class="hc-map-panel" aria-label="Expedition map">
                    <div class="hc-map-host" data-map></div>
                    <p class="hc-hint" data-inspection>Click a cell to inspect it. This does not change travel intent or move the party.</p>
                </section>
                <aside class="hc-sidebar hc-stack" aria-label="Expedition controls">
                    <section class="hc-stack" aria-label="Current position">
                        <h2>Current location</h2><p data-status></p><p data-actual></p>
                        <div data-cell-navigator></div>
                        <p class="hc-hint" data-course>Choose a boundary to set intended travel.</p>
                    </section>
                    <section class="hc-stack" aria-label="Travel decisions">
                        <h2>Travel</h2>
                        <p class="hc-hint">Choosing a course does not resolve movement. A watch advances only when you confirm the DM resolution.</p>
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
                            <button type="button" data-resolve-encounter>Resolve pending encounter</button></div>
                    </section>
                    <details><summary>Party and movement summary</summary><div data-party></div><div data-movement></div></details>
                    <section class="hc-stack hc-page hc-gameplay-controls" data-gameplay-panels
                        aria-label="Shared party and gameplay editors">
                        <h2>Party and gameplay</h2>
                        <p class="hc-hint">These are the same party, environment, journey, effects and survival editors used by other expeditions.</p>
                    </section>
                    <details><summary>Journey events</summary><ol data-history></ol></details>
                </aside>
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
            choice("encounter").value !== "KeyedLocationDiscovery";
    };
    choice("encounter").addEventListener("change", showKeyedLocation);
    showKeyedLocation();

    const map = new MapSurface(required<HTMLElement>("[data-map]"), () => world, undefined, api);
    required<HTMLButtonElement>("[data-world]").hidden = world === null;
    required<HTMLElement>("[data-guidance-controls]").append(guidancePreferenceButton(root));
    required<HTMLButtonElement>("[data-world]").addEventListener("click", () => {
        if (world) navigate(`/worlds/${world.id}`);
    });
    required<HTMLButtonElement>("[data-reload]").addEventListener("click", () => void refresh());
    map.setCellSelectionHandler(cell => {
        inspectedCell = cell?.id ?? null;
        text("[data-inspection]", cell
            ? `Selected an adjacent or distant cell at ${cell.center.x.toFixed(2)}, ${cell.center.y.toFixed(2)}. The party remains in its current cell.`
            : "Cell inspection cleared. The party has not moved.");
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
        if (interfaceIndex === null) return;
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
            publishExpeditionRuntimeChanged(root, runtime);
        },
        async (control, action) => {
            if (busy || disposed) return;
            busy = true;
            if (control) control.disabled = true;
            clearUiError(error);
            try {
                await action();
                if (!disposed) await refresh();
            } catch (failure) {
                if (!disposed) {
                    showUiError(error, failure);
                    if (failure instanceof Error && /version|conflict|stale/i.test(failure.message))
                        await refresh();
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
        }
    );

    const progress = async (action: () => Promise<ExpeditionDetail>): Promise<void> => {
        if (busy || disposed) return;
        busy = true;
        clearUiError(error);
        form.querySelectorAll<HTMLButtonElement>("button").forEach(button => button.disabled = true);
        try {
            const updated = await action();
            if (disposed) return;
            runtime = updated;
            topology = await api.getCellTopology(runtime.id);
            selectedExit = topology.selectedExitInterfaceIndex;
            render();
            gameplay.sync();
            publishExpeditionRuntimeChanged(root, runtime);
        } catch (failure) {
            if (disposed) return;
            showUiError(error, failure);
            if (failure instanceof Error && /version|conflict|stale/i.test(failure.message)) {
                await refresh();
            }
        } finally {
            busy = false;
            // Reapply the authoritative pause and encounter gates instead of
            // unconditionally enabling an advance button after completion.
            if (!disposed) render();
        }
    };
    const refresh = async (): Promise<void> => {
        try {
            const [next, nextTopology, nextWorld] = await Promise.all([
                api.getExpedition(runtime.id), api.getCellTopology(runtime.id),
                world ? api.getOverworld(world.id) : Promise.resolve(null)
            ]);
            if (disposed) return;
            runtime = next;
            world = nextWorld;
            required<HTMLButtonElement>("[data-world]").hidden = world === null;
            topology = nextTopology;
            selectedExit = topology.selectedExitInterfaceIndex;
            render();
            gameplay.sync();
            publishExpeditionRuntimeChanged(root, runtime);
        } catch (failure) {
            if (!disposed) showUiError(error, failure);
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
                ?? runtime.movementComposition.effectiveDistanceUnit;
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
                    travel, navigation: nav, encounter, boundaryDecision,
                    dmOverrideNote: "DM confirmed generalized watch inputs"
                }
            });
        });
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
    required<HTMLButtonElement>("[data-resolve-encounter]").addEventListener("click", () => void progress(
        () => {
            const pending = currentState().pendingEncounter;
            if (!pending) throw new Error("No encounter is pending.");
            return api.resolveCellEncounter(runtime.id, pending.id, runtime.version,
                required<HTMLInputElement>("[data-encounter-result]").value.trim());
        }));

    function render(): void {
        if (disposed) return;
        const state = currentState();
        text("[data-title]", runtime.name);
        text("[data-context-label]", world
            ? "World-bound travel and discovery" : "Mapless cell travel");
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
                selectHeading(edge.intentValue === selectedExit ? null : edge.intentValue);
                render();
            }
        });
        required<HTMLElement>("[data-cell-navigator]").replaceChildren(navigation);
        const selectedEdge = topology.interfaces.find(edge => edge.interfaceIndex === selectedExit);
        text("[data-course]", selectedEdge
            ? `Intended boundary ${selectedExit! + 1}; adjacent cell selected. Travel has not been resolved.`
            : "Select an adjacent boundary for intended travel. This does not move the party.");
        if (input("headingX").value === "" || input("headingY").value === "") {
            if (state.intendedHeading) {
                input("headingX").value = String(state.intendedHeading.x);
                input("headingY").value = String(state.intendedHeading.y);
            } else if (selectedExit !== null) selectHeading(selectedExit);
        }
        const resolutions = runtime.procedure.runtime?.travelResolution;
        const isSteps = resolutions === "CellSteps";
        required<HTMLElement>("[data-cell-step-field]").hidden = !isSteps;
        required<HTMLElement>("[data-distance-fields]").hidden = isSteps;
        const units = runtime.movementComposition.suggestedExpectedDistance?.unit
            ?? runtime.movementComposition.effectiveDistanceUnit;
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
        text("[data-encounter-cadence]", cadence === "None"
            ? "This pinned ruleset does not schedule encounter checks. Resolve travel without an encounter result."
            : state.activeWatchNumber !== null
                ? "The encounter decision was captured when the watch started. Complete or resolve the current watch first."
                : "An encounter result is used only when the pinned ruleset calls for a check in this watch.");
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
        const mapPoint = topology.position;
        map.renderer.currentWorldCell = {
            id: topology.currentCell,
            polygon: topology.polygon,
            center: topology.center
        };
        map.renderer.expeditionPosition = { ...mapPoint };
        if (world?.grid) map.renderer.expeditionHex = null;
        map.requestRender();
        const party = required<HTMLElement>("[data-party]");
        party.replaceChildren();
        const heading = document.createElement("p");
        heading.textContent = runtime.party.members.length === 0
            ? "No party members assigned." : runtime.party.members.map(member => member.name).join(", ");
        party.append(heading);
        text("[data-movement]",
            `Movement: ${runtime.movementComposition.status.toLowerCase()}`
            + (runtime.movementComposition.effectiveValue === null ? "" :
                ` · ${runtime.movementComposition.effectiveValue} ${runtime.movementComposition.effectiveUnit ?? ""}`));
        const history = required<HTMLElement>("[data-history]");
        history.replaceChildren();
        for (const entry of runtime.history.slice(-30).reverse()) {
            const li = document.createElement("li");
            li.textContent = entry.message;
            history.append(li);
        }
        if (inspectedCell && inspectedCell.tilingId !== topology.currentCell.tilingId)
            inspectedCell = null;
    }
    render();
    gameplay.sync();
    return () => {
        disposed = true;
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
