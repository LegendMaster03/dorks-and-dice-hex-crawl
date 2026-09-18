import type { HexCrawlApi } from "./api";
import { manualEntryResolutionSources } from "./expedition-input-policy";
import { encounterCheckDue, navigationResolutionDue, pauseInstruction, watchActionLabel, watchPhase } from "./expedition-workflow";
import { worldToHex } from "./hex-math";
import { MapSurface } from "./map-surface";
import { discoveredSubjectIds, directionLabel, formatDistance, formatHours } from "./runtime-view";
import { canonicalExpeditionRoute } from "./tool-route";
import type { ExpeditionDetail, Overworld, ProcedureResolutionHelperRequest, ProcedureResolutionHelperResult, ResolutionSource, RuntimeAdvanceRequest, SpatialRuntimeExpedition } from "./types";
import { clearUiError, showUiError } from "./ui-error";

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
    let advancePending = false;

    const modeLabel = showMap ? "Full crawl workbench" : "Mapless expedition tracker";
    const mapMarkup = showMap ? '<div class="hc-map-host" data-map></div>' : "";
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
            <div class="hc-view-switcher" aria-label="Expedition views">
                <button type="button" data-view-tracker>Tracker</button>
                <button type="button" data-view-map>Full map</button>
                <button type="button" data-view-travel>Travel / watch</button>
                <button type="button" data-view-navigation>Navigation</button>
                <button type="button" data-view-encounters>Encounters</button>
            </div>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="${showMap ? "hc-workspace-grid" : "hc-tracker-grid"}">
                <section class="${showMap ? "hc-map-panel" : "hc-panel hc-runtime-panel"}" aria-label="Expedition state${showMap ? " and map" : ""}">
                    ${mapMarkup}
                    <section class="hc-status-section" aria-labelledby="hc-expedition-state-title">
                        <h2 id="hc-expedition-state-title">Expedition state</h2>
                        <div class="hc-status-grid" data-status></div>
                    </section>
                    <section data-pause-panel hidden></section>
                    <details open><summary>Recent procedure history</summary><ol class="hc-history" data-history></ol></details>
                </section>
                <aside class="hc-sidebar" aria-label="Expedition controls">
                    <details open><summary data-watch-summary>Run watch</summary>
                        <div class="hc-form" data-requirements></div>
                        <form class="hc-form" data-advance>
                            <fieldset data-plan-fields data-focus-group="travel navigation">
                                <legend>Travel plan</legend>
                                <label>Intended direction <select name="direction">
                                    <option value="0">0</option><option value="1">1</option><option value="2">2</option>
                                    <option value="3">3</option><option value="4">4</option><option value="5">5</option>
                                </select></label>
                                <label>Pace / travel mode <input name="pace" value="normal"></label>
                                <label>Party activities <input name="activities" placeholder="scout, forage, map"></label>
                                <label>Navigation aid/context <input name="navigationAid" value="none"></label>
                                <label data-suppress-nav-row><input name="suppressNav" type="checkbox"> Navigation aid suppresses the check</label>
                                <label data-reset-veer-row><input name="resetVeer" type="checkbox"> Navigation aid resets veer at a boundary</label>
                                <label><input name="continueAcross" type="checkbox"> Continue automatically across unchanged boundaries</label>
                                <label data-double-back-row><input name="doubleBack" type="checkbox"> Deliberate single-hex double-back</label>
                                <p class="hc-hint" data-direction-hint></p>
                            </fieldset>

                            <fieldset data-resolution-helper>
                                <legend>Optional procedure resolution helper</legend>
                                <p class="hc-hint">Generate the procedure-defined random inputs for this watch. The helper only fills the explicit resolved fields below; nothing changes in the crawl session until you run the watch.</p>
                                <div data-helper-travel><p class="hc-hint">Travel uses the expected distance entered below as the DM-confirmed situational input.</p></div>
                                <div data-helper-navigation class="hc-form">
                                    <label>Navigation DC <input name="helperNavigationDc" type="number" step="1" placeholder="DM-confirmed DC"></label>
                                    <label>Navigation modifier <input name="helperNavigationModifier" type="number" step="1" value="0"></label>
                                    <label>Failure veer <input name="helperFailureVeer" type="number" step="1" placeholder="+1 or -1"></label>
                                    <p class="hc-hint">Failure veer remains DM-confirmed because the deterministic runtime requires an explicit non-zero veer on a failed navigation check.</p>
                                </div>
                                <div data-helper-encounter><p class="hc-hint">The configured encounter check and encounter timing are generated automatically when a check is due.</p></div>
                                <button type="button" data-resolution-helper-button>Resolve configured inputs</button>
                                <p class="hc-hint" data-resolution-helper-result></p>
                            </fieldset>

                            <fieldset data-travel-resolution data-focus-group="travel">
                                <legend>Resolved travel context</legend>
                                <p class="hc-hint">Supply the effective movement result for this watch segment. Terrain and route category names are descriptive; the runtime does not infer a multiplier from them.</p>
                                <div data-fixed-distance><label>Effective distance <input name="effectiveDistance" type="number" min="0" step="any"></label></div>
                                <div data-variable-distance><label>Expected distance <input name="expectedDistance" type="number" min="0" step="any"></label><label>Actual resolved distance <input name="actualDistance" type="number" min="0" step="any"></label></div>
                                <div data-step-distance><label>Resolved hex steps <input name="hexSteps" type="number" min="0" step="1" value="1"></label></div>
                                <label>Travel result source <select name="travelSource"></select></label>
                                <label>Travel source note <input name="travelNote" placeholder="optional"></label>
                            </fieldset>

                            <fieldset data-navigation-resolution data-focus-group="navigation">
                                <legend>Navigation resolution</legend>
                                <label>Result <select name="navigationOutcome"><option value="Succeeded">Succeeded</option><option value="Failed">Failed / lost</option></select></label>
                                <label data-veer-row>Resolved veer steps <input name="veerSteps" type="number" step="1" value="1"></label>
                                <label>Navigation result source <select name="navigationSource"></select></label>
                                <label>Navigation source note <input name="navigationNote" placeholder="optional"></label>
                            </fieldset>

                            <fieldset data-encounter-resolution data-focus-group="encounters">
                                <legend>Encounter check</legend>
                                <label>Resolved outcome <select name="encounterOutcome"><option value="None">No encounter</option><option value="WanderingEncounter">Wandering encounter</option><option value="KeyedLocationDiscovery">Keyed location discovery</option><option value="ManualCustom">Manual / custom interruption</option></select></label>
                                <label data-encounter-hour>Occurs at hour within watch <input name="encounterHour" type="number" min="0" step="any"></label>
                                <label data-encounter-location>Keyed location <select name="locationId"><option value="">—</option></select></label>
                                <label>Encounter note <input name="encounterNote" placeholder="optional"></label>
                                <label>Encounter result source <select name="encounterSource"></select></label>
                                <label>Encounter source note <input name="encounterSourceNote" placeholder="optional"></label>
                            </fieldset>

                            <fieldset data-boundary-resolution data-focus-group="navigation">
                                <legend>Lost boundary decision</legend>
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
                    <details><summary>${showMap ? "Procedure and presentation snapshots" : "Procedure snapshot"}</summary><div data-snapshots></div></details>
                </aside>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const form = required<HTMLFormElement>(root, "[data-advance]");
    const advanceButton = required<HTMLButtonElement>(form, "[data-advance-button]");
    const resolutionHelperButton = required<HTMLButtonElement>(form, "[data-resolution-helper-button]");
    const resolutionHelperResult = required<HTMLElement>(form, "[data-resolution-helper-result]");
    const mapHost = root.querySelector<HTMLElement>("[data-map]");
    const map = mapHost && world ? new MapSurface(mapHost, () => world) : null;
    const locationSelect = select(form, "locationId");
    for (const name of ["travelSource", "navigationSource", "encounterSource", "boundarySource"] as const) {
        const control = select(form, name);
        for (const source of manualEntryResolutionSources) control.append(option(source, sourceLabel(source)));
        control.value = "ManualRoll";
    }
    if (world) {
        for (const location of world.locations) locationSelect.append(option(location.id, location.name));
    } else {
        select(form, "encounterOutcome").querySelector('option[value="KeyedLocationDiscovery"]')?.remove();
    }

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
        renderStatus();
        renderPause();
        renderHistory();
        if (showMap) {
            renderDiscovery();
            renderPlayerPreview();
        }
        renderSnapshots();
        syncWatchForm();
    };

    const renderStatus = (): void => {
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
            statusCell("State", watchPhase(runtime) === "paused" ? `Paused · ${runtime.pauseReason}` : watchPhase(runtime) === "active" ? "Watch active" : "Ready")
        ];
        if (runtime.profile.tracksIntraHexProgress) {
            cells.push(statusCell(
                "Intra-hex progress",
                state.exitRequirement ? `${formatDistance(state.hexProgress)} / ${formatDistance(state.exitRequirement)}` : formatDistance(state.hexProgress)));
        }
        if (state.activeWatchNumber !== null) cells.push(statusCell("Watch remaining", formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)));
        status.replaceChildren(...cells);
    };

    const renderPause = (): void => {
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
    };

    const renderHistory = (): void => {
        const host = required<HTMLOListElement>(root, "[data-history]");
        host.replaceChildren();
        const recent = [...runtime.history].reverse().slice(0, 30);
        if (recent.length === 0) {
            const item = document.createElement("li");
            item.textContent = "No procedure events yet.";
            host.append(item);
            return;
        }
        for (const event of recent) {
            const item = document.createElement("li");
            const hex = event.hex ? ` · hex ${event.hex.q},${event.hex.r}` : "";
            item.textContent = `#${event.sequence} · watch ${event.watchNumber} · ${formatHours(event.expeditionElapsedHours)}${hex} · ${event.message}`;
            host.append(item);
        }
    };

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
            hint.textContent = "No authored point locations or features are indexed in the current hex.";
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

    const renderPlayerPreview = (): void => {
        if (!world) return;
        const host = required<HTMLElement>(root, "[data-player-preview]");
        host.replaceChildren();
        const presentationPolicy = runtime.presentation;
        if (!presentationPolicy) return;
        const summary = document.createElement("p");
        summary.textContent = `${presentationPolicy.name}: player grid ${presentationPolicy.playerGrid.toLowerCase()}, terrain ${prettyEnum(presentationPolicy.terrainMode)}, ${runtime.knownHexes.length} explored/known hex(es), ${runtime.knowledge.length} known subject(s).`;
        host.append(summary);
        if (runtime.knownHexes.length > 0) {
            const knownHexes = document.createElement("p");
            knownHexes.className = "hc-hint";
            knownHexes.textContent = `Known hexes: ${runtime.knownHexes.map(hex => `${hex.q},${hex.r}`).join(" · ")}`;
            host.append(knownHexes);
        }
        const list = document.createElement("ul");
        for (const entry of runtime.knowledge) {
            const subject = subjectLabel(entry.subjectId);
            const item = document.createElement("li");
            item.textContent = `${subject} · ${entry.subjectType} · ${entry.state}${entry.source ? ` · ${entry.source}` : ""}`;
            list.append(item);
        }
        if (list.childElementCount === 0) {
            const item = document.createElement("li");
            item.textContent = "No semantic subjects are currently known to the players.";
            list.append(item);
        }
        host.append(list);
        const warning = document.createElement("p");
        warning.className = "hc-hint";
        warning.textContent = "This is a knowledge-state preview, not a player renderer. GM source maps are not treated as player-visible merely because this policy is open.";
        host.append(warning);
    };

    const renderSnapshots = (): void => {
        const host = required<HTMLElement>(root, "[data-snapshots]");
        host.replaceChildren();
        const procedure = document.createElement("p");
        procedure.textContent = `${runtime.profile.name} (${runtime.profile.key}) · ${runtime.profile.watchHours}h watch · ${prettyEnum(runtime.profile.travelResolution)} · ${prettyEnum(runtime.profile.actualDistanceResolution)} · encounters ${prettyEnum(runtime.profile.encounterCadence)} · navigation ${runtime.profile.usesNavigationChecks ? "enabled" : "disabled"} · veer ${runtime.profile.usesPersistentVeer ? "persistent" : "non-persistent"}.`;
        const note = document.createElement("p");
        note.className = "hc-hint";
        if (showMap && runtime.presentation) {
            const presentation = document.createElement("p");
            presentation.textContent = `${runtime.presentation.name} (${runtime.presentation.key}) · grid ${runtime.presentation.playerGrid.toLowerCase()} · terrain ${prettyEnum(runtime.presentation.terrainMode)} · automation ${prettyEnum(runtime.presentation.automationMode)}.`;
            note.textContent = "Both are stored snapshots for this expedition. Catalog changes do not reconstruct active expedition behavior.";
            host.append(procedure, presentation, note);
        } else {
            note.textContent = "The tracker uses the persisted crawl procedure snapshot. Map presentation remains owned by the full map workbench.";
            host.append(procedure, note);
        }
    };

    const syncWatchForm = (): void => {
        const state = spatialState(runtime);
        const newWatch = state.activeWatchNumber === null;
        required<HTMLElement>(root, "[data-watch-summary]").textContent = watchActionLabel(runtime);
        advanceButton.textContent = watchActionLabel(runtime);

        const requirements = required<HTMLElement>(root, "[data-requirements]");
        const requirementLines: string[] = [];
        if (runtime.pauseReason) requirementLines.push(`Resolve pending ${prettyEnum(runtime.pauseReason)} before the watch can continue.`);
        else if (newWatch) requirementLines.push(`Plan watch ${state.completedWatches + 1} (${formatHours(runtime.profile.watchHours)}).`);
        else requirementLines.push(`Continue watch ${state.activeWatchNumber} with ${formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)} remaining.`);
        if (runtime.profile.actualDistanceResolution === "VariableResolved" && runtime.profile.travelResolution === "ContinuousDistance") requirementLines.push("A resolved expected and actual distance are required for this segment.");
        else if (runtime.profile.travelResolution === "ContinuousDistance") requirementLines.push("An effective travel distance is required for this segment.");
        else requirementLines.push("A resolved hex-step count is required for this segment.");
        if (newWatch && runtime.profile.usesNavigationChecks) requirementLines.push("Navigation resolution is required unless the selected aid suppresses it or this is a deliberate double-back.");
        if (encounterCheckDue(runtime)) requirementLines.push(`An encounter check is due (${prettyEnum(runtime.profile.encounterCadence)} cadence).`);
        requirements.replaceChildren(...requirementLines.map(text => paragraph(text)));

        const continuous = runtime.profile.travelResolution === "ContinuousDistance";
        required<HTMLElement>(form, "[data-fixed-distance]").hidden = !(continuous && runtime.profile.actualDistanceResolution === "Fixed");
        required<HTMLElement>(form, "[data-variable-distance]").hidden = !(continuous && runtime.profile.actualDistanceResolution === "VariableResolved");
        required<HTMLElement>(form, "[data-step-distance]").hidden = continuous;

        required<HTMLElement>(form, "[data-encounter-resolution]").hidden = !encounterCheckDue(runtime);
        required<HTMLElement>(form, "[data-boundary-resolution]").hidden = runtime.pauseReason !== "LostRecognitionRequired";
        required<HTMLElement>(form, "[data-double-back-row]").hidden = !runtime.profile.supportsDeliberateDoubleBack;
        required<HTMLElement>(form, "[data-suppress-nav-row]").hidden = !runtime.profile.usesNavigationChecks;
        required<HTMLElement>(form, "[data-reset-veer-row]").hidden = !runtime.profile.usesPersistentVeer;

        if (state.activeWatchNumber !== null) {
            select(form, "direction").value = String(state.intendedDirection ?? 0);
            input(form, "pace").value = state.activePaceKey ?? "normal";
            input(form, "activities").value = state.activeActivities.join(", ");
            input(form, "navigationAid").value = state.activeNavigationAidKey ?? "none";
            checkbox(form, "doubleBack").checked = state.activeDeliberateDoubleBack;
            checkbox(form, "continueAcross").checked = state.activeContinueAcrossBoundaries;
        }

        const scale = runtime.context.hexCenterDistance?.value
            ?? throwContextError("Spatial crawl session is missing hex-center distance.");
        if (!input(form, "effectiveDistance").value) input(form, "effectiveDistance").value = String(scale);
        if (!input(form, "expectedDistance").value) input(form, "expectedDistance").value = String(scale);
        if (!input(form, "actualDistance").value) input(form, "actualDistance").value = String(scale);

        syncNavigationVisibility();
        syncEncounterFields();
        syncHelperVisibility();
        required<HTMLElement>(form, "[data-direction-hint]").textContent = runtime.profile.directionChangesCostProgress
            ? "Changing course can consume intra-hex progress under this procedure. The runtime applies the configured cost."
            : "Direction changes do not consume additional progress under this procedure.";
    };

    const syncHelperVisibility = (): void => {
        const helpers = runtime.profile.resolutionHelpers;
        const continuous = runtime.profile.travelResolution === "ContinuousDistance";
        const travelActive = Boolean(
            helpers?.travel
            && continuous
            && runtime.profile.actualDistanceResolution === "VariableResolved");
        const navigationActive = Boolean(
            helpers?.navigation
            && navigationResolutionDue(runtime, checkbox(form, "suppressNav").checked, checkbox(form, "doubleBack").checked));
        const encounterActive = Boolean(helpers?.encounter && encounterCheckDue(runtime));

        required<HTMLElement>(form, "[data-resolution-helper]").hidden = !(travelActive || navigationActive || encounterActive);
        required<HTMLElement>(form, "[data-helper-travel]").hidden = !travelActive;
        required<HTMLElement>(form, "[data-helper-navigation]").hidden = !navigationActive;
        required<HTMLElement>(form, "[data-helper-encounter]").hidden = !encounterActive;
    };

    const syncNavigationVisibility = (): void => {
        const due = navigationResolutionDue(runtime, checkbox(form, "suppressNav").checked, checkbox(form, "doubleBack").checked);
        required<HTMLElement>(form, "[data-navigation-resolution]").hidden = !due;
        const failed = select(form, "navigationOutcome").value === "Failed";
        required<HTMLElement>(form, "[data-veer-row]").hidden = !failed;
    };

    const syncEncounterFields = (): void => {
        const outcome = select(form, "encounterOutcome").value;
        required<HTMLElement>(form, "[data-encounter-hour]").hidden = outcome === "None";
        required<HTMLElement>(form, "[data-encounter-location]").hidden = outcome !== "KeyedLocationDiscovery";
    };

    const applyGeneratedProvenance = (
        sourceName: string,
        noteName: string,
        provenance: { source: ResolutionSource; note: string | null }): void => {
        const source = select(form, sourceName);
        if (![...source.options].some(item => item.value === provenance.source)) {
            source.append(option(provenance.source, sourceLabel(provenance.source)));
        }
        source.value = provenance.source;
        input(form, noteName).value = provenance.note ?? "";
    };

    const applyHelperResult = (result: ProcedureResolutionHelperResult): void => {
        if (result.expeditionVersion !== runtime.version) {
            throw new Error("The helper result was generated for a different crawl-session version.");
        }

        if (result.travel) {
            input(form, "expectedDistance").value = String(result.travel.expectedDistance);
            input(form, "actualDistance").value = String(result.travel.actualDistance);
            applyGeneratedProvenance("travelSource", "travelNote", result.travel.provenance);
        }
        if (result.navigation) {
            select(form, "navigationOutcome").value = result.navigation.outcome;
            if (result.navigation.veerSteps !== null) {
                input(form, "veerSteps").value = String(result.navigation.veerSteps);
            }
            applyGeneratedProvenance("navigationSource", "navigationNote", result.navigation.provenance);
        }
        if (result.encounter) {
            select(form, "encounterOutcome").value = result.encounter.kind;
            if (result.encounter.occursAtHours !== null) {
                input(form, "encounterHour").value = String(result.encounter.occursAtHours);
            }
            if (result.encounter.locationId) {
                locationSelect.value = result.encounter.locationId;
            }
            if (result.encounter.note) {
                input(form, "encounterNote").value = result.encounter.note;
            }
            applyGeneratedProvenance("encounterSource", "encounterSourceNote", result.encounter.provenance);
        }

        syncNavigationVisibility();
        syncEncounterFields();
        const rollText = result.rolls
            .map(roll => roll.purpose + " " + roll.formula + " [" + roll.dice.join(", ") + "] = " + roll.total)
            .join("; ");
        resolutionHelperResult.textContent = [rollText, ...result.notes].filter(Boolean).join(" ");
    };

    const markAutomaticResultEdited = (sourceName: string, noteName: string): void => {
        const source = select(form, sourceName);
        if (source.value !== "AutomaticRoll") return;
        source.value = "DmOverride";
        const note = input(form, noteName);
        if (!note.value.trim()) note.value = "edited after automatic helper result";
    };

    const mutate = async (control: HTMLButtonElement | null, action: () => Promise<void>): Promise<void> => {
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

    checkbox(form, "suppressNav").addEventListener("change", () => {
        syncNavigationVisibility();
        syncHelperVisibility();
    });
    checkbox(form, "doubleBack").addEventListener("change", () => {
        syncNavigationVisibility();
        syncHelperVisibility();
    });
    select(form, "navigationOutcome").addEventListener("change", () => {
        markAutomaticResultEdited("navigationSource", "navigationNote");
        syncNavigationVisibility();
    });
    input(form, "veerSteps").addEventListener("input", () => markAutomaticResultEdited("navigationSource", "navigationNote"));
    select(form, "encounterOutcome").addEventListener("change", () => {
        markAutomaticResultEdited("encounterSource", "encounterSourceNote");
        syncEncounterFields();
    });
    input(form, "encounterHour").addEventListener("input", () => markAutomaticResultEdited("encounterSource", "encounterSourceNote"));
    for (const name of ["effectiveDistance", "expectedDistance", "actualDistance", "hexSteps"]) {
        input(form, name).addEventListener("input", () => markAutomaticResultEdited("travelSource", "travelNote"));
    }
    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    const editButton = required<HTMLButtonElement>(root, "[data-edit]");
    editButton.hidden = runtime.overworldId === null;
    editButton.addEventListener("click", () => {
        if (runtime.overworldId) navigate(`/worlds/${runtime.overworldId}/edit`);
    });
    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    required<HTMLButtonElement>(root, "[data-view-tracker]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}`));
    const mapButton = required<HTMLButtonElement>(root, "[data-view-map]");
    mapButton.hidden = runtime.overworldId === null;
    mapButton.addEventListener("click", () => {
        if (runtime.overworldId) navigate(`/worlds/${runtime.overworldId}/expeditions/${runtime.id}`);
    });
    required<HTMLButtonElement>(root, "[data-view-travel]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/travel`));
    required<HTMLButtonElement>(root, "[data-view-navigation]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/navigation`));
    required<HTMLButtonElement>(root, "[data-view-encounters]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/encounters`));

    resolutionHelperButton.addEventListener("click", () => {
        void mutate(resolutionHelperButton, async () => {
            resolutionHelperResult.textContent = "";
            const helpers = runtime.profile.resolutionHelpers;
            const navigationActive = Boolean(
                helpers?.navigation
                && navigationResolutionDue(runtime, checkbox(form, "suppressNav").checked, checkbox(form, "doubleBack").checked));
            const travelActive = Boolean(
                helpers?.travel
                && runtime.profile.travelResolution === "ContinuousDistance"
                && runtime.profile.actualDistanceResolution === "VariableResolved");

            const request: ProcedureResolutionHelperRequest = {
                expectedVersion: runtime.version,
                suppressesNavigationCheck: checkbox(form, "suppressNav").checked,
                deliberateDoubleBack: runtime.profile.supportsDeliberateDoubleBack && checkbox(form, "doubleBack").checked,
                navigationModifier: 0
            };
            if (travelActive) {
                request.expectedDistance = numeric(input(form, "expectedDistance"));
            }
            if (navigationActive) {
                const dc = input(form, "helperNavigationDc");
                const failureVeer = input(form, "helperFailureVeer");
                if (!dc.value.trim()) throw new Error("A DM-confirmed navigation DC is required to use the navigation helper.");
                if (!failureVeer.value.trim()) throw new Error("A DM-confirmed non-zero failure veer is required to use the navigation helper.");
                request.navigationDifficultyClass = integer(dc);
                request.navigationModifier = integer(input(form, "helperNavigationModifier"));
                request.failureVeerSteps = nonZeroInteger(failureVeer);
            }
            if (locationSelect.value) request.keyedLocationId = locationSelect.value;

            applyHelperResult(await api.resolveProcedureInputs(runtime.id, request));
        });
    });

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (advancePending) return;
        advancePending = true;
        advanceButton.disabled = true;
        advanceButton.textContent = "Applying…";
        void mutate(null, async () => {
            try {
                const continuous = runtime.profile.travelResolution === "ContinuousDistance";
                const navRequired = navigationResolutionDue(runtime, checkbox(form, "suppressNav").checked, checkbox(form, "doubleBack").checked);
                const encounterRequired = encounterCheckDue(runtime);
                const request: RuntimeAdvanceRequest = {
                    expectedVersion: runtime.version,
                    intendedDirection: integer(select(form, "direction")),
                    paceKey: input(form, "pace").value.trim() || "normal",
                    activities: input(form, "activities").value.split(",").map(value => value.trim()).filter(Boolean),
                    navigationAidKey: input(form, "navigationAid").value.trim() || "none",
                    suppressesNavigationCheck: checkbox(form, "suppressNav").checked,
                    resetsVeerAtBoundary: checkbox(form, "resetVeer").checked,
                    resolutionSource: "ManualRoll",
                    travelResolutionSource: select(form, "travelSource").value as ResolutionSource,
                    travelResolutionNote: optionalText(input(form, "travelNote")),
                    deliberateDoubleBack: runtime.profile.supportsDeliberateDoubleBack && checkbox(form, "doubleBack").checked,
                    continueAcrossBoundaries: checkbox(form, "continueAcross").checked
                };

                if (continuous && runtime.profile.actualDistanceResolution === "Fixed") request.effectiveDistance = numeric(input(form, "effectiveDistance"));
                else if (continuous) {
                    request.expectedDistance = numeric(input(form, "expectedDistance"));
                    request.actualDistance = numeric(input(form, "actualDistance"));
                } else request.hexSteps = integer(input(form, "hexSteps"));

                if (navRequired) {
                    request.navigationOutcome = select(form, "navigationOutcome").value as "Succeeded" | "Failed";
                    if (request.navigationOutcome === "Failed") request.veerSteps = nonZeroInteger(input(form, "veerSteps"));
                    request.navigationResolutionSource = select(form, "navigationSource").value as ResolutionSource;
                    request.navigationResolutionNote = optionalText(input(form, "navigationNote"));
                }

                if (encounterRequired) {
                    request.encounterOutcome = select(form, "encounterOutcome").value as RuntimeAdvanceRequest["encounterOutcome"];
                    request.encounterResolutionSource = select(form, "encounterSource").value as ResolutionSource;
                    request.encounterResolutionNote = optionalText(input(form, "encounterSourceNote"));
                    if (request.encounterOutcome !== "None") request.encounterHour = numeric(input(form, "encounterHour"));
                    if (request.encounterOutcome === "KeyedLocationDiscovery") {
                        if (!locationSelect.value) throw new Error("A keyed-location encounter requires a location.");
                        request.locationId = locationSelect.value;
                    }
                    request.encounterNote = optionalText(input(form, "encounterNote"));
                }

                if (runtime.pauseReason === "LostRecognitionRequired") {
                    request.recognizedLost = checkbox(form, "recognizedLost").checked;
                    request.reorient = checkbox(form, "reorient").checked;
                    request.boundaryResolutionSource = select(form, "boundarySource").value as ResolutionSource;
                    request.boundaryResolutionNote = optionalText(input(form, "boundaryNote"));
                }

                request.dmOverrideNote = optionalText(input(form, "dmOverrideNote"));
                apply(await api.advanceExpedition(runtime.id, request));
            } finally {
                advancePending = false;
                if (!disposed) {
                    advanceButton.disabled = false;
                    advanceButton.textContent = watchActionLabel(runtime);
                }
            }
        });
    });

    apply(runtime);
    return () => {
        disposed = true;
        map?.dispose();
    };

    function subjectLabel(id: string): string {
        if (!world) return id;
        return world.locations.find(item => item.id === id)?.name
            ?? world.features.find(item => item.id === id)?.name
            ?? id;
    }
}

function spatialState(runtime: ExpeditionDetail): SpatialRuntimeExpedition {
    if (!runtime.expedition.isSpatial) {
        throw new Error("This operation requires a spatial crawl session.");
    }
    return runtime.expedition;
}

function throwContextError(message: string): never {
    throw new Error(message);
}

function renderNonSpatialTracker(
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
                    <button type="button" data-watch>Watch / time</button>
                    <button type="button" data-encounters>Encounter cadence</button>
                </nav>
            </header>
            <div class="hc-columns">
                <section class="hc-panel">
                    <h2>Procedure state</h2>
                    <div class="hc-status-grid">
                        <div><strong>Day</strong><span>${state.currentDay}</span></div>
                        <div><strong>Watch</strong><span>${state.activeWatchNumber === null ? `Ready for watch ${state.completedWatches + 1}` : `Watch ${state.activeWatchNumber}`}</span></div>
                        <div><strong>Watch length</strong><span>${formatHours(state.activeWatchTotalHours ?? runtime.profile.watchHours)}</span></div>
                        <div><strong>Watch elapsed</strong><span>${formatHours(state.activeWatchElapsedHours ?? 0)}</span></div>
                        <div><strong>Watch remaining</strong><span>${formatHours(state.activeWatchRemainingHours ?? runtime.profile.watchHours)}</span></div>
                        <div><strong>Completed watches</strong><span>${state.completedWatches}</span></div>
                        <div><strong>Total elapsed</strong><span>${formatHours(state.elapsedTravelHours)}</span></div>
                        <div><strong>Context</strong><span>Non-spatial</span></div>
                    </div>
                    <p class="hc-hint">This session intentionally has no hex coordinates, distance scale, world position, or Overworld. Spatial travel and navigation tools do not apply.</p>
                </section>
                <section class="hc-panel">
                    <h2>Recent procedure history</h2>
                    <ol class="hc-history" data-history></ol>
                    <p class="hc-hint">${escapeHtml(runtime.profile.name)} · encounters ${escapeHtml(prettyEnum(runtime.profile.encounterCadence))}</p>
                </section>
            </div>
        </section>`;

    const history = required<HTMLOListElement>(root, "[data-history]");
    const recent = [...runtime.history].reverse().slice(0, 30);
    if (recent.length === 0) {
        const item = document.createElement("li");
        item.textContent = "No procedure events yet.";
        history.append(item);
    } else {
        for (const event of recent) {
            const item = document.createElement("li");
            item.textContent = `#${event.sequence} · ${formatHours(event.expeditionElapsedHours)} · ${event.message}`;
            history.append(item);
        }
    }

    required<HTMLButtonElement>(root, "[data-home]").addEventListener("click", () => navigate("/"));
    required<HTMLButtonElement>(root, "[data-watch]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/travel`));
    required<HTMLButtonElement>(root, "[data-encounters]").addEventListener("click", () => navigate(`/expeditions/${runtime.id}/encounters`));
    return () => {};
}

function escapeHtml(value: string): string {
    return value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
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

function paragraph(text: string): HTMLParagraphElement {
    const item = document.createElement("p");
    item.className = "hc-hint";
    item.textContent = text;
    return item;
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

function sameHex(left: { q: number; r: number }, right: { q: number; r: number }): boolean {
    return left.q === right.q && left.r === right.r;
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

function nonZeroInteger(element: HTMLInputElement): number {
    const value = integer(element);
    if (value === 0) throw new Error(`${element.name} must be a non-zero integer.`);
    return value;
}

function optionalText(element: HTMLInputElement): string | undefined {
    const value = element.value.trim();
    return value || undefined;
}
