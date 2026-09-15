import { HexCrawlApi } from "./api";
import { CanvasMapRenderer } from "./canvas-renderer";
import { worldToHex } from "./hex-math";
import { RenderLifecycle } from "./render-lifecycle";
import {
    alexandrianActualDistance,
    directionLabel,
    discoveredSubjectIds,
    formatDistance,
    formatHours
} from "./runtime-view";
import { ensureStyles } from "./styles";
import { deriveToolRoute } from "./tool-route";
import type { DemoWorld, RuntimeAdvanceRequest, RuntimeProfile, RuntimeState } from "./types";
import { Viewport } from "./viewport";

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) throw new Error("Hex Crawl requires #tool-root.");

ensureStyles();
void boot(root);

async function boot(rootElement: HTMLElement): Promise<void> {
    const shell = renderShell(rootElement);
    const viewport = new Viewport();
    let world: DemoWorld | null = null;
    let runtime: RuntimeState | null = null;
    let profiles: RuntimeProfile[] = [];
    let orientation: "pointy" | "flat" = "pointy";
    let scale = 12;
    let unit: "mi" | "km" = "mi";
    const lifecycle = new RenderLifecycle();
    const renderer = new CanvasMapRenderer(shell.canvas, viewport, () => world);
    lifecycle.register("map", () => renderer.render());

    const { api, context } = await HexCrawlApi.create(rootElement);
    shell.hostMode.textContent = context ? `Embedded: ${context.toolSlug}` : "Standalone";

    const showError = (error: unknown): void => {
        shell.error.hidden = false;
        shell.error.textContent = error instanceof Error ? error.message : String(error);
    };
    const clearError = (): void => {
        shell.error.hidden = true;
        shell.error.textContent = "";
    };

    const refreshWorld = async (): Promise<void> => {
        clearError();
        try {
            world = await api.getDemoWorld(orientation, scale, unit);
            shell.scaleValue.textContent = `${world.grid.neighborCenterDistance.value} ${world.grid.neighborCenterDistance.unit.symbol} center-to-center`;
            updateCoordinate();
            renderDiscoveryList();
            lifecycle.requestRender();
        } catch (error) {
            showError(error);
        }
    };

    const applyRuntime = (next: RuntimeState): void => {
        runtime = next;
        renderer.expeditionHex = next.expedition.currentHex;
        renderer.discoveredSubjectIds = discoveredSubjectIds(next);
        shell.profile.value = next.profile.key;
        shell.runtimeGrid.textContent = formatDistance(next.hexCenterDistance);
        shell.currentHex.textContent = `${next.expedition.currentHex.q}, ${next.expedition.currentHex.r}`;
        shell.intendedCourse.textContent = directionLabel(next.expedition.intendedDirection);
        shell.actualCourse.textContent = directionLabel(next.expedition.actualDirection);
        shell.lostState.textContent = next.expedition.isLost
            ? `Lost · veer ${next.expedition.veerSteps} (${next.expedition.veerDegrees}°)`
            : "Oriented";
        shell.distanceTraveled.textContent = formatDistance(next.expedition.distanceTraveled);
        shell.hexProgress.textContent = next.expedition.exitRequirement
            ? `${formatDistance(next.expedition.hexProgress)} / ${formatDistance(next.expedition.exitRequirement)}`
            : formatDistance(next.expedition.hexProgress);
        shell.watchState.textContent = next.expedition.activeWatchNumber === null
            ? `${next.expedition.completedWatches} completed`
            : `Watch ${next.expedition.activeWatchNumber} active`;
        shell.remainingWatch.textContent = formatHours(next.remainingWatchHours);
        shell.pauseReason.textContent = next.pauseReason ?? "—";
        shell.elapsedTravel.textContent = formatHours(next.expedition.elapsedTravelHours);

        const newWatch = next.expedition.activeWatchNumber === null;
        shell.navigationInputs.hidden = !(newWatch && next.profile.usesNavigationChecks);
        shell.encounterInputs.hidden = !(newWatch && next.profile.encounterCadence !== "None");
        shell.continuousInputs.hidden = next.profile.travelResolution !== "ContinuousDistance";
        shell.hexStepInputs.hidden = next.profile.travelResolution !== "HexSteps";
        shell.lostDecision.hidden = next.pauseReason !== "LostRecognitionRequired";
        shell.variableDistanceHelper.hidden = next.profile.actualDistanceResolution !== "VariableResolved";

        renderDiscoveryList();
        renderHistory();
        lifecycle.requestRender();
    };

    const renderDiscoveryList = (): void => {
        shell.discoveryList.replaceChildren();
        if (!world) return;
        const discovered = discoveredSubjectIds(runtime);
        const subjects = [
            ...world.locations.map(item => ({ id: item.id, name: item.name, type: "Location" as const })),
            ...world.features.map(item => ({ id: item.id, name: item.name, type: "Feature" as const }))
        ];
        for (const subject of subjects) {
            const row = document.createElement("div");
            row.className = "hc-discovery-row";
            const label = document.createElement("span");
            label.textContent = `${subject.name} · ${subject.type}`;
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = discovered.has(subject.id) ? "Discovered" : "Mark discovered";
            button.disabled = discovered.has(subject.id);
            button.addEventListener("click", async () => {
                clearError();
                try {
                    applyRuntime(await api.discover(subject.id, subject.type));
                } catch (error) {
                    showError(error);
                }
            });
            row.append(label, button);
            shell.discoveryList.append(row);
        }
    };

    const renderHistory = (): void => {
        shell.history.replaceChildren();
        if (!runtime) return;
        for (const runtimeEvent of runtime.history.slice(-40).reverse()) {
            const item = document.createElement("li");
            const title = document.createElement("strong");
            title.textContent = `#${runtimeEvent.sequence} ${runtimeEvent.kind}`;
            const detail = document.createElement("span");
            detail.textContent = ` ${formatHours(runtimeEvent.expeditionElapsedHours)} · hex ${runtimeEvent.hex.q},${runtimeEvent.hex.r} · ${runtimeEvent.message}`;
            item.append(title, detail);
            shell.history.append(item);
        }
    };

    const markMapSettingsChanged = (): void => {
        shell.mapSettingsNote.textContent = "Map settings changed. Reset the expedition to make the runtime use this grid scale/orientation.";
    };

    shell.orientation.addEventListener("change", () => {
        orientation = shell.orientation.value === "flat" ? "flat" : "pointy";
        markMapSettingsChanged();
        void refreshWorld();
    });
    shell.unit.addEventListener("change", () => {
        unit = shell.unit.value === "km" ? "km" : "mi";
        markMapSettingsChanged();
        void refreshWorld();
    });
    shell.scale.addEventListener("change", () => {
        const parsed = Number(shell.scale.value);
        scale = Number.isFinite(parsed) && parsed > 0 ? parsed : 12;
        shell.scale.value = String(scale);
        markMapSettingsChanged();
        void refreshWorld();
    });
    shell.resetView.addEventListener("click", () => {
        viewport.center = { x: 0, y: 0 };
        viewport.zoom = 56;
        lifecycle.requestRender();
    });

    shell.resetRuntime.addEventListener("click", async () => {
        clearError();
        try {
            applyRuntime(await api.resetRuntime(shell.profile.value, orientation, scale, unit));
            shell.expectedDistance.value = String(scale);
            shell.actualDistance.value = String(scale);
            shell.mapSettingsNote.textContent = "Runtime grid matches the current map settings.";
        } catch (error) {
            showError(error);
        }
    });

    shell.autoDistance.addEventListener("click", () => {
        const expected = numericValue(shell.expectedDistance, scale);
        const first = randomDie(6);
        const second = randomDie(6);
        shell.actualDistance.value = String(alexandrianActualDistance(expected, first, second));
        shell.resolutionSource.value = "AutomaticRoll";
        shell.rollInfo.textContent = `Distance roll: ${first} + ${second}; actual distance prefilled.`;
    });

    shell.autoNavigation.addEventListener("click", () => {
        const die = randomDie(20);
        const modifier = numericValue(shell.navigationModifier, 0);
        const dc = numericValue(shell.navigationDc, 10);
        const total = die + modifier;
        shell.navigationOutcome.value = total >= dc ? "success" : "failure";
        shell.resolutionSource.value = "AutomaticRoll";
        shell.rollInfo.textContent = `Navigation roll: ${die} ${modifier >= 0 ? "+" : ""}${modifier} = ${total} vs DC ${dc}.`;
    });

    shell.advance.addEventListener("click", async () => {
        if (!runtime) return;
        clearError();
        try {
            const newWatch = runtime.expedition.activeWatchNumber === null;
            const navigationAid = shell.navigationAid.value;
            const request: RuntimeAdvanceRequest = {
                intendedDirection: Number(shell.direction.value),
                paceKey: shell.pace.value,
                activities: Array.from(rootElement.querySelectorAll<HTMLInputElement>("[data-runtime-activity]:checked"), item => item.value),
                navigationAidKey: navigationAid,
                suppressesNavigationCheck: navigationAid === "route",
                resetsVeerAtBoundary: navigationAid === "route",
                resolutionSource: shell.resolutionSource.value as RuntimeAdvanceRequest["resolutionSource"],
                deliberateDoubleBack: shell.doubleBack.checked,
                continueAcrossBoundaries: shell.continueAcross.checked
            };

            if (runtime.profile.travelResolution === "ContinuousDistance") {
                request.expectedDistance = numericValue(shell.expectedDistance, 0);
                request.actualDistance = numericValue(shell.actualDistance, 0);
            } else {
                request.hexSteps = Math.max(0, Math.trunc(numericValue(shell.hexSteps, 0)));
            }

            if (newWatch && runtime.profile.usesNavigationChecks && navigationAid !== "route" && !shell.doubleBack.checked) {
                request.navigationOutcome = shell.navigationOutcome.value as "success" | "failure";
                if (request.navigationOutcome === "failure") {
                    request.veerSteps = nonZeroInteger(shell.veerSteps, 1);
                }
            }

            if (newWatch && runtime.profile.encounterCadence !== "None") {
                request.encounterOutcome = shell.encounterOutcome.value as RuntimeAdvanceRequest["encounterOutcome"];
                if (request.encounterOutcome !== "none") {
                    request.encounterHour = numericValue(shell.encounterHour, runtime.profile.watchHours / 2);
                    request.encounterNote = shell.encounterNote.value.trim() || undefined;
                    if (request.encounterOutcome === "location" && shell.encounterLocation.value) {
                        request.locationId = shell.encounterLocation.value;
                    }
                }
            }

            if (runtime.pauseReason === "LostRecognitionRequired") {
                request.recognizedLost = shell.recognizedLost.checked;
                request.reorient = shell.reorient.checked;
            }

            const overrideNote = shell.overrideNote.value.trim();
            if (overrideNote) request.dmOverrideNote = overrideNote;
            applyRuntime(await api.advanceRuntime(request));
        } catch (error) {
            showError(error);
        }
    });

    const resizeObserver = new ResizeObserver(() => lifecycle.requestRender());
    resizeObserver.observe(shell.canvas);

    let pointerId: number | null = null;
    let lastX = 0;
    let lastY = 0;
    let moved = false;

    shell.canvas.addEventListener("pointerdown", event => {
        pointerId = event.pointerId;
        lastX = event.clientX;
        lastY = event.clientY;
        moved = false;
        shell.canvas.setPointerCapture(event.pointerId);
        shell.canvas.dataset.dragging = "true";
    });

    shell.canvas.addEventListener("pointermove", event => {
        updateCoordinate(event);
        if (pointerId !== event.pointerId) return;
        const dx = event.clientX - lastX;
        const dy = event.clientY - lastY;
        if (Math.abs(dx) + Math.abs(dy) > 1) moved = true;
        viewport.panByPixels(dx, dy);
        lastX = event.clientX;
        lastY = event.clientY;
        lifecycle.requestRender();
    });

    shell.canvas.addEventListener("pointerup", event => {
        if (pointerId !== event.pointerId) return;
        shell.canvas.releasePointerCapture(event.pointerId);
        shell.canvas.dataset.dragging = "false";
        pointerId = null;
        if (!moved && world) {
            const point = eventToWorld(event);
            renderer.selectedHex = worldToHex(world.grid, point);
            shell.selected.textContent = `${renderer.selectedHex.q}, ${renderer.selectedHex.r}`;
            lifecycle.requestRender();
        }
    });

    shell.canvas.addEventListener("pointerleave", () => {
        if (pointerId === null) shell.hover.textContent = "—";
    });

    shell.canvas.addEventListener("wheel", event => {
        event.preventDefault();
        const rect = shell.canvas.getBoundingClientRect();
        viewport.zoomAt(event.deltaY < 0 ? 1.12 : 1 / 1.12, event.clientX - rect.left, event.clientY - rect.top, rect.width, rect.height);
        lifecycle.requestRender();
    }, { passive: false });

    window.addEventListener("popstate", () => {
        shell.route.textContent = routeFor(rootElement, context?.toolBasePath ?? "/");
    });

    function eventToWorld(event: PointerEvent) {
        const rect = shell.canvas.getBoundingClientRect();
        return viewport.screenToWorld(event.clientX - rect.left, event.clientY - rect.top, rect.width, rect.height);
    }

    function updateCoordinate(event?: PointerEvent): void {
        if (!world) return;
        const rect = shell.canvas.getBoundingClientRect();
        const point = event
            ? viewport.screenToWorld(event.clientX - rect.left, event.clientY - rect.top, rect.width, rect.height)
            : viewport.center;
        const hex = worldToHex(world.grid, point);
        shell.hover.textContent = `${hex.q}, ${hex.r}`;
    }

    shell.route.textContent = routeFor(rootElement, context?.toolBasePath ?? "/");
    await refreshWorld();
    try {
        [profiles, runtime] = await Promise.all([api.getRuntimeProfiles(), api.getRuntime()]);
        shell.profile.replaceChildren(...profiles.map(profile => {
            const option = document.createElement("option");
            option.value = profile.key;
            option.textContent = profile.name;
            return option;
        }));
        applyRuntime(runtime);
        shell.expectedDistance.value = String(runtime.hexCenterDistance.value);
        shell.actualDistance.value = String(runtime.hexCenterDistance.value);
        shell.encounterLocation.replaceChildren(...(world?.locations ?? []).map(location => {
            const option = document.createElement("option");
            option.value = location.id;
            option.textContent = location.name;
            return option;
        }));
    } catch (error) {
        showError(error);
    }
}

function renderShell(rootElement: HTMLElement) {
    rootElement.replaceChildren();
    const app = document.createElement("section");
    app.className = "hc-app";
    app.innerHTML = `
        <div class="hc-toolbar">
            <label class="hc-control">Orientation
                <select data-role="orientation"><option value="pointy">Pointy top</option><option value="flat">Flat top</option></select>
            </label>
            <label class="hc-control">Scale
                <input data-role="scale" type="number" min="0.01" step="0.5" value="12">
            </label>
            <label class="hc-control">Unit
                <select data-role="unit"><option value="mi">Miles</option><option value="km">Kilometers</option></select>
            </label>
            <button data-role="reset-view" type="button">Reset view</button>
            <span data-role="scale-value"></span>
            <span class="hc-map-note" data-role="map-settings-note">Runtime grid starts at the default 12 mi scale.</span>
        </div>
        <div class="hc-workspace">
            <div class="hc-map-column">
                <div class="hc-stage">
                    <canvas class="hc-canvas" data-role="canvas"></canvas>
                    <div class="hc-overlay">Hex under cursor: <strong data-role="hover">—</strong><br>Selected: <strong data-role="selected">—</strong></div>
                </div>
                <div class="hc-status">
                    <span>Host: <strong data-role="host-mode">Loading</strong></span>
                    <span>Route: <strong data-role="route">/</strong></span>
                    <span>Pan: drag · Zoom: wheel · Select: click</span>
                </div>
            </div>
            <aside class="hc-runtime">
                <div class="hc-runtime-heading">
                    <div><strong>Expedition runtime</strong><div class="hc-muted">Ephemeral DM demonstrator</div></div>
                    <label class="hc-control">Procedure<select data-role="profile"></select></label>
                    <button type="button" data-role="reset-runtime">Start / reset expedition</button>
                </div>
                <dl class="hc-runtime-state">
                    <div><dt>Runtime grid</dt><dd data-role="runtime-grid">—</dd></div>
                    <div><dt>Current hex</dt><dd data-role="current-hex">—</dd></div>
                    <div><dt>Intended</dt><dd data-role="intended-course">—</dd></div>
                    <div><dt>Actual</dt><dd data-role="actual-course">—</dd></div>
                    <div><dt>Navigation</dt><dd data-role="lost-state">—</dd></div>
                    <div><dt>Distance</dt><dd data-role="distance-traveled">—</dd></div>
                    <div><dt>Hex progress</dt><dd data-role="hex-progress">—</dd></div>
                    <div><dt>Watch</dt><dd data-role="watch-state">—</dd></div>
                    <div><dt>Remaining</dt><dd data-role="remaining-watch">—</dd></div>
                    <div><dt>Elapsed</dt><dd data-role="elapsed-travel">—</dd></div>
                    <div><dt>Pause</dt><dd data-role="pause-reason">—</dd></div>
                </dl>
                <div class="hc-runtime-form">
                    <label class="hc-control">Direction
                        <select data-role="direction">${[0,1,2,3,4,5].map(value => `<option value="${value}">Direction ${value}</option>`).join("")}</select>
                    </label>
                    <label class="hc-control">Pace
                        <select data-role="pace"><option value="normal">Normal</option><option value="cautious">Cautious</option><option value="fast">Fast / hustle</option></select>
                    </label>
                    <fieldset class="hc-inline-fieldset"><legend>Activities</legend>
                        <label><input data-runtime-activity type="checkbox" value="exploration"> Exploration</label>
                        <label><input data-runtime-activity type="checkbox" value="foraging"> Foraging</label>
                    </fieldset>
                    <label class="hc-control">Navigation aid
                        <select data-role="navigation-aid"><option value="none">None</option><option value="compass">Compass / bearing</option><option value="route">Road / trail / landmark</option></select>
                    </label>
                    <div class="hc-runtime-subgrid" data-role="continuous-inputs">
                        <label class="hc-control">Expected distance<input data-role="expected-distance" type="number" min="0" step="0.1" value="12"></label>
                        <label class="hc-control">Actual distance<input data-role="actual-distance" type="number" min="0" step="0.1" value="12"></label>
                        <button type="button" data-role="auto-distance">Roll Alexandrian 2d6 distance</button>
                        <span data-role="variable-distance-helper" class="hc-muted">Uses (2d6 + 3) × 10% of expected distance.</span>
                    </div>
                    <div class="hc-runtime-subgrid" data-role="hex-step-inputs" hidden>
                        <label class="hc-control">Resolved hex steps<input data-role="hex-steps" type="number" min="0" step="1" value="1"></label>
                    </div>
                    <div class="hc-runtime-subgrid" data-role="navigation-inputs">
                        <label class="hc-control">Navigation outcome<select data-role="navigation-outcome"><option value="success">Success</option><option value="failure">Failure</option></select></label>
                        <label class="hc-control">Resolved veer steps<input data-role="veer-steps" type="number" step="1" value="1"></label>
                        <label class="hc-control">Helper modifier<input data-role="navigation-modifier" type="number" step="1" value="0"></label>
                        <label class="hc-control">Helper DC<input data-role="navigation-dc" type="number" step="1" value="10"></label>
                        <button type="button" data-role="auto-navigation">Roll d20 navigation helper</button>
                    </div>
                    <div class="hc-runtime-subgrid" data-role="encounter-inputs">
                        <label class="hc-control">Encounter result<select data-role="encounter-outcome"><option value="none">No encounter</option><option value="wandering">Wandering encounter</option><option value="location">Keyed location discovery</option><option value="manual">Manual/custom event</option></select></label>
                        <label class="hc-control">Encounter hour<input data-role="encounter-hour" type="number" min="0" step="0.5" value="2"></label>
                        <label class="hc-control">Keyed location<select data-role="encounter-location"></select></label>
                        <label class="hc-control hc-span-2">Encounter note<input data-role="encounter-note" type="text"></label>
                    </div>
                    <div class="hc-runtime-subgrid" data-role="lost-decision" hidden>
                        <label><input data-role="recognized-lost" type="checkbox"> Party recognizes it is lost</label>
                        <label><input data-role="reorient" type="checkbox"> Reorient course</label>
                    </div>
                    <label class="hc-control">Resolution source
                        <select data-role="resolution-source"><option value="ManualRoll">Manual / physical dice</option><option value="AutomaticRoll">Tool helper</option><option value="ExternalSystem">External system</option><option value="DmOverride">DM override</option></select>
                    </label>
                    <label class="hc-control hc-span-2">DM override / ruling note<input data-role="override-note" type="text"></label>
                    <label><input data-role="continue-across" type="checkbox"> Continue across boundaries without condition review</label>
                    <label><input data-role="double-back" type="checkbox"> Deliberately double back</label>
                    <button class="hc-advance" type="button" data-role="advance">Advance watch / segment</button>
                    <div class="hc-muted hc-span-2" data-role="roll-info"></div>
                </div>
                <details open><summary>Discoveries</summary><div class="hc-discovery-list" data-role="discovery-list"></div></details>
                <details open><summary>Runtime history</summary><ol class="hc-history" data-role="history"></ol></details>
            </aside>
        </div>
        <div class="hc-error" data-role="error" hidden></div>
    `;
    rootElement.append(app);

    const required = <T extends Element>(selector: string): T => {
        const element = app.querySelector<T>(selector);
        if (!element) throw new Error(`Missing Hex Crawl UI element ${selector}.`);
        return element;
    };

    return {
        canvas: required<HTMLCanvasElement>("[data-role=canvas]"),
        orientation: required<HTMLSelectElement>("[data-role=orientation]"),
        scale: required<HTMLInputElement>("[data-role=scale]"),
        unit: required<HTMLSelectElement>("[data-role=unit]"),
        resetView: required<HTMLButtonElement>("[data-role=reset-view]"),
        scaleValue: required<HTMLElement>("[data-role=scale-value]"),
        mapSettingsNote: required<HTMLElement>("[data-role=map-settings-note]"),
        hover: required<HTMLElement>("[data-role=hover]"),
        selected: required<HTMLElement>("[data-role=selected]"),
        hostMode: required<HTMLElement>("[data-role=host-mode]"),
        route: required<HTMLElement>("[data-role=route]"),
        error: required<HTMLElement>("[data-role=error]"),
        profile: required<HTMLSelectElement>("[data-role=profile]"),
        resetRuntime: required<HTMLButtonElement>("[data-role=reset-runtime]"),
        runtimeGrid: required<HTMLElement>("[data-role=runtime-grid]"),
        currentHex: required<HTMLElement>("[data-role=current-hex]"),
        intendedCourse: required<HTMLElement>("[data-role=intended-course]"),
        actualCourse: required<HTMLElement>("[data-role=actual-course]"),
        lostState: required<HTMLElement>("[data-role=lost-state]"),
        distanceTraveled: required<HTMLElement>("[data-role=distance-traveled]"),
        hexProgress: required<HTMLElement>("[data-role=hex-progress]"),
        watchState: required<HTMLElement>("[data-role=watch-state]"),
        remainingWatch: required<HTMLElement>("[data-role=remaining-watch]"),
        elapsedTravel: required<HTMLElement>("[data-role=elapsed-travel]"),
        pauseReason: required<HTMLElement>("[data-role=pause-reason]"),
        direction: required<HTMLSelectElement>("[data-role=direction]"),
        pace: required<HTMLSelectElement>("[data-role=pace]"),
        navigationAid: required<HTMLSelectElement>("[data-role=navigation-aid]"),
        continuousInputs: required<HTMLElement>("[data-role=continuous-inputs]"),
        expectedDistance: required<HTMLInputElement>("[data-role=expected-distance]"),
        actualDistance: required<HTMLInputElement>("[data-role=actual-distance]"),
        autoDistance: required<HTMLButtonElement>("[data-role=auto-distance]"),
        variableDistanceHelper: required<HTMLElement>("[data-role=variable-distance-helper]"),
        hexStepInputs: required<HTMLElement>("[data-role=hex-step-inputs]"),
        hexSteps: required<HTMLInputElement>("[data-role=hex-steps]"),
        navigationInputs: required<HTMLElement>("[data-role=navigation-inputs]"),
        navigationOutcome: required<HTMLSelectElement>("[data-role=navigation-outcome]"),
        veerSteps: required<HTMLInputElement>("[data-role=veer-steps]"),
        navigationModifier: required<HTMLInputElement>("[data-role=navigation-modifier]"),
        navigationDc: required<HTMLInputElement>("[data-role=navigation-dc]"),
        autoNavigation: required<HTMLButtonElement>("[data-role=auto-navigation]"),
        encounterInputs: required<HTMLElement>("[data-role=encounter-inputs]"),
        encounterOutcome: required<HTMLSelectElement>("[data-role=encounter-outcome]"),
        encounterHour: required<HTMLInputElement>("[data-role=encounter-hour]"),
        encounterLocation: required<HTMLSelectElement>("[data-role=encounter-location]"),
        encounterNote: required<HTMLInputElement>("[data-role=encounter-note]"),
        lostDecision: required<HTMLElement>("[data-role=lost-decision]"),
        recognizedLost: required<HTMLInputElement>("[data-role=recognized-lost]"),
        reorient: required<HTMLInputElement>("[data-role=reorient]"),
        resolutionSource: required<HTMLSelectElement>("[data-role=resolution-source]"),
        overrideNote: required<HTMLInputElement>("[data-role=override-note]"),
        continueAcross: required<HTMLInputElement>("[data-role=continue-across]"),
        doubleBack: required<HTMLInputElement>("[data-role=double-back]"),
        advance: required<HTMLButtonElement>("[data-role=advance]"),
        rollInfo: required<HTMLElement>("[data-role=roll-info]"),
        discoveryList: required<HTMLElement>("[data-role=discovery-list]"),
        history: required<HTMLOListElement>("[data-role=history]")
    };
}

function numericValue(input: HTMLInputElement, fallback: number): number {
    const value = Number(input.value);
    return Number.isFinite(value) ? value : fallback;
}

function nonZeroInteger(input: HTMLInputElement, fallback: number): number {
    const value = Math.trunc(numericValue(input, fallback));
    return value === 0 ? fallback : value;
}

function randomDie(sides: number): number {
    const buffer = new Uint32Array(1);
    crypto.getRandomValues(buffer);
    return (buffer[0]! % sides) + 1;
}

function routeFor(rootElement: HTMLElement, basePath: string): string {
    return deriveToolRoute(basePath, window.location.pathname, rootElement.dataset.toolRoute);
}
