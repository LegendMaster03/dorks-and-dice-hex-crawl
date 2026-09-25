import type { HexCrawlApi } from "../../api";
import { manualEntryResolutionSources } from "./expedition-input-policy";
import { suggestedWatchDistance } from "./expedition-party-movement";
import { encounterCheckDue, navigationResolutionDue, watchActionLabel } from "./expedition-workflow";
import { formatHours } from "../../runtime-view";
import type {
    ExpeditionDetail,
    Overworld,
    ProcedureResolutionHelperResult,
    ResolutionSource,
    RuntimeAdvanceRequest,
    SpatialRuntimeExpedition
} from "../../types";
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
} from "../../ui/dom";

export class ExpeditionWatchController {
    private readonly form: HTMLFormElement;
    private readonly advanceButton: HTMLButtonElement;
    private readonly locationSelect: HTMLSelectElement;
    private disposed = false;
    private advancePending = false;
    private resolutionPending = false;
    private generatedResolutionId: string | null = null;
    private generatedResolutionVersion: number | null = null;
    private generatedEncounterLocationId: string | null | undefined;
    private segmentStateKey: string | null = null;

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
            const placeholder = option("", "Select resolution source");
            placeholder.disabled = true;
            control.append(placeholder);
            for (const source of manualEntryResolutionSources) {
                control.append(option(source, sourceLabel(source)));
            }
            if (name !== "boundarySource") {
                const automatic = option("AutomaticRoll", sourceLabel("AutomaticRoll"));
                automatic.disabled = true;
                control.append(automatic);
            }
            control.value = "";
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
            .addEventListener("change", () => {
                this.markGeneratedComponentEdited("navigation");
                this.syncNavigationVisibility();
                this.syncResolutionHelperVisibility();
            });
        checkbox(this.form, "doubleBack")
            .addEventListener("change", () => {
                this.markGeneratedComponentEdited("navigation");
                this.syncNavigationVisibility();
                this.syncResolutionHelperVisibility();
            });
        select(this.form, "navigationOutcome")
            .addEventListener("change", () => {
                this.markGeneratedComponentEdited("navigation");
                this.syncNavigationVisibility();
            });
        input(this.form, "veerSteps")
            .addEventListener("input", () => this.markGeneratedComponentEdited("navigation"));
        select(this.form, "encounterOutcome")
            .addEventListener("change", () => {
                this.markGeneratedComponentEdited("encounter");
                this.syncEncounterFields();
            });
        input(this.form, "encounterHour")
            .addEventListener("input", () => this.markGeneratedComponentEdited("encounter"));
        this.locationSelect.addEventListener("change", () => this.handleGeneratedLocationEdit());
        for (const name of ["expectedDistance", "actualDistance"] as const) {
            input(this.form, name)
                .addEventListener("input", () => this.markGeneratedComponentEdited("travel"));
        }
        required<HTMLButtonElement>(this.root, "[data-resolution-helper-button]")
            .addEventListener("click", () => this.generateProcedureResolution());
        this.form.addEventListener("submit", event => this.submit(event));
    }

    public sync(runtime: ExpeditionDetail): void {
        this.expireGeneratedResolutionIfVersionChanged(runtime.version);
        const state = spatialState(runtime);
        this.syncSegmentState(runtime, state);
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

        if (!runtime.context.hexCenterDistance) {
            throw new Error("Spatial crawl session is missing hex-center distance.");
        }
        const suggestedDistance = suggestedWatchDistance(runtime);
        if (suggestedDistance !== null) {
            if (!input(this.form, "effectiveDistance").value) {
                input(this.form, "effectiveDistance").value = String(suggestedDistance);
            }
            if (!input(this.form, "expectedDistance").value) {
                input(this.form, "expectedDistance").value = String(suggestedDistance);
            }
        }

        this.syncNavigationVisibility();
        this.syncEncounterFields();
        this.syncResolutionHelperVisibility(runtime);

        const directionHelp = runtime.profile.directionChangesCostProgress
            ? "Changing course can consume intra-hex progress under this procedure. The runtime applies the configured cost."
            : "Direction changes do not consume additional progress under this procedure.";
        required<HTMLElement>(this.form, "[data-direction-hint]").textContent =
            `${directionHelp} Direction labels show the axial grid step; persisted runtime values remain 0–5.`;
    }

    public dispose(): void {
        this.disposed = true;
    }

    private syncSegmentState(
        runtime: ExpeditionDetail,
        state: SpatialRuntimeExpedition): void {
        const key = [
            state.completedWatches,
            state.activeWatchNumber ?? "ready",
            state.activeWatchElapsedHours ?? 0,
            `${state.currentHex.q},${state.currentHex.r}`,
            runtime.pauseReason ?? "none"
        ].join(":");

        if (this.segmentStateKey !== null && this.segmentStateKey !== key) {
            this.clearResolvedSegmentInputs();
        }
        this.segmentStateKey = key;
    }

    private clearResolvedSegmentInputs(): void {
        for (const name of ["effectiveDistance", "expectedDistance", "actualDistance", "hexSteps"] as const) {
            input(this.form, name).value = "";
        }
        input(this.form, "travelNote").value = "";
        input(this.form, "navigationNote").value = "";
        input(this.form, "veerSteps").value = "";
        input(this.form, "encounterHour").value = "";
        input(this.form, "encounterNote").value = "";
        input(this.form, "encounterSourceNote").value = "";
        input(this.form, "boundaryNote").value = "";
        input(this.form, "dmOverrideNote").value = "";
        input(this.form, "helperNavigationDc").value = "";
        input(this.form, "helperNavigationModifier").value = "";
        input(this.form, "helperFailureVeer").value = "";
        checkbox(this.form, "recognizedLost").checked = false;
        checkbox(this.form, "reorient").checked = false;
        select(this.form, "navigationOutcome").value = "";
        select(this.form, "encounterOutcome").value = "";
        this.locationSelect.value = "";
        select(this.form, "travelSource").value = "";
        select(this.form, "navigationSource").value = "";
        select(this.form, "encounterSource").value = "";
        select(this.form, "boundarySource").value = "";
        this.generatedResolutionId = null;
        this.generatedResolutionVersion = null;
        this.generatedEncounterLocationId = undefined;
        const helperResult = this.root.querySelector<HTMLElement>("[data-resolution-helper-result]");
        if (helperResult) helperResult.textContent = "";
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
        required<HTMLElement>(this.form, "[data-encounter-hour]").hidden =
            outcome === "" || outcome === "None";
        required<HTMLElement>(this.form, "[data-encounter-location]").hidden =
            outcome !== "KeyedLocationDiscovery";
    }

    private helperApplicability(runtime: ExpeditionDetail): {
        travel: boolean;
        navigation: boolean;
        encounter: boolean;
    } {
        const configured = runtime.profile.resolutionHelpers;
        if (!configured) return { travel: false, navigation: false, encounter: false };

        return {
            travel: configured.travel !== null
                && runtime.profile.travelResolution === "ContinuousDistance"
                && runtime.profile.actualDistanceResolution === "VariableResolved",
            navigation: configured.navigation !== null
                && navigationResolutionDue(
                    runtime,
                    checkbox(this.form, "suppressNav").checked,
                    checkbox(this.form, "doubleBack").checked),
            encounter: configured.encounter !== null && encounterCheckDue(runtime)
        };
    }

    private syncResolutionHelperVisibility(runtime = this.getRuntime()): void {
        const applicability = this.helperApplicability(runtime);
        const any = applicability.travel || applicability.navigation || applicability.encounter;
        required<HTMLElement>(this.root, "[data-resolution-helper]").hidden = !any;
        required<HTMLElement>(this.root, "[data-helper-travel]").hidden = !applicability.travel;
        required<HTMLElement>(this.root, "[data-helper-navigation]").hidden = !applicability.navigation;
        required<HTMLElement>(this.root, "[data-helper-encounter]").hidden = !applicability.encounter;
        const button = required<HTMLButtonElement>(this.root, "[data-resolution-helper-button]");
        button.disabled = !any || this.advancePending || this.resolutionPending;
    }

    private generateProcedureResolution(): void {
        if (this.disposed || this.advancePending || this.resolutionPending) return;
        const button = required<HTMLButtonElement>(this.root, "[data-resolution-helper-button]");
        if (button.disabled) return;

        const idleText = button.textContent ?? "Roll procedure inputs";
        this.resolutionPending = true;
        this.advanceButton.disabled = true;
        button.disabled = true;
        button.textContent = "Rolling…";

        void this.runMutation(async () => {
            try {
                const runtime = this.getRuntime();
                const applicability = this.helperApplicability(runtime);
                if (!applicability.travel && !applicability.navigation && !applicability.encounter) {
                    throw new Error("No automatic procedure helper is applicable to the current watch state.");
                }

                const request = {
                    expectedVersion: runtime.version,
                    suppressesNavigationCheck: checkbox(this.form, "suppressNav").checked,
                    deliberateDoubleBack:
                        runtime.profile.supportsDeliberateDoubleBack
                        && checkbox(this.form, "doubleBack").checked,
                    navigationModifier: applicability.navigation
                        ? integer(input(this.form, "helperNavigationModifier"))
                        : 0,
                    expectedDistance: applicability.travel
                        ? numeric(input(this.form, "expectedDistance"))
                        : undefined,
                    navigationDifficultyClass: applicability.navigation
                        ? integer(input(this.form, "helperNavigationDc"))
                        : undefined,
                    failureVeerSteps: applicability.navigation
                        ? nonZeroInteger(input(this.form, "helperFailureVeer"))
                        : undefined,
                    keyedLocationId: applicability.encounter && this.locationSelect.value
                        ? this.locationSelect.value
                        : undefined
                };

                const result = await this.api.resolveProcedureInputs(runtime.id, request);
                if (result.generatedResolutionId !== null) {
                    const refreshed = await this.api.getExpedition(runtime.id);
                    if (refreshed.version !== result.expeditionVersion) {
                        throw new Error(
                            "The crawl session changed after procedure inputs were generated. Generate them again for the current version.");
                    }
                    this.applyRuntime(refreshed);
                }
                this.applyGeneratedResolution(result);
            } finally {
                this.resolutionPending = false;
                if (!this.disposed) {
                    button.textContent = idleText;
                    this.advanceButton.disabled = false;
                    this.advanceButton.textContent = watchActionLabel(this.getRuntime());
                    this.syncResolutionHelperVisibility();
                }
            }
        });
    }

    private applyGeneratedResolution(result: ProcedureResolutionHelperResult): void {
        this.generatedResolutionId = result.generatedResolutionId;
        this.generatedResolutionVersion = result.generatedResolutionId === null
            ? null
            : result.expeditionVersion;
        this.generatedEncounterLocationId = result.encounter?.locationId;

        if (result.travel) {
            input(this.form, "expectedDistance").value = String(result.travel.expectedDistance);
            input(this.form, "actualDistance").value = String(result.travel.actualDistance);
            this.setAutomaticSource(
                "travelSource",
                "travelNote",
                result.travel.provenance.note);
        }

        if (result.navigation) {
            select(this.form, "navigationOutcome").value = result.navigation.outcome;
            input(this.form, "veerSteps").value =
                result.navigation.veerSteps === null ? "" : String(result.navigation.veerSteps);
            this.setAutomaticSource(
                "navigationSource",
                "navigationNote",
                result.navigation.provenance.note);
        }

        if (result.encounter) {
            select(this.form, "encounterOutcome").value = result.encounter.kind;
            input(this.form, "encounterHour").value =
                result.encounter.occursAtHours === null ? "" : String(result.encounter.occursAtHours);
            if (result.encounter.locationId) this.locationSelect.value = result.encounter.locationId;
            input(this.form, "encounterNote").value = result.encounter.note ?? "";
            this.setAutomaticSource(
                "encounterSource",
                "encounterSourceNote",
                result.encounter.provenance.note);
        }

        this.syncNavigationVisibility();
        this.syncEncounterFields();

        const summary = required<HTMLElement>(this.root, "[data-resolution-helper-result]");
        const parts: string[] = [];
        if (result.rolls.length > 0) {
            parts.push(result.rolls
                .map(roll => `${roll.purpose}: ${roll.formula} [${roll.dice.join(", ")}] = ${roll.total}`)
                .join("; "));
        }
        if (result.travel) {
            parts.push(
                `travel ${formatNumber(result.travel.actualDistance)} from expected ${formatNumber(result.travel.expectedDistance)}`);
        }
        if (result.navigation) {
            parts.push(
                `navigation ${prettyEnum(result.navigation.outcome)}${result.navigation.veerSteps === null ? "" : ` · veer ${result.navigation.veerSteps}`}`);
        }
        if (result.encounter) {
            parts.push(
                `encounter ${prettyEnum(result.encounter.kind)}${result.encounter.occursAtHours === null ? "" : ` at ${formatHours(result.encounter.occursAtHours)}`}`);
        }
        parts.push(...result.notes);
        summary.textContent = parts.join(" · ") || "No procedure-defined helper result was generated.";
    }

    private setAutomaticSource(
        sourceName: "travelSource" | "navigationSource" | "encounterSource",
        noteName: "travelNote" | "navigationNote" | "encounterSourceNote",
        note: string | null): void {
        select(this.form, sourceName).value = "AutomaticRoll";
        input(this.form, noteName).value = note ?? "";
    }

    private markGeneratedComponentEdited(
        component: "travel" | "navigation" | "encounter"): void {
        const fields = {
            travel: ["travelSource", "travelNote"],
            navigation: ["navigationSource", "navigationNote"],
            encounter: ["encounterSource", "encounterSourceNote"]
        } as const;
        const [sourceName, noteName] = fields[component];
        const source = select(this.form, sourceName);
        if (source.value !== "AutomaticRoll") return;

        source.value = "DmOverride";
        const note = input(this.form, noteName);
        const suffix = "Edited after automatic generation.";
        note.value = note.value.trim()
            ? `${note.value.trim()} ${suffix}`
            : suffix;
        this.releaseGeneratedResolutionIfUnused();

        required<HTMLElement>(this.root, "[data-resolution-helper-result]").textContent =
            "A generated result was edited. That component is now a DM override; generate again to restore AutomaticRoll provenance.";
    }

    private expireGeneratedResolutionIfVersionChanged(runtimeVersion: number): void {
        if (this.generatedResolutionId === null
            || this.generatedResolutionVersion === null
            || this.generatedResolutionVersion === runtimeVersion) {
            return;
        }

        const fields = [
            ["travelSource", "travelNote"],
            ["navigationSource", "navigationNote"],
            ["encounterSource", "encounterSourceNote"]
        ] as const;
        let hadAutomaticComponent = false;
        for (const [sourceName, noteName] of fields) {
            const source = select(this.form, sourceName);
            if (source.value !== "AutomaticRoll") continue;
            hadAutomaticComponent = true;
            source.value = "";
            input(this.form, noteName).value = "";
        }

        this.generatedResolutionId = null;
        this.generatedResolutionVersion = null;
        this.generatedEncounterLocationId = undefined;
        if (hadAutomaticComponent) {
            required<HTMLElement>(this.root, "[data-resolution-helper-result]").textContent =
                "Generated procedure inputs expired because the crawl session changed. Generate again or choose an explicit resolution source before running the watch.";
        }
    }

    private handleGeneratedLocationEdit(): void {
        if (select(this.form, "encounterSource").value !== "AutomaticRoll") return;
        if (this.generatedEncounterLocationId === null) {
            // A keyed-location roll may intentionally leave location choice to the DM.
            return;
        }
        if (this.generatedEncounterLocationId !== undefined
            && this.locationSelect.value !== this.generatedEncounterLocationId) {
            this.markGeneratedComponentEdited("encounter");
        }
    }

    private releaseGeneratedResolutionIfUnused(): void {
        if (["travelSource", "navigationSource", "encounterSource"]
            .every(name => select(this.form, name).value !== "AutomaticRoll")) {
            this.generatedResolutionId = null;
            this.generatedResolutionVersion = null;
            this.generatedEncounterLocationId = undefined;
        }
    }

    private readResolutionSource(
        name: "travelSource" | "navigationSource" | "encounterSource" | "boundarySource",
        label: string,
        allowAutomatic = true): ResolutionSource {
        const value = select(this.form, name).value;
        if (!value) {
            throw new Error(`Select the ${label} resolution source.`);
        }
        if (value === "AutomaticRoll" && !allowAutomatic) {
            throw new Error(`${label} can not use automatic procedure provenance.`);
        }
        return value as ResolutionSource;
    }

    private submit(event: SubmitEvent): void {
        event.preventDefault();
        if (this.advancePending || this.resolutionPending || this.disposed) return;

        this.advancePending = true;
        this.advanceButton.disabled = true;
        this.advanceButton.textContent = "Applying…";
        this.syncResolutionHelperVisibility();

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
                        this.readResolutionSource("travelSource", "travel"),
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
                    const navigationOutcome = select(this.form, "navigationOutcome").value;
                    if (navigationOutcome !== "Succeeded" && navigationOutcome !== "Failed") {
                        throw new Error("A navigation check is due. Select its resolved result.");
                    }
                    request.navigationOutcome = navigationOutcome;
                    if (request.navigationOutcome === "Failed") {
                        request.veerSteps = nonZeroInteger(input(this.form, "veerSteps"));
                    }
                    request.navigationResolutionSource =
                        this.readResolutionSource("navigationSource", "navigation");
                    request.navigationResolutionNote =
                        optionalText(input(this.form, "navigationNote"));
                }

                if (encounterRequired) {
                    const encounterOutcome = select(this.form, "encounterOutcome").value;
                    if (!["None", "WanderingEncounter", "KeyedLocationDiscovery", "ManualCustom"]
                        .includes(encounterOutcome)) {
                        throw new Error("An encounter check is due. Select its resolved outcome.");
                    }
                    request.encounterOutcome =
                        encounterOutcome as RuntimeAdvanceRequest["encounterOutcome"];
                    request.encounterResolutionSource =
                        this.readResolutionSource("encounterSource", "encounter");
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
                        this.readResolutionSource("boundarySource", "boundary", false);
                    request.boundaryResolutionNote =
                        optionalText(input(this.form, "boundaryNote"));
                }

                request.dmOverrideNote = optionalText(input(this.form, "dmOverrideNote"));
                const usesAutomatic = [
                    request.travelResolutionSource,
                    request.navigationResolutionSource,
                    request.encounterResolutionSource
                ].some(source => source === "AutomaticRoll");
                if (usesAutomatic) {
                    if (!this.generatedResolutionId) {
                        throw new Error(
                            "Automatic procedure results are no longer valid. Generate them again before running the watch.");
                    }
                    request.generatedProcedureResolutionId = this.generatedResolutionId;
                }

                const advanced = await this.api.advanceExpedition(runtime.id, request);
                this.generatedResolutionId = null;
                this.generatedResolutionVersion = null;
                this.generatedEncounterLocationId = undefined;
                this.applyRuntime(advanced);
            } finally {
                this.advancePending = false;
                if (!this.disposed) {
                    this.advanceButton.disabled = false;
                    this.advanceButton.textContent =
                        watchActionLabel(this.getRuntime());
                    this.syncResolutionHelperVisibility();
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

function formatNumber(value: number): string {
    return Number.isInteger(value)
        ? String(value)
        : value.toFixed(3).replace(/0+$/, "").replace(/\.$/, "");
}
