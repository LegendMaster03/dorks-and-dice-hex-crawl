import type { HexCrawlApi } from "./api";
import { manualEntryResolutionSources } from "./expedition-input-policy";
import { encounterCheckDue, navigationResolutionDue, watchActionLabel } from "./expedition-workflow";
import { formatHours } from "./runtime-view";
import type {
    ExpeditionDetail,
    Overworld,
    ResolutionSource,
    RuntimeAdvanceRequest,
    SpatialRuntimeExpedition
} from "./types";
import {
    checkbox,
    input,
    integer,
    nonZeroInteger,
    numeric,
    option,
    optionalText,
    prettyEnum,
    required,
    select,
    sourceLabel
} from "./ui/dom";

export class ExpeditionWatchController {
    private readonly form: HTMLFormElement;
    private readonly advanceButton: HTMLButtonElement;
    private readonly locationSelect: HTMLSelectElement;
    private disposed = false;
    private advancePending = false;

    public constructor(
        private readonly root: HTMLElement,
        private readonly api: HexCrawlApi,
        world: Overworld | null,
        private readonly getRuntime: () => ExpeditionDetail,
        private readonly applyRuntime: (runtime: ExpeditionDetail) => void,
        private readonly runMutation: (action: () => Promise<void>) => Promise<void>) {
        this.form = required<HTMLFormElement>(root, "[data-advance]");
        this.advanceButton = required<HTMLButtonElement>(this.form, "[data-advance-button]");
        this.locationSelect = select(this.form, "locationId");

        for (const name of [
            "travelSource",
            "navigationSource",
            "encounterSource",
            "boundarySource"
        ] as const) {
            const control = select(this.form, name);
            for (const source of manualEntryResolutionSources) {
                control.append(option(source, sourceLabel(source)));
            }
            control.value = "ManualRoll";
        }

        if (world) {
            for (const location of world.locations) {
                this.locationSelect.append(option(location.id, location.name));
            }
        } else {
            select(this.form, "encounterOutcome")
                .querySelector('option[value="KeyedLocationDiscovery"]')
                ?.remove();
        }

        checkbox(this.form, "suppressNav")
            .addEventListener("change", () => this.syncNavigationVisibility());
        checkbox(this.form, "doubleBack")
            .addEventListener("change", () => this.syncNavigationVisibility());
        select(this.form, "navigationOutcome")
            .addEventListener("change", () => this.syncNavigationVisibility());
        select(this.form, "encounterOutcome")
            .addEventListener("change", () => this.syncEncounterFields());
        this.form.addEventListener("submit", event => this.submit(event));
    }

    public sync(runtime: ExpeditionDetail): void {
        const state = spatialState(runtime);
        const newWatch = state.activeWatchNumber === null;
        required<HTMLElement>(this.root, "[data-watch-summary]").textContent =
            watchActionLabel(runtime);
        this.advanceButton.textContent = watchActionLabel(runtime);

        const requirements = required<HTMLElement>(this.root, "[data-requirements]");
        const requirementLines: string[] = [];
        if (runtime.pauseReason) {
            requirementLines.push(
                `Resolve pending ${prettyEnum(runtime.pauseReason)} before the watch can continue.`);
        } else if (newWatch) {
            requirementLines.push(
                `Plan watch ${state.completedWatches + 1} (${formatHours(runtime.profile.watchHours)}).`);
        } else {
            requirementLines.push(
                `Continue watch ${state.activeWatchNumber} with ${formatHours(state.activeWatchRemainingHours ?? runtime.remainingWatchHours)} remaining.`);
        }

        if (runtime.profile.actualDistanceResolution === "VariableResolved"
            && runtime.profile.travelResolution === "ContinuousDistance") {
            requirementLines.push(
                "A resolved expected and actual distance are required for this segment.");
        } else if (runtime.profile.travelResolution === "ContinuousDistance") {
            requirementLines.push("An effective travel distance is required for this segment.");
        } else {
            requirementLines.push("A resolved hex-step count is required for this segment.");
        }

        if (newWatch && runtime.profile.usesNavigationChecks) {
            requirementLines.push(
                "Navigation resolution is required unless the selected aid suppresses it or this is a deliberate double-back.");
        }
        if (encounterCheckDue(runtime)) {
            requirementLines.push(
                `An encounter check is due (${prettyEnum(runtime.profile.encounterCadence)} cadence).`);
        }
        requirements.replaceChildren(...requirementLines.map(text => paragraph(text)));

        const continuous = runtime.profile.travelResolution === "ContinuousDistance";
        required<HTMLElement>(this.form, "[data-fixed-distance]").hidden =
            !(continuous && runtime.profile.actualDistanceResolution === "Fixed");
        required<HTMLElement>(this.form, "[data-variable-distance]").hidden =
            !(continuous && runtime.profile.actualDistanceResolution === "VariableResolved");
        required<HTMLElement>(this.form, "[data-step-distance]").hidden = continuous;

        required<HTMLElement>(this.form, "[data-encounter-resolution]").hidden =
            !encounterCheckDue(runtime);
        required<HTMLElement>(this.form, "[data-boundary-resolution]").hidden =
            runtime.pauseReason !== "LostRecognitionRequired";
        required<HTMLElement>(this.form, "[data-double-back-row]").hidden =
            !runtime.profile.supportsDeliberateDoubleBack;
        required<HTMLElement>(this.form, "[data-suppress-nav-row]").hidden =
            !runtime.profile.usesNavigationChecks;
        required<HTMLElement>(this.form, "[data-reset-veer-row]").hidden =
            !runtime.profile.usesPersistentVeer;

        if (state.activeWatchNumber !== null) {
            select(this.form, "direction").value = String(state.intendedDirection ?? 0);
            input(this.form, "pace").value = state.activePaceKey ?? "normal";
            input(this.form, "activities").value = state.activeActivities.join(", ");
            input(this.form, "navigationAid").value = state.activeNavigationAidKey ?? "none";
            checkbox(this.form, "doubleBack").checked = state.activeDeliberateDoubleBack;
            checkbox(this.form, "continueAcross").checked = state.activeContinueAcrossBoundaries;
        }

        const scale = runtime.context.hexCenterDistance?.value;
        if (scale === undefined) {
            throw new Error("Spatial crawl session is missing hex-center distance.");
        }
        if (!input(this.form, "effectiveDistance").value) {
            input(this.form, "effectiveDistance").value = String(scale);
        }
        if (!input(this.form, "expectedDistance").value) {
            input(this.form, "expectedDistance").value = String(scale);
        }
        if (!input(this.form, "actualDistance").value) {
            input(this.form, "actualDistance").value = String(scale);
        }

        this.syncNavigationVisibility();
        this.syncEncounterFields();

        const directionHelp = runtime.profile.directionChangesCostProgress
            ? "Changing course can consume intra-hex progress under this procedure. The runtime applies the configured cost."
            : "Direction changes do not consume additional progress under this procedure.";
        required<HTMLElement>(this.form, "[data-direction-hint]").textContent =
            `${directionHelp} Direction labels show the axial grid step; persisted runtime values remain 0–5.`;
    }

    public dispose(): void {
        this.disposed = true;
    }

    private syncNavigationVisibility(): void {
        const runtime = this.getRuntime();
        const due = navigationResolutionDue(
            runtime,
            checkbox(this.form, "suppressNav").checked,
            checkbox(this.form, "doubleBack").checked);
        required<HTMLElement>(this.form, "[data-navigation-resolution]").hidden = !due;
        const failed = select(this.form, "navigationOutcome").value === "Failed";
        required<HTMLElement>(this.form, "[data-veer-row]").hidden = !failed;
    }

    private syncEncounterFields(): void {
        const outcome = select(this.form, "encounterOutcome").value;
        required<HTMLElement>(this.form, "[data-encounter-hour]").hidden = outcome === "None";
        required<HTMLElement>(this.form, "[data-encounter-location]").hidden =
            outcome !== "KeyedLocationDiscovery";
    }

    private submit(event: SubmitEvent): void {
        event.preventDefault();
        if (this.advancePending || this.disposed) return;

        this.advancePending = true;
        this.advanceButton.disabled = true;
        this.advanceButton.textContent = "Applying…";

        void this.runMutation(async () => {
            try {
                const runtime = this.getRuntime();
                const continuous = runtime.profile.travelResolution === "ContinuousDistance";
                const navRequired = navigationResolutionDue(
                    runtime,
                    checkbox(this.form, "suppressNav").checked,
                    checkbox(this.form, "doubleBack").checked);
                const encounterRequired = encounterCheckDue(runtime);
                const request: RuntimeAdvanceRequest = {
                    expectedVersion: runtime.version,
                    intendedDirection: integer(select(this.form, "direction")),
                    paceKey: input(this.form, "pace").value.trim() || "normal",
                    activities: input(this.form, "activities").value
                        .split(",")
                        .map(value => value.trim())
                        .filter(Boolean),
                    navigationAidKey: input(this.form, "navigationAid").value.trim() || "none",
                    suppressesNavigationCheck: checkbox(this.form, "suppressNav").checked,
                    resetsVeerAtBoundary: checkbox(this.form, "resetVeer").checked,
                    resolutionSource: "ManualRoll",
                    travelResolutionSource:
                        select(this.form, "travelSource").value as ResolutionSource,
                    travelResolutionNote: optionalText(input(this.form, "travelNote")),
                    deliberateDoubleBack:
                        runtime.profile.supportsDeliberateDoubleBack
                        && checkbox(this.form, "doubleBack").checked,
                    continueAcrossBoundaries: checkbox(this.form, "continueAcross").checked
                };

                if (continuous && runtime.profile.actualDistanceResolution === "Fixed") {
                    request.effectiveDistance = numeric(input(this.form, "effectiveDistance"));
                } else if (continuous) {
                    request.expectedDistance = numeric(input(this.form, "expectedDistance"));
                    request.actualDistance = numeric(input(this.form, "actualDistance"));
                } else {
                    request.hexSteps = integer(input(this.form, "hexSteps"));
                }

                if (navRequired) {
                    request.navigationOutcome =
                        select(this.form, "navigationOutcome").value as "Succeeded" | "Failed";
                    if (request.navigationOutcome === "Failed") {
                        request.veerSteps = nonZeroInteger(input(this.form, "veerSteps"));
                    }
                    request.navigationResolutionSource =
                        select(this.form, "navigationSource").value as ResolutionSource;
                    request.navigationResolutionNote =
                        optionalText(input(this.form, "navigationNote"));
                }

                if (encounterRequired) {
                    request.encounterOutcome = (
                        select(this.form, "encounterOutcome").value
                    ) as RuntimeAdvanceRequest["encounterOutcome"];
                    request.encounterResolutionSource =
                        select(this.form, "encounterSource").value as ResolutionSource;
                    request.encounterResolutionNote =
                        optionalText(input(this.form, "encounterSourceNote"));
                    if (request.encounterOutcome !== "None") {
                        request.encounterHour = numeric(input(this.form, "encounterHour"));
                    }
                    if (request.encounterOutcome === "KeyedLocationDiscovery") {
                        if (!this.locationSelect.value) {
                            throw new Error(
                                "A keyed-location encounter requires a location.");
                        }
                        request.locationId = this.locationSelect.value;
                    }
                    request.encounterNote = optionalText(input(this.form, "encounterNote"));
                }

                if (runtime.pauseReason === "LostRecognitionRequired") {
                    request.recognizedLost = checkbox(this.form, "recognizedLost").checked;
                    request.reorient = checkbox(this.form, "reorient").checked;
                    request.boundaryResolutionSource =
                        select(this.form, "boundarySource").value as ResolutionSource;
                    request.boundaryResolutionNote =
                        optionalText(input(this.form, "boundaryNote"));
                }

                request.dmOverrideNote = optionalText(input(this.form, "dmOverrideNote"));
                this.applyRuntime(await this.api.advanceExpedition(runtime.id, request));
            } finally {
                this.advancePending = false;
                if (!this.disposed) {
                    this.advanceButton.disabled = false;
                    this.advanceButton.textContent =
                        watchActionLabel(this.getRuntime());
                }
            }
        });
    }
}

function spatialState(runtime: ExpeditionDetail): SpatialRuntimeExpedition {
    if (!runtime.expedition.isSpatial) {
        throw new Error("This operation requires a spatial crawl session.");
    }
    return runtime.expedition;
}

function paragraph(text: string): HTMLParagraphElement {
    const item = document.createElement("p");
    item.className = "hc-hint";
    item.textContent = text;
    return item;
}
