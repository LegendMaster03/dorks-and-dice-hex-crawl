
import type { HexCrawlApi } from "../../api";
import { MapSurface } from "../../map-surface";
import { blockInitiativeHandoffHref } from "../../encounter-handoff";
import type {
    CellTraversalContext, CellProcedureResolutionHelperResult, CellWatchRequest, ExpeditionDetail, Overworld,
    WorldCellId, WorldPoint, ToolHostContext
} from "../../types";
import { showUiError, clearUiError } from "../../ui-error";
import { currentPeriodicCellAdjacency } from "./current-cell-topology";
import { renderCurrentCellNavigator } from "./cell-navigator";
import { adjacencyForIntent } from "./spatial-adjacency";
import { guidancePreferenceButton } from "../../ui/guidance";
import { openWorkspaceDrawer, statAction, textElement, type WorkspaceDrawer } from "../../ui/workspace";
import { renderPhase15NextAction } from "./phase15-next-action";
import { nextCellExpeditionAction } from "./phase15-cell-action";
import { resolveHexStartCell } from "./world-cell-start";
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
    let generated: CellProcedureResolutionHelperResult | null = null;
    let inspectedCell: WorldCellId | null = null;

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
                    <p class="hc-hint" data-inspection>Click a cell to inspect it. This does not change travel intent or move the party.</p>
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
                        <button type="button" data-gm-party>Party and gameplay</button>
                        <button type="button" data-gm-history>Expedition history</button>
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
    const openTravelDrawer = (focus?: "encounter" | "course"): void => {
        if (!travelDrawer) {
            travelDrawer = openWorkspaceDrawer(root, "Travel / watch",
                body => { body.append(travelContent); },
                document.activeElement instanceof HTMLElement ? document.activeElement : null,
                () => {
                    travelDock.append(travelContent);
                    travelDrawer = null;
                });
        }
        const target = focus === "encounter" ? required<HTMLButtonElement>("[data-resolve-encounter]")
            : focus === "course" ? required<HTMLButtonElement>("[data-confirm-course]")
                : required<HTMLButtonElement>("[data-advance]");
        target.focus();
    };
    required<HTMLButtonElement>("[data-open-travel]").addEventListener("click",
        () => openTravelDrawer());
    required<HTMLButtonElement>("[data-gm-travel]").addEventListener("click",
        () => openTravelDrawer());
    required<HTMLButtonElement>("[data-gm-party]").addEventListener("click", () => {
        const disclosure = required<HTMLDetailsElement>("[data-gameplay-disclosure]");
        disclosure.open = true;
        disclosure.scrollIntoView({ block: "nearest" });
    });
    required<HTMLButtonElement>("[data-gm-history]").addEventListener("click", () => {
        const disclosure = required<HTMLDetailsElement>("[data-history-disclosure]");
        disclosure.open = true;
        disclosure.scrollIntoView({ block: "nearest" });
    });
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
    let hexInspectionGeneration = 0;
    map.setHexSelectionHandler(hex => {
        // Legacy q/r is only an editor gesture; identify the selected cell
        // using the server's accepted geometry, never by fabricating V1 state.
        if (!world?.grid || !hex) return;
        const current = world;
        const generation = ++hexInspectionGeneration;
        void (async () => {
            try {
                const address = await resolveHexStartCell(api, current, hex);
                if (disposed || generation !== hexInspectionGeneration) return;
                inspectedCell = { tilingId: current.tiling!.id, address };
                text("[data-inspection]",
                    `Selected cell via grid ${hex.q}, ${hex.r}. Inspection does not move the party.`);
            } catch (cause) {
                if (!disposed && generation === hexInspectionGeneration) showUiError(error, cause);
            }
        })();
    });
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
            generated = null; // Any other aggregate mutation supersedes this version-bound grant.
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
            generated = null;
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
            generated = null;
            world = nextWorld;
            required<HTMLButtonElement>("[data-world]").hidden = world === null;
            topology = nextTopology;
            selectedExit = topology.selectedExitInterfaceIndex;
            if (!world) map.centerOn(topology.position);
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
                ?? runtime.movementComposition.effectiveDistanceUnit
                ?? runtime.context.hexCenterDistance?.unit;
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
    form.addEventListener("input", event => {
        if ((event.target as Element).closest("[data-helper-section]")) return;
        if (generated) invalidateGenerated();
    });
    form.addEventListener("change", event => {
        if ((event.target as Element).closest("[data-helper-section]")) return;
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
            clearUiError(error);
            const button = required<HTMLButtonElement>("[data-generate-helper]");
            button.disabled = true;
            generated = null;
            try {
                const unit = runtime.movementComposition.suggestedExpectedDistance?.unit
                    ?? runtime.movementComposition.effectiveDistanceUnit
                    ?? runtime.context.hexCenterDistance?.unit;
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
                    await refresh();
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
            activeWatchNumber: state.activeWatchNumber
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
            } else if (action.label === "Choose travel direction") {
                const navigatorButton = root.querySelector<HTMLButtonElement>(
                    "[data-cell-navigator] .hc-adjacency-interface:not(:disabled)");
                if (navigatorButton) navigatorButton.focus();
                else openTravelDrawer();
            } else {
                openTravelDrawer(action.kind === "encounter" ? "encounter"
                    : action.kind === "boundary" && pause === "CellCourseAdjudicationRequired" ? "course"
                    : undefined);
            }
        };
        const content = renderPhase15NextAction(
            action, { label: action.label, detail: action.detail }, activate, guide,
            "The next action comes from the saved procedure and current server-authored expedition state.");
        required<HTMLElement>("[data-phase15-action]").replaceChildren(content);

        const address = topology.currentCell.address;
        const position = `Cell ${address.motifCellId} · ${address.translation.u}, ${address.translation.v}`;
        const openParty = (): void => {
            const panel = required<HTMLDetailsElement>("[data-gameplay-disclosure]");
            panel.open = true;
            panel.scrollIntoView({ block: "nearest" });
        };
        const stats = [
            statAction("Time",
                state.activeWatchNumber === null
                    ? `Day ${state.currentDay} · ${state.completedWatches} watches complete`
                    : `Day ${state.currentDay} · watch ${state.activeWatchNumber}`,
                state.activeWatchNumber === null ? "Ready for next watch" : "Travel period in progress",
                () => required<HTMLDetailsElement>("[data-history-disclosure]").open = true),
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
                () => openTravelDrawer(), state.isLost ? "warning" : "neutral"));
        if (pending)
            stats.push(statAction("Encounters", "Encounter active",
                "Resolve before more movement", () => openTravelDrawer("encounter"), "danger"));
        required<HTMLElement>("[data-phase15-stats]").replaceChildren(...stats);
        required<HTMLButtonElement>("[data-open-travel]").disabled = busy || !runtime.procedure.runtime;
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
            ?? runtime.context.hexCenterDistance?.unit;
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
        required<HTMLButtonElement>("[data-handoff-encounter]").disabled =
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
        travelDrawer?.close();
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
