import type { HexCrawlApi } from "../../api";
import { newExpeditionEncounterCadences } from "./expedition-input-policy";
import type { PresentationProfile, RuntimeProfile } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { procedureProfileSummary } from "../../procedure-profile-view";

export async function enhanceExpeditionSetup(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    const previous = root.querySelector<HTMLFormElement>("[data-expedition-form]");
    if (!previous) return () => {};

    const [profiles, presentations] = await Promise.all([
        api.getRuntimeProfiles(),
        api.getPresentationProfiles()
    ]);
    const form = document.createElement("form");
    form.className = "hc-form";
    form.dataset.expeditionForm = "";
    form.innerHTML = `
        <label>Name <input name="name" required value="Expedition"></label>
        <label>Procedure preset <select name="procedure"></select></label>
        <p class="hc-hint" data-procedure-summary></p>
        <label>Map presentation <select name="presentation"></select></label>
        <p class="hc-hint" data-presentation-summary></p>
        <div class="hc-inline"><label>Start q <input name="q" type="number" step="1" value="0"></label><label>Start r <input name="r" type="number" step="1" value="0"></label></div>
        <label><input name="customize" type="checkbox"> Customize this expedition procedure</label>
        <div data-customization hidden>
            <div class="hc-form">
                <label>Watch length (hours) <input name="watchHours" type="number" min="0.01" step="any"></label>
                <label>Travel model <select name="travelResolution"><option>ContinuousDistance</option><option>HexSteps</option></select></label>
                <label>Travel distance <select name="actualDistanceResolution"><option>Fixed</option><option>VariableResolved</option></select></label>
                <label>Encounter cadence <select name="encounterCadence"></select></label>
                <label><input name="usesNavigationChecks" type="checkbox"> Navigation checks</label>
                <label><input name="usesPersistentVeer" type="checkbox"> Persistent veer</label>
                <label><input name="tracksIntraHexProgress" type="checkbox"> Intra-hex progress</label>
                <label><input name="directionChangesCostProgress" type="checkbox"> Direction changes cost progress</label>
                <label><input name="supportsDeliberateDoubleBack" type="checkbox"> Deliberate single-hex double-back</label>
                <details><summary>Advanced progress factors</summary><div class="hc-form">
                    <label>Start exit factor <input name="startingExitProgressFactor" type="number" min="0" step="any"></label>
                    <label>Near exit factor <input name="nearExitProgressFactor" type="number" min="0" step="any"></label>
                    <label>Far exit factor <input name="farExitProgressFactor" type="number" min="0" step="any"></label>
                    <label>Back exit factor <input name="backExitProgressFactor" type="number" min="0" step="any"></label>
                    <label>Direction-change cost factor <input name="directionChangeProgressCostFactor" type="number" min="0" step="any"></label>
                </div></details>
                <p class="hc-hint">The server applies the same domain validation used by the runtime. This form does not maintain a separate validity rule set.</p>
            </div>
        </div>
        <button type="submit" class="hc-primary-action">Start expedition</button>`;
    previous.replaceWith(form);

    const procedure = select(form, "procedure");
    for (const profile of profiles) procedure.append(option(profile.key, profile.name));
    const presentation = select(form, "presentation");
    for (const policy of presentations) presentation.append(option(policy.key, policy.name));
    const encounterCadence = select(form, "encounterCadence");
    for (const cadence of newExpeditionEncounterCadences) encounterCadence.append(option(cadence, cadence));
    if (presentations.some(policy => policy.key === "exploration-map")) presentation.value = "exploration-map";

    const customization = required<HTMLElement>(form, "[data-customization]");
    const customize = checkbox(form, "customize");
    const syncCustomization = (): void => {
        customization.hidden = !customize.checked;
        if (customize.checked) fillProcedure(selectedProfile(profiles, procedure.value));
    };
    const fillProcedure = (profile: RuntimeProfile): void => {
        numberInput(form, "watchHours").value = String(profile.watchHours);
        select(form, "travelResolution").value = profile.travelResolution;
        select(form, "actualDistanceResolution").value = profile.actualDistanceResolution;
        select(form, "encounterCadence").value = profile.encounterCadence;
        checkbox(form, "usesNavigationChecks").checked = profile.usesNavigationChecks;
        checkbox(form, "usesPersistentVeer").checked = profile.usesPersistentVeer;
        checkbox(form, "tracksIntraHexProgress").checked = profile.tracksIntraHexProgress;
        checkbox(form, "directionChangesCostProgress").checked = profile.directionChangesCostProgress;
        checkbox(form, "supportsDeliberateDoubleBack").checked = profile.supportsDeliberateDoubleBack;
        numberInput(form, "startingExitProgressFactor").value = String(profile.startingExitProgressFactor);
        numberInput(form, "nearExitProgressFactor").value = String(profile.nearExitProgressFactor);
        numberInput(form, "farExitProgressFactor").value = String(profile.farExitProgressFactor);
        numberInput(form, "backExitProgressFactor").value = String(profile.backExitProgressFactor);
        numberInput(form, "directionChangeProgressCostFactor").value = String(profile.directionChangeProgressCostFactor);
        renderProcedureSummary(profile);
    };
    const renderProcedureSummary = (profile: RuntimeProfile): void => {
        required<HTMLElement>(form, "[data-procedure-summary]").textContent =
            procedureProfileSummary(profile);
    };
    const renderPresentationSummary = (policy: PresentationProfile): void => {
        const automation = policy.automationMode === "DmControlled" ? "DM controls every reveal" : policy.markEnteredHexKnown ? "explored hexes become known" : "no travel-based reveal";
        required<HTMLElement>(form, "[data-presentation-summary]").textContent =
            `${policy.playerGrid.toLowerCase()} player grid · terrain ${policy.terrainMode.replace(/([A-Z])/g, " $1").trim().toLowerCase()} · ${automation}`;
    };

    procedure.addEventListener("change", () => {
        const profile = selectedProfile(profiles, procedure.value);
        renderProcedureSummary(profile);
        if (customize.checked) fillProcedure(profile);
    });
    presentation.addEventListener("change", () => renderPresentationSummary(selectedPresentation(presentations, presentation.value)));
    customize.addEventListener("change", syncCustomization);

    fillProcedure(selectedProfile(profiles, procedure.value));
    renderPresentationSummary(selectedPresentation(presentations, presentation.value));
    syncCustomization();

    const error = root.querySelector<HTMLElement>("[data-error]");
    const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    form.addEventListener("submit", event => {
        event.preventDefault();
        if (form.dataset.pending === "true") return;
        form.dataset.pending = "true";
        submit.disabled = true;
        submit.textContent = "Starting…";
        if (error) clearUiError(error);
        void (async () => {
            try {
                const preset = selectedProfile(profiles, procedure.value);
                const procedureSnapshot = customize.checked ? profileFromForm(form, preset) : undefined;
                const expedition = await api.startConfiguredExpedition(worldId, {
                    name: input(form, "name").value.trim(),
                    procedureKey: procedure.value,
                    presentationKey: presentation.value,
                    startHex: { q: integer(numberInput(form, "q")), r: integer(numberInput(form, "r")) },
                    procedureSnapshot
                });
                navigate(`/worlds/${worldId}/expeditions/${expedition.id}`);
            } catch (value) {
                if (error) showUiError(error, value);
            } finally {
                form.dataset.pending = "false";
                submit.disabled = false;
                submit.textContent = "Start expedition";
            }
        })();
    });

    return () => form.remove();
}

function profileFromForm(form: HTMLFormElement, preset: RuntimeProfile): RuntimeProfile {
    const travelResolution =
        select(form, "travelResolution").value as RuntimeProfile["travelResolution"];
    const actualDistanceResolution =
        select(form, "actualDistanceResolution").value as RuntimeProfile["actualDistanceResolution"];
    const encounterCadence =
        select(form, "encounterCadence").value as RuntimeProfile["encounterCadence"];
    const usesNavigationChecks = checkbox(form, "usesNavigationChecks").checked;

    return {
        ...preset,
        watchHours: numeric(numberInput(form, "watchHours")),
        travelResolution,
        actualDistanceResolution,
        encounterCadence,
        usesNavigationChecks,
        usesPersistentVeer: checkbox(form, "usesPersistentVeer").checked,
        tracksIntraHexProgress: checkbox(form, "tracksIntraHexProgress").checked,
        directionChangesCostProgress: checkbox(form, "directionChangesCostProgress").checked,
        supportsDeliberateDoubleBack: checkbox(form, "supportsDeliberateDoubleBack").checked,
        startingExitProgressFactor: numeric(numberInput(form, "startingExitProgressFactor")),
        nearExitProgressFactor: numeric(numberInput(form, "nearExitProgressFactor")),
        farExitProgressFactor: numeric(numberInput(form, "farExitProgressFactor")),
        backExitProgressFactor: numeric(numberInput(form, "backExitProgressFactor")),
        directionChangeProgressCostFactor: numeric(numberInput(form, "directionChangeProgressCostFactor")),
        resolutionHelpers: compatibleResolutionHelpers(
            preset.resolutionHelpers,
            travelResolution,
            actualDistanceResolution,
            usesNavigationChecks,
            encounterCadence)
    };
}

function compatibleResolutionHelpers(
    helpers: RuntimeProfile["resolutionHelpers"],
    travelResolution: RuntimeProfile["travelResolution"],
    actualDistanceResolution: RuntimeProfile["actualDistanceResolution"],
    usesNavigationChecks: boolean,
    encounterCadence: RuntimeProfile["encounterCadence"]): RuntimeProfile["resolutionHelpers"] {
    if (!helpers) return null;

    const compatible = {
        travel: travelResolution === "ContinuousDistance"
            && actualDistanceResolution === "VariableResolved"
            ? helpers.travel
            : null,
        navigation: usesNavigationChecks ? helpers.navigation : null,
        encounter: encounterCadence === "None" ? null : helpers.encounter
    };

    return compatible.travel || compatible.navigation || compatible.encounter
        ? compatible
        : null;
}

function selectedProfile(profiles: RuntimeProfile[], key: string): RuntimeProfile {
    const profile = profiles.find(candidate => candidate.key === key);
    if (!profile) throw new Error(`Unknown procedure preset ${key}.`);
    return profile;
}

function selectedPresentation(policies: PresentationProfile[], key: string): PresentationProfile {
    const policy = policies.find(candidate => candidate.key === key);
    if (!policy) throw new Error(`Unknown presentation preset ${key}.`);
    return policy;
}

function option(value: string, label: string): HTMLOptionElement {
    const result = document.createElement("option");
    result.value = value;
    result.textContent = label;
    return result;
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

function numberInput(root: ParentNode, name: string): HTMLInputElement {
    return input(root, name);
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