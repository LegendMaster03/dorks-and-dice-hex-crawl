import type { HexCrawlApi } from "./api";
import { MapSurface } from "./map-surface";
import { discoveredSubjectIds, formatDistance, formatHours } from "./runtime-view";
import type { ExpeditionDetail, Overworld, RuntimeAdvanceRequest } from "./types";

export async function renderExpedition(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    expeditionId: string,
    navigate: (route: string) => void): Promise<() => void> {
    let world: Overworld = await api.getOverworld(worldId);
    let runtime: ExpeditionDetail = await api.getExpedition(expeditionId);

    root.innerHTML = `
        <section class="hc-page hc-workspace">
            <header class="hc-page-header"><div><h1 data-title></h1><p>Expedition runtime · persistent version <span data-version></span></p></div>
                <nav><button type="button" data-edit>Authoring</button><button type="button" data-worlds>Worlds</button></nav></header>
            <div class="hc-error" data-error hidden></div>
            <div class="hc-workspace-grid">
                <div class="hc-map-panel"><div class="hc-map-host" data-map></div>
                    <div class="hc-status-grid" data-status></div>
                    <details open><summary>History</summary><ol class="hc-history" data-history></ol></details>
                </div>
                <aside class="hc-sidebar">
                    <details open><summary>Advance travel</summary><form class="hc-form" data-advance>
                        <label>Direction (0–5) <input name="direction" type="number" min="0" max="5" step="1" value="0"></label>
                        <label>Pace key <input name="pace" value="normal"></label>
                        <label>Activities, comma separated <input name="activities"></label>
                        <label>Navigation aid <input name="navigationAid" value="none"></label>
                        <label><input name="suppressNav" type="checkbox"> Navigation check suppressed</label>
                        <label><input name="resetVeer" type="checkbox"> Reset veer at boundary</label>
                        <div data-distance-fields><label>Expected distance <input name="expectedDistance" type="number" min="0" step="any"></label><label>Actual distance <input name="actualDistance" type="number" min="0" step="any"></label></div>
                        <div data-step-fields><label>Hex steps <input name="hexSteps" type="number" min="0" step="1" value="1"></label></div>
                        <label>Resolution source <select name="resolutionSource"><option>ManualRoll</option><option>AutomaticRoll</option><option>ExternalSystem</option><option>ProcedureDefault</option><option>DmOverride</option></select></label>
                        <div data-nav-fields><label>Navigation result <select name="navigationOutcome"><option value="Succeeded">Succeeded</option><option value="Failed">Failed</option></select></label><label>Veer steps on failure <input name="veerSteps" type="number" step="1" value="1"></label></div>
                        <div data-encounter-fields><label>Encounter <select name="encounterOutcome"><option value="None">None</option><option value="WanderingEncounter">Wandering</option><option value="KeyedLocationDiscovery">Keyed location</option><option value="ManualCustom">Manual/custom</option></select></label><label>Encounter hour <input name="encounterHour" type="number" min="0" step="any"></label><label>Location <select name="locationId"><option value="">—</option></select></label><label>Encounter note <input name="encounterNote"></label></div>
                        <label><input name="continueAcross" type="checkbox"> Continue across hex boundaries</label>
                        <label><input name="doubleBack" type="checkbox"> Deliberate double-back</label>
                        <div data-lost-decision><label><input name="recognizedLost" type="checkbox"> Party recognized it was lost</label><label><input name="reorient" type="checkbox"> Reorient</label></div>
                        <label>DM override note <input name="dmOverrideNote"></label>
                        <button type="submit">Advance expedition</button>
                    </form></details>
                    <details open><summary>Manual discovery</summary><div data-discovery></div></details>
                </aside>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const form = required<HTMLFormElement>(root, "[data-advance]");
    const map = new MapSurface(required(root, "[data-map]"), () => world);
    const showError = (value: unknown): void => {
        error.hidden = false;
        error.textContent = value instanceof Error ? value.message : String(value);
    };

    const apply = (next: ExpeditionDetail): void => {
        runtime = next;
        required<HTMLElement>(root, "[data-title]").textContent = `${world.name}: ${next.name}`;
        required<HTMLElement>(root, "[data-version]").textContent = String(next.version);
        map.renderer.expeditionHex = next.expedition.currentHex;
        map.renderer.discoveredSubjectIds = discoveredSubjectIds(next);
        map.requestRender();
        renderStatus();
        renderHistory();
        renderDiscovery();
        syncProcedureInputs();
    };

    const renderStatus = (): void => {
        const state = runtime.expedition;
        const status = required<HTMLElement>(root, "[data-status]");
        status.replaceChildren(
            statusCell("Hex", `${state.currentHex.q}, ${state.currentHex.r}`),
            statusCell("Course", `${state.intendedDirection ?? "—"} → ${state.actualDirection ?? "—"}`),
            statusCell("Navigation", state.isLost ? `Lost · veer ${state.veerSteps}` : "Oriented"),
            statusCell("Distance", formatDistance(state.distanceTraveled)),
            statusCell("Hex progress", state.exitRequirement ? `${formatDistance(state.hexProgress)} / ${formatDistance(state.exitRequirement)}` : formatDistance(state.hexProgress)),
            statusCell("Travel time", formatHours(state.elapsedTravelHours)),
            statusCell("Watch", state.activeWatchNumber === null ? `${state.completedWatches} complete` : `Watch ${state.activeWatchNumber} active`),
            statusCell("Pause", runtime.pauseReason ?? "—"),
            statusCell("Remaining watch", formatHours(runtime.remainingWatchHours))
        );
    };

    const renderHistory = (): void => {
        const host = required<HTMLOListElement>(root, "[data-history]");
        host.replaceChildren();
        for (const event of [...runtime.history].reverse()) {
            const item = document.createElement("li");
            item.textContent = `#${event.sequence} ${event.kind} · ${event.message}`;
            host.append(item);
        }
    };

    const renderDiscovery = (): void => {
        const host = required<HTMLElement>(root, "[data-discovery]");
        host.replaceChildren();
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
            button.addEventListener("click", () => void mutate(async () =>
                apply(await api.discover(runtime.id, runtime.version, subject.id, subject.type))));
            row.append(label, button);
            host.append(row);
        }
    };

    const syncProcedureInputs = (): void => {
        const continuous = runtime.profile.travelResolution === "ContinuousDistance";
        required<HTMLElement>(root, "[data-distance-fields]").hidden = !continuous;
        required<HTMLElement>(root, "[data-step-fields]").hidden = continuous;
        const newWatch = runtime.expedition.activeWatchNumber === null;
        required<HTMLElement>(root, "[data-nav-fields]").hidden = !(newWatch && runtime.profile.usesNavigationChecks);
        required<HTMLElement>(root, "[data-encounter-fields]").hidden = !(newWatch && runtime.profile.encounterCadence !== "None");
        required<HTMLElement>(root, "[data-lost-decision]").hidden = runtime.pauseReason !== "LostRecognitionRequired";
        const scale = world.grid.neighborCenterDistance.value;
        if (!input(form, "expectedDistance").value) input(form, "expectedDistance").value = String(scale);
        if (!input(form, "actualDistance").value) input(form, "actualDistance").value = String(scale);
    };

    const mutate = async (action: () => Promise<void>): Promise<void> => {
        error.hidden = true;
        try { await action(); } catch (value) { showError(value); }
    };

    required<HTMLButtonElement>(root, "[data-edit]").addEventListener("click", () => navigate(`/worlds/${world.id}/edit`));
    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));

    const locationSelect = select(form, "locationId");
    for (const location of world.locations) {
        const option = document.createElement("option");
        option.value = location.id;
        option.textContent = location.name;
        locationSelect.append(option);
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        void mutate(async () => {
            const continuous = runtime.profile.travelResolution === "ContinuousDistance";
            const navRequired = runtime.expedition.activeWatchNumber === null && runtime.profile.usesNavigationChecks && !checkbox(form, "suppressNav").checked && !checkbox(form, "doubleBack").checked;
            const encounterRequired = runtime.expedition.activeWatchNumber === null && runtime.profile.encounterCadence !== "None";
            const encounterOutcome = select(form, "encounterOutcome").value as RuntimeAdvanceRequest["encounterOutcome"];
            const request: RuntimeAdvanceRequest = {
                expectedVersion: runtime.version,
                intendedDirection: integer(input(form, "direction")),
                paceKey: input(form, "pace").value,
                activities: input(form, "activities").value.split(",").map(value => value.trim()).filter(Boolean),
                navigationAidKey: input(form, "navigationAid").value,
                suppressesNavigationCheck: checkbox(form, "suppressNav").checked,
                resetsVeerAtBoundary: checkbox(form, "resetVeer").checked,
                resolutionSource: select(form, "resolutionSource").value as RuntimeAdvanceRequest["resolutionSource"],
                deliberateDoubleBack: checkbox(form, "doubleBack").checked,
                continueAcrossBoundaries: checkbox(form, "continueAcross").checked
            };
            if (continuous) {
                request.expectedDistance = numeric(input(form, "expectedDistance"));
                request.actualDistance = numeric(input(form, "actualDistance"));
            } else {
                request.hexSteps = integer(input(form, "hexSteps"));
            }
            if (navRequired) {
                request.navigationOutcome = select(form, "navigationOutcome").value as "Succeeded" | "Failed";
                request.veerSteps = integer(input(form, "veerSteps"));
            }
            if (encounterRequired) {
                request.encounterOutcome = encounterOutcome;
                if (encounterOutcome !== "None") request.encounterHour = numeric(input(form, "encounterHour"));
                const locationId = locationSelect.value;
                if (locationId) request.locationId = locationId;
                const note = input(form, "encounterNote").value.trim();
                if (note) request.encounterNote = note;
            }
            if (runtime.pauseReason === "LostRecognitionRequired") {
                request.recognizedLost = checkbox(form, "recognizedLost").checked;
                request.reorient = checkbox(form, "reorient").checked;
            }
            const override = input(form, "dmOverrideNote").value.trim();
            if (override) request.dmOverrideNote = override;
            apply(await api.advanceExpedition(runtime.id, request));
        });
    });

    apply(runtime);
    return () => map.dispose();
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

function integer(element: HTMLInputElement): number {
    const value = numeric(element);
    if (!Number.isInteger(value)) throw new Error(`${element.name} must be an integer.`);
    return value;
}
