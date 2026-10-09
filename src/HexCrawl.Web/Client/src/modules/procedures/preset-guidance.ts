import type { CampaignProcedure, ProcedureAutomationLevel, ProcedureModule } from "../../types";

export type PresetGuidanceFact = {
    label: string;
    value: string;
};

export type PresetGuidance = {
    bestFor: string;
    manage: string;
    breadth: "Focused" | "Moderate" | "Broad";
    areaCount: number;
    summaryFacts: PresetGuidanceFact[];
    caution: string | null;
};

type GuidanceArea = {
    label: string;
    modules: ProcedureModule[];
    annotateAutomation: boolean;
};

export function presetGuidance(procedure: CampaignProcedure): PresetGuidance {
    const modules = new Map(procedure.modules.map(module => [module.moduleKey, module]));
    const get = (key: string): ProcedureModule | null => modules.get(key) ?? null;
    const parameter = (moduleKey: string, key: string): string =>
        get(moduleKey)?.parameters[key]?.trim() ?? "";

    const timeActive = get("time.interval") !== null;
    const movementResolutionActive = activeToken(parameter("movement.resolution", "travelResolution"));
    const tracksIntraHexProgress = bool(parameter("movement.resolution", "tracksIntraHexProgress"));
    const hexProgressActive = get("movement.hex-progress") !== null && (
        tracksIntraHexProgress
        || bool(parameter("movement.hex-progress", "directionChangesCostProgress"))
        || bool(parameter("movement.hex-progress", "supportsDeliberateDoubleBack")));
    const movementBudgetActive = activeToken(parameter("movement.budget", "budgetModel"));
    const terrainActive = activeToken(parameter("movement.terrain", "adjustmentModel"));

    const navigationCheckActive = bool(parameter("navigation.check", "usesNavigationChecks"));
    const navigationOutcomeActive = activeToken(parameter("navigation.outcome", "checkTriggerModel"));

    const encounterCadenceActive = activeToken(parameter("encounters.cadence", "cadence"));
    const scheduleModel = normalized(parameter("encounters.schedule", "scheduleModel"));
    const encounterScheduleActive =
        positiveNumber(parameter("encounters.schedule", "travelChecksPerInterval"))
        || bool(parameter("encounters.schedule", "campCheck"))
        || (activeToken(scheduleModel) && scheduleModel !== "cadence-backed");

    const resourcesActive =
        hasListValue(parameter("survival.resources", "resourceKinds"))
        && activeToken(parameter("survival.resources", "consumptionModel"));
    const foragingActive = activeToken(parameter("exploration.foraging", "resolutionModel"));
    const campingActive = activeToken(parameter("survival.camping", "resolutionModel"));
    const forcedTravelActive =
        finiteNumber(parameter("time.forced-travel", "normalTravelLimit"))
        && activeToken(parameter("time.forced-travel", "checkModel"));
    const exposureActive =
        hasListValue(parameter("survival.exposure", "dimensions"))
        && activeToken(parameter("survival.exposure", "evaluationModel"));
    const effectsActive =
        hasListValue(parameter("effects.expedition", "effectKinds"))
        && activeToken(parameter("effects.expedition", "accumulationModel"));

    const journeyProcessActive =
        activeToken(parameter("journey.process", "stageModel"))
        || activeToken(parameter("journey.process", "progressModel"))
        || activeToken(parameter("journey.process", "completionModel"));
    const journeyEventsActive = activeToken(parameter("journey.events", "triggerModel"));
    const activitiesActive =
        activeToken(parameter("party.activities", "activityBudgetModel"))
        && (
            hasListValue(parameter("party.activities", "activityKeys"))
            || hasListValue(parameter("party.activities", "roleKeys"))
        );
    const helpersActive = [
        "travel.enabled",
        "navigation.enabled",
        "encounter.enabled"
    ].some(key => bool(parameter("procedure.helpers", key)));

    const areas: GuidanceArea[] = [];
    addArea(
        areas,
        "travel time and movement",
        activeModules([
            [timeActive, get("time.interval")],
            [movementResolutionActive, get("movement.resolution")],
            [hexProgressActive, get("movement.hex-progress")],
            [movementBudgetActive, get("movement.budget")],
            [terrainActive, get("movement.terrain")]
        ]),
        true);
    addArea(
        areas,
        "navigation and getting lost",
        activeModules([
            [navigationCheckActive, get("navigation.check")],
            [navigationOutcomeActive, get("navigation.outcome")]
        ]),
        true);
    addArea(
        areas,
        "encounter checks",
        activeModules([
            [encounterCadenceActive, get("encounters.cadence")],
            [encounterScheduleActive, get("encounters.schedule")]
        ]),
        true);
    addArea(
        areas,
        "survival, resources, and expedition effects",
        activeModules([
            [resourcesActive, get("survival.resources")],
            [foragingActive, get("exploration.foraging")],
            [campingActive, get("survival.camping")],
            [forcedTravelActive, get("time.forced-travel")],
            [exposureActive, get("survival.exposure")],
            [effectsActive, get("effects.expedition")]
        ]),
        true);
    addArea(
        areas,
        "journey stages and events",
        activeModules([
            [journeyProcessActive, get("journey.process")],
            [journeyEventsActive, get("journey.events")]
        ]),
        true);
    addArea(
        areas,
        "travel roles and activities",
        activeModules([[activitiesActive, get("party.activities")]]),
        true);
    addArea(
        areas,
        "automatic result generation",
        activeModules([[helpersActive, get("procedure.helpers")]]),
        false);

    const travelActive =
        timeActive || movementResolutionActive || hexProgressActive || movementBudgetActive || terrainActive;
    const navigationActive = navigationCheckActive || navigationOutcomeActive;
    const survivalActive =
        resourcesActive || foragingActive || campingActive || forcedTravelActive || exposureActive || effectsActive;

    const bestFor = journeyProcessActive
        ? "You want travel to run primarily as a staged journey or challenge with explicit progress and events."
        : travelActive && navigationActive
            ? "You want travel where route movement and navigation outcomes such as getting lost, veering, or recovery matter."
            : movementBudgetActive && terrainActive && (activitiesActive || survivalActive)
                ? `You want pace- or budget-based travel where ${listPhrase([
                    "terrain",
                    activitiesActive ? "party activities" : null,
                    survivalActive ? "extended-travel or survival consequences" : null
                ])} matter.`
                : movementBudgetActive && terrainActive
                    ? "You want travel driven by a movement or pace budget with terrain and route adjustments."
                    : movementResolutionActive
                        ? "You want repeated distance or cell travel without a getting-lost and recovery subsystem."
                        : movementBudgetActive
                            ? "You want travel driven by an explicit movement, activity, or progress budget."
                            : timeActive
                                ? "You mainly need repeating travel-time bookkeeping and a small foundation you can extend."
                                : "The active rule areas already match the exploration procedure you intend to run.";

    const areaCount = areas.length;
    const breadth = areaCount <= 2 ? "Focused" : areaCount <= 4 ? "Moderate" : "Broad";
    const active = areas.flatMap(area => area.modules);
    const automation = automationSummary(active, procedure.isExecutable === false);
    const caution = procedure.isExecutable === false
        ? "Some configured rules are reference or manual only; Hex Crawl can not run this entire ruleset automatically."
        : active.some(module => module.automationLevel === "Manual")
            ? "This ruleset includes manual tracking or result entry."
            : null;
    return {
        bestFor,
        manage: areaCount > 0
            ? areas.map(describeArea).join(", ")
            : "only the ruleset-specific rules shown in details",
        breadth,
        areaCount,
        summaryFacts: [
            {
                label: "Workflow",
                value: journeyProcessActive
                    ? "Staged journey"
                    : travelActive
                        ? "Spatial travel"
                        : timeActive
                            ? "Time / interval"
                            : "Ruleset-specific"
            },
            {
                label: "Travel",
                value: terrainActive
                    ? "Terrain / route adjusted"
                    : movementBudgetActive
                        ? "Pace / budget based"
                        : movementResolutionActive
                            ? "Distance / cell travel"
                            : "Not used"
            },
            {
                label: "Navigation",
                value: navigationOutcomeActive
                    ? "Getting lost / recovery"
                    : navigationCheckActive
                        ? "Route checks"
                        : "Not used"
            },
            {
                label: "Table handling",
                value: automation
            }
        ],
        caution
    };
}

function automationSummary(modules: ProcedureModule[], nonExecutable: boolean): string {
    if (nonExecutable) return "Reference / manual";
    if (modules.length === 0) return "Table-resolved";
    const modes = new Set(modules.map(module => module.automationLevel));
    const labels = [
        modes.has("Automatic") ? "Automatic" : null,
        modes.has("Assisted") ? "DM-assisted" : null,
        modes.has("Manual") ? "Manual" : null
    ].filter((value): value is string => value !== null);
    return labels.join(" + ") || "Table-resolved";
}

function addArea(
    areas: GuidanceArea[],
    label: string,
    modules: ProcedureModule[],
    annotateAutomation: boolean): void {
    if (modules.length === 0) return;
    areas.push({ label, modules, annotateAutomation });
}

function activeModules(
    candidates: Array<[boolean, ProcedureModule | null]>): ProcedureModule[] {
    return candidates
        .filter((candidate): candidate is [true, ProcedureModule] => candidate[0] && candidate[1] !== null)
        .map(([, module]) => module);
}

function describeArea(area: GuidanceArea): string {
    if (!area.annotateAutomation) return area.label;
    const modes = new Set(area.modules.map(module => module.automationLevel));
    const ordered: ProcedureAutomationLevel[] = ["Automatic", "Assisted", "Manual"];
    const labels = ordered
        .filter(mode => modes.has(mode))
        .map(mode => mode === "Automatic"
            ? "automatic"
            : mode === "Assisted"
                ? "DM-assisted"
                : "manual tracking");
    return labels.length === 0 ? area.label : `${area.label} (${labels.join(" + ")})`;
}

function bool(value: string): boolean {
    return normalized(value) === "true";
}

function activeToken(value: string): boolean {
    const token = normalized(value);
    return token !== ""
        && token !== "none"
        && token !== "disabled"
        && token !== "not-used"
        && token !== "not used"
        && token !== "false";
}

function normalized(value: string): string {
    return value.trim().toLowerCase();
}

function finiteNumber(value: string): boolean {
    if (value.trim() === "") return false;
    return Number.isFinite(Number(value));
}

function positiveNumber(value: string): boolean {
    return finiteNumber(value) && Number(value) > 0;
}

function hasListValue(value: string): boolean {
    return value
        .split(/[;,]/)
        .map(item => normalized(item))
        .some(item => activeToken(item));
}

function listPhrase(values: Array<string | null>): string {
    const present = values.filter((value): value is string => value !== null);
    if (present.length <= 1) return present[0] ?? "the configured travel rules";
    if (present.length === 2) return `${present[0]} and ${present[1]}`;
    return `${present.slice(0, -1).join(", ")}, and ${present.at(-1)}`;
}
