import type { HexCrawlApi } from "../../api";
import {
    travelEnvironmentMechanicKeys,
    type SourceBackedProcedureResolutionHelperRequest,
    type TravelEnvironmentCatalog,
    type TravelEnvironmentMechanicView,
    type TravelEnvironmentProviderCatalog
} from "../../travel-rules";

export type ProcedureHelperApplicability = {
    travel: boolean;
    navigation: boolean;
    encounter: boolean;
};

export class TravelEnvironmentProviderUi {
    private readonly status: HTMLParagraphElement;
    private readonly travelRule: HTMLSelectElement;
    private readonly baseSpeed: HTMLInputElement;
    private readonly terrain: HTMLSelectElement;
    private readonly route: HTMLSelectElement;
    private readonly navigationRisks: HTMLSelectElement;
    private terrainAvailable = false;
    private disposed = false;

    public constructor(
        private readonly root: HTMLElement,
        private readonly api: HexCrawlApi) {
        const helper = requiredElement<HTMLElement>(root, "[data-resolution-helper]");
        this.status = document.createElement("p");
        this.status.className = "hc-hint";
        this.status.dataset.helperRulesStatus = "";
        this.status.textContent = "Loading external travel and navigation provider capabilities…";
        helper.querySelector("legend")?.after(this.status);

        const travelHost = requiredElement<HTMLElement>(root, "[data-helper-travel]");
        const travelControls = document.createElement("div");
        travelControls.className = "hc-form";
        travelControls.dataset.helperSourceTravel = "";

        this.travelRule = document.createElement("select");
        this.travelRule.name = "helperTravelRule";
        this.travelRule.append(
            createOption("", "Use explicit expected distance"),
            createOption("walk", "Provider-backed walking distance"),
            createOption("hustle", "Provider-backed hustle distance"));
        this.travelRule.disabled = true;
        travelControls.append(labelled("Travel rule", this.travelRule));

        this.baseSpeed = document.createElement("input");
        this.baseSpeed.name = "helperBaseSpeedFeet";
        this.baseSpeed.type = "number";
        this.baseSpeed.min = "1";
        this.baseSpeed.step = "1";
        this.baseSpeed.placeholder = "base speed in feet";
        this.baseSpeed.disabled = true;
        travelControls.append(labelled("Base speed (feet)", this.baseSpeed));

        this.terrain = document.createElement("select");
        this.terrain.name = "helperTerrain";
        this.terrain.append(createOption("", "No provider terrain factor"));
        this.terrain.disabled = true;
        travelControls.append(labelled("Terrain", this.terrain));

        this.route = document.createElement("select");
        this.route.name = "helperRoute";
        this.route.append(createOption("", "No provider route factor"));
        this.route.disabled = true;
        travelControls.append(labelled("Route", this.route));

        const travelHint = document.createElement("p");
        travelHint.className = "hc-hint";
        travelHint.textContent = "An explicit expected distance remains a DM override. Clear it to derive this segment from the available external rule provider; terrain and route are applied only when both are selected.";
        travelControls.append(travelHint);
        travelHost.append(travelControls);

        const navigationHost = requiredElement<HTMLElement>(root, "[data-helper-navigation]");
        this.navigationRisks = document.createElement("select");
        this.navigationRisks.name = "helperNavigationRiskFactors";
        this.navigationRisks.multiple = true;
        this.navigationRisks.size = 4;
        this.navigationRisks.disabled = true;
        navigationHost.append(labelled("Provider-backed navigation risks", this.navigationRisks));
        const navigationHint = document.createElement("p");
        navigationHint.className = "hc-hint";
        navigationHint.textContent = "The available provider can supply the navigation DC for selected risks. An explicit Navigation DC remains a DM override, and the situational modifier and failure veer remain explicit.";
        navigationHost.append(navigationHint);

        this.travelRule.addEventListener("change", () => this.syncTravelInputs());
    }

    public async load(expeditionId: string): Promise<void> {
        try {
            const result = await this.api.getTravelEnvironmentCatalog(expeditionId);
            if (this.disposed) return;
            this.applyCatalogResult(result);
        } catch {
            if (this.disposed) return;
            this.disableProviderControls();
            this.status.textContent = "The external travel rule provider could not be queried. Explicit DM distance and navigation inputs remain usable.";
        }
    }

    public applySourceInputs(
        request: SourceBackedProcedureResolutionHelperRequest,
        applicability: ProcedureHelperApplicability): void {
        if (applicability.travel && request.expectedDistance === undefined) {
            const rule = this.travelRule.value;
            if (rule === "walk" || rule === "hustle") {
                request.travelDistanceRule = rule;
                request.baseSpeedFeet = positiveInteger(this.baseSpeed, "Base speed");

                const terrain = this.terrain.value;
                const route = this.route.value;
                if (Boolean(terrain) !== Boolean(route)) {
                    throw new Error("Select both terrain and route to apply a provider terrain factor, or leave both blank.");
                }
                if (terrain && route) {
                    request.terrain = terrain;
                    request.route = route;
                }
            }
        }

        if (applicability.navigation && request.navigationDifficultyClass === undefined) {
            const selected = Array.from(this.navigationRisks.selectedOptions)
                .map(option => option.value)
                .filter(Boolean);
            if (selected.length > 0) request.navigationRiskFactors = selected;
        }
    }

    public dispose(): void {
        this.disposed = true;
    }

    private applyCatalogResult(result: TravelEnvironmentCatalog): void {
        const providerName = result.provider?.displayName ?? "External provider";
        if (result.availability !== "available" || !result.catalog) {
            this.disableProviderControls();
            const detail = result.detail?.trim();
            this.status.textContent = `${providerName} is ${humanize(result.availability)}. Explicit DM distance and navigation inputs remain usable.${detail ? ` ${detail}` : ""}`;
            return;
        }

        this.applyCatalog(result.catalog, providerName);
    }

    private applyCatalog(catalog: TravelEnvironmentProviderCatalog, providerName: string): void {
        const walk = effectiveMechanic(catalog, travelEnvironmentMechanicKeys.walkDistance);
        const hustle = effectiveMechanic(catalog, travelEnvironmentMechanicKeys.hustleDistance);
        const terrain = effectiveMechanic(catalog, travelEnvironmentMechanicKeys.terrainDistanceFactor);
        const navigation = effectiveMechanic(catalog, travelEnvironmentMechanicKeys.avoidGettingLost);

        const walkOption = this.travelRule.querySelector<HTMLOptionElement>('option[value="walk"]');
        const hustleOption = this.travelRule.querySelector<HTMLOptionElement>('option[value="hustle"]');
        if (walkOption) walkOption.disabled = !walk;
        if (hustleOption) hustleOption.disabled = !hustle;
        this.travelRule.disabled = !walk && !hustle;

        this.terrainAvailable = Boolean(
            terrain?.definition
            && terrain.definition.factorSemantic === "distance-multiplier");
        if (this.terrainAvailable && terrain?.definition) {
            populateAllowedValues(this.terrain, terrain.definition.inputs, "terrain", "No provider terrain factor");
            populateAllowedValues(this.route, terrain.definition.inputs, "route", "No provider route factor");
        } else {
            resetSelect(this.terrain, "No provider terrain factor");
            resetSelect(this.route, "No provider route factor");
        }

        if (navigation?.definition) {
            populateAllowedValues(this.navigationRisks, navigation.definition.inputs, "risk-factors", null);
            this.navigationRisks.disabled = this.navigationRisks.options.length === 0;
        } else {
            this.navigationRisks.replaceChildren();
            this.navigationRisks.disabled = true;
        }

        const unavailable: string[] = [];
        if (!walk) unavailable.push(stateLabel(catalog, travelEnvironmentMechanicKeys.walkDistance, "walk"));
        if (!hustle) unavailable.push(stateLabel(catalog, travelEnvironmentMechanicKeys.hustleDistance, "hustle"));
        if (!this.terrainAvailable) unavailable.push(stateLabel(catalog, travelEnvironmentMechanicKeys.terrainDistanceFactor, "terrain factor"));
        if (!navigation) unavailable.push(stateLabel(catalog, travelEnvironmentMechanicKeys.avoidGettingLost, "navigation DC"));

        const scope = catalog.campaignId
            ? "effective campaign rules"
            : "global effective rules";
        this.status.textContent = unavailable.length === 0
            ? `Available provider: ${providerName}. Provider-backed ${scope} are available. Explicit DM values still take precedence.`
            : `Available provider: ${providerName}. Provider-backed ${scope} loaded. Unavailable or unresolved: ${unavailable.join(", ")}. Explicit DM values remain usable.`;
        this.syncTravelInputs();
    }

    private syncTravelInputs(): void {
        const usesProviderDistance = !this.travelRule.disabled && this.travelRule.value !== "";
        this.baseSpeed.disabled = !usesProviderDistance;
        this.terrain.disabled = !usesProviderDistance || !this.terrainAvailable || this.terrain.options.length <= 1;
        this.route.disabled = !usesProviderDistance || !this.terrainAvailable || this.route.options.length <= 1;
    }

    private disableProviderControls(): void {
        this.travelRule.disabled = true;
        this.baseSpeed.disabled = true;
        this.terrain.disabled = true;
        this.route.disabled = true;
        this.navigationRisks.disabled = true;
    }
}

function effectiveMechanic(catalog: TravelEnvironmentProviderCatalog, key: string): TravelEnvironmentMechanicView | null {
    const mechanic = catalog.mechanics.find(value => value.mechanicKey === key) ?? null;
    return mechanic?.state === "resolved" && mechanic.canResolve && mechanic.definition
        ? mechanic
        : null;
}

function stateLabel(catalog: TravelEnvironmentProviderCatalog, key: string, label: string): string {
    const mechanic = catalog.mechanics.find(value => value.mechanicKey === key);
    return mechanic ? `${label} (${humanize(mechanic.state)})` : `${label} (not present)`;
}

function populateAllowedValues(
    select: HTMLSelectElement,
    inputs: { key: string; allowedValues: string[] | null }[],
    inputKey: string,
    placeholder: string | null): void {
    const definition = inputs.find(input => input.key === inputKey);
    select.replaceChildren();
    if (placeholder !== null) select.append(createOption("", placeholder));
    for (const value of definition?.allowedValues ?? []) {
        select.append(createOption(value, humanize(value)));
    }
}

function resetSelect(select: HTMLSelectElement, placeholder: string): void {
    select.replaceChildren(createOption("", placeholder));
    select.value = "";
}

function labelled(text: string, control: HTMLElement): HTMLLabelElement {
    const label = document.createElement("label");
    label.append(document.createTextNode(`${text} `), control);
    return label;
}

function createOption(value: string, text: string): HTMLOptionElement {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = text;
    return option;
}

function requiredElement<T extends Element>(root: ParentNode, selector: string): T {
    const element = root.querySelector<T>(selector);
    if (!element) throw new Error(`Required element '${selector}' was not found.`);
    return element;
}

function positiveInteger(input: HTMLInputElement, label: string): number {
    const raw = input.value.trim();
    if (!raw) throw new Error(`${label} is required for provider-backed walking or hustling distance.`);
    const value = Number(raw);
    if (!Number.isInteger(value) || value <= 0) {
        throw new Error(`${label} must be a positive whole number.`);
    }
    return value;
}

function humanize(value: string): string {
    return value
        .replace(/[-_]+/g, " ")
        .replace(/\b\w/g, character => character.toUpperCase());
}
