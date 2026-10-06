import type { ProcedureModuleComposer, ProcedureParameterDefinition } from "../../procedure-composer-types";

export type CompactRuleDescriptor = {
    moduleKey: string;
    group: string;
    label: string;
    description: string;
    order: number;
};

export type CompactParameterPresentation = {
    label: string;
    help: string | null;
    control: "duration" | "boolean" | "number" | "select" | "text" | "key-list" | "mapping" | "summary";
    choices?: Array<{ value: string; label: string }>;
};

const rules: CompactRuleDescriptor[] = [
    { moduleKey: "time.interval", group: "Travel flow", label: "Travel period", description: "Sets the repeating turn, watch, or travel period used by the procedure.", order: 10 },
    { moduleKey: "party.activities", group: "Travel flow", label: "Travel activities", description: "Defines how party members or roles take on expedition activities.", order: 20 },
    { moduleKey: "movement.budget", group: "Travel flow", label: "Available movement", description: "Determines how much movement the party has before terrain and route adjustments.", order: 30 },
    { moduleKey: "navigation.check", group: "Travel flow", label: "Navigation checks", description: "Determines whether and how route checks are made while traveling.", order: 40 },
    { moduleKey: "navigation.outcome", group: "Travel flow", label: "Getting lost and recovery", description: "Defines what happens when navigation fails and how the party recognizes or corrects its course.", order: 45 },
    { moduleKey: "movement.terrain", group: "Travel flow", label: "Terrain and routes", description: "Adjusts movement for terrain, routes, and weather.", order: 50 },
    { moduleKey: "movement.resolution", group: "Travel flow", label: "Resolve movement", description: "Defines whether travel advances by distance or whole cells and how actual distance is resolved.", order: 60 },
    { moduleKey: "movement.hex-progress", group: "Travel flow", label: "Cell progress", description: "Tracks partial progress and direction changes when movement crosses map cells.", order: 65 },
    { moduleKey: "encounters.cadence", group: "Travel flow", label: "Encounter checks", description: "Sets the ordinary cadence for encounter checks.", order: 70 },
    { moduleKey: "encounters.schedule", group: "Travel flow", label: "Encounter schedule", description: "Adds contextual or scheduled encounter checks beyond a simple cadence.", order: 75 },
    { moduleKey: "survival.resources", group: "Survival & resources", label: "Food, water, and supplies", description: "Tracks resource consumption during expedition play.", order: 100 },
    { moduleKey: "exploration.foraging", group: "Survival & resources", label: "Foraging", description: "Defines how the party searches for expedition resources and what it costs.", order: 110 },
    { moduleKey: "survival.camping", group: "Survival & resources", label: "Camping", description: "Defines camp procedure, time cost, and watch behavior.", order: 120 },
    { moduleKey: "time.forced-travel", group: "Survival & resources", label: "Forced travel", description: "Defines when ordinary travel becomes forced and what resolves each extension.", order: 130 },
    { moduleKey: "survival.exposure", group: "Survival & resources", label: "Environmental exposure", description: "Defines which environmental conditions require exposure resolution and who is affected.", order: 135 },
    { moduleKey: "effects.expedition", group: "Survival & resources", label: "Persistent effects", description: "Defines accumulating expedition conditions and how they recover.", order: 140 },
    { moduleKey: "journey.process", group: "Journey process", label: "Multi-stage journey", description: "Defines journey stages, progress, roles, transitions, and completion.", order: 200 },
    { moduleKey: "journey.events", group: "Journey process", label: "Journey events", description: "Defines when journey events occur and how they affect the expedition.", order: 210 },
    { moduleKey: "procedure.helpers", group: "Procedure support", label: "Resolution helpers", description: "Optional procedure-defined assistance for travel, navigation, and encounters.", order: 300 }
];

const ruleMap = new Map(rules.map(rule => [rule.moduleKey, rule]));

const labels: Record<string, string> = {
    durationTicks: "Travel period",
    travelResolution: "Movement method",
    actualDistanceResolution: "Actual distance",
    tracksIntraHexProgress: "Track partial cell progress",
    startingExitProgressFactor: "Starting exit progress",
    nearExitProgressFactor: "Nearby exit progress",
    farExitProgressFactor: "Far exit progress",
    backExitProgressFactor: "Back exit progress",
    directionChangesCostProgress: "Direction changes cost progress",
    directionChangeProgressCostFactor: "Direction-change cost",
    supportsDeliberateDoubleBack: "Allow deliberate double-back",
    usesNavigationChecks: "Use navigation checks",
    usesPersistentVeer: "Keep an incorrect course until corrected",
    cadence: "Check cadence",
    budgetModel: "Movement budget",
    baseBudget: "Base movement",
    budgetUnit: "Movement unit",
    limitingScope: "Whose movement limits the party",
    adjustmentModel: "Terrain adjustment",
    terrainAdjustments: "Terrain costs",
    routeAdjustmentModel: "Route effect",
    weatherAdjustmentModel: "Weather effect",
    assignmentScope: "Assign activities by",
    activityBudgetModel: "Activity allowance",
    activityKeys: "Available activities",
    roleKeys: "Travel roles",
    checkTriggerModel: "When to check",
    failureStateModel: "On failure",
    directionalErrorModel: "Off-course direction",
    recognitionModel: "Recognizing the error",
    reorientationModel: "Getting back on course",
    scheduleModel: "Schedule",
    travelChecksPerInterval: "Checks while traveling",
    campCheck: "Check while camped",
    terrainProbabilityModel: "Terrain influence",
    resourceKinds: "Tracked resources",
    inventoryModel: "Inventory tracking",
    consumptionModel: "Consumption",
    consumptionInterval: "Consume resources",
    resolutionModel: "Resolution",
    timeCost: "Time cost",
    timeUnit: "Time unit",
    movementTradeoff: "Movement tradeoff",
    watchModel: "Camp watches",
    normalTravelLimit: "Ordinary travel limit",
    limitUnit: "Limit unit",
    checkModel: "Resolution",
    failureConsequence: "On failure",
    effectKinds: "Effects",
    accumulationModel: "Accumulation",
    recoveryModel: "Recovery",
    scope: "Applies to",
    triggerModel: "When events occur",
    triggerSources: "Event triggers",
    linkMode: "Journey link",
    targetingModel: "Who resolves it",
    terrainInfluence: "Terrain influence",
    consequenceModel: "Consequences",
    requiresResolvedTrigger: "Require event resolution",
    blocksRelevantTravelWhileResolutionRequired: "Pause relevant travel until resolved",
    stageModel: "Stages",
    progressModel: "Progress",
    completionModel: "Completion",
    roleDriven: "Use journey roles",
    stageTransitionModel: "Stage transitions",
    progressKind: "Progress type",
    progressUnit: "Progress unit",
    stageKeys: "Journey stages",
    progressFloor: "Minimum progress",
    progressCeiling: "Maximum progress",
    allowNegativeProgress: "Allow lost progress",
    roleAssignmentModel: "Role assignment",
    intervalIntegrationModel: "Travel integration",
    dimensions: "Exposure conditions",
    evaluationModel: "Exposure resolution",
    evaluationInterval: "Exposure cadence",
    targetScope: "Who is affected",
    "travel.enabled": "Resolve travel distance with a roll",
    "travel.diceCount": "Travel dice count",
    "travel.dieSides": "Travel die sides",
    "travel.modifier": "Travel roll modifier",
    "travel.distanceFactor": "Distance per travel-roll point",
    "navigation.enabled": "Use a navigation roll helper",
    "navigation.diceCount": "Navigation dice count",
    "navigation.dieSides": "Navigation die sides",
    "navigation.modifier": "Navigation roll modifier",
    "encounter.enabled": "Use an encounter roll helper",
    "encounter.diceCount": "Encounter dice count",
    "encounter.dieSides": "Encounter die sides",
    "encounter.modifier": "Encounter roll modifier",
    "encounter.wanderingResults": "Wandering encounter results",
    "encounter.keyedLocationResults": "Keyed-location encounter results",
    "encounter.timingSlots": "Encounter timing slots"
};

const choiceSets: Record<string, Array<{ value: string; label: string }>> = {
    "movement.resolution.travelResolution": choices("ContinuousDistance", "HexSteps"),
    "movement.resolution.actualDistanceResolution": choices("Fixed", "VariableResolved"),
    "encounters.cadence.cadence": choices("None", "PerWatch", "PerDay"),
    "movement.budget.budgetModel": choices(
        "fixed-per-interval", "fixed-per-day", "distance-per-hour", "speed-and-pace",
        "speed-derived-distance", "speed-derived-activities", "activity-and-distance",
        "movement-points", "quarter-day-activities", "journey-progress"),
    "movement.budget.budgetUnit": choices(
        "interval", "hour", "watch", "travel-day", "travel-hours", "daily-movement-budget",
        "hexploration-activity", "quarter-day", "journey-leg"),
    "movement.budget.limitingScope": choices("party", "party-limiting", "slowest-traveler", "guide"),
    "movement.terrain.adjustmentModel": choices(
        "multiplier", "distance-per-hour-multiplier", "movement-points-per-distance",
        "activity-cost", "hexes-per-quarter-day", "maximum-pace", "terrain-difficulty"),
    "movement.terrain.routeAdjustmentModel": choices(
        "none", "manual", "road-assistance", "road-multiplier", "road-improves-one-step",
        "good-road-improves-one-step", "route-improves-cost", "route-may-ignore-terrain"),
    "movement.terrain.weatherAdjustmentModel": choices(
        "manual", "environment-specific", "movement-mode-specific", "weather-multiplier"),
    "party.activities.assignmentScope": choices("participant", "party", "role"),
    "party.activities.activityBudgetModel": choices(
        "per-interval", "per-watch", "daily-activity-budget", "quarter-day", "travel-compatible", "journey-role"),
    "navigation.outcome.checkTriggerModel": choices(
        "manual-or-procedure", "contextual-getting-lost", "daily-terrain-or-context",
        "hourly-poor-visibility-or-terrain", "lead-way-per-travel-activity", "per-watch-when-navigation-required"),
    "navigation.outcome.failureStateModel": choices("lost-state", "lost-until-recognized", "mishap"),
    "navigation.outcome.directionalErrorModel": choices(
        "manual-off-course", "persistent-veer", "random-direction", "mishap-defined", "rules-defined"),
    "navigation.outcome.recognitionModel": choices(
        "manual", "boundary-check", "mapping-support", "periodic-check", "procedure-check", "procedure-defined"),
    "navigation.outcome.reorientationModel": choices("manual", "procedure-check", "procedure-defined", "rules-defined"),
    "encounters.schedule.scheduleModel": choices(
        "cadence-backed", "daily-terrain-sensitive", "manual-contextual", "travel-and-camp"),
    "encounters.schedule.terrainProbabilityModel": choices("none", "manual", "terrain-tagged"),
    "survival.resources.inventoryModel": choices("counted", "abstract", "supply-die", "external/manual"),
    "survival.resources.consumptionModel": choices("manual", "fixed-per-person", "daily-supplies", "usage-roll"),
    "survival.resources.consumptionInterval": choices(
        "interval", "watch", "travel-day", "expedition-day", "quarter-day-or-use"),
    "exploration.foraging.resolutionModel": choices(
        "manual-check", "procedure-check", "skill-check", "subsist-activity", "activity-check"),
    "exploration.foraging.timeUnit": choices(
        "activity", "hexploration-activity", "movement-rate", "quarter-day", "travel-day", "watch", "watch-activity"),
    "exploration.foraging.movementTradeoff": choices("none", "half-speed", "half-or-full-day", "replaces-activity"),
    "survival.camping.resolutionModel": choices("manual-camp", "fortify-camp-activity", "procedure-check"),
    "survival.camping.timeUnit": choices("activity", "night", "quarter-day", "watch", "watch-activity"),
    "survival.camping.watchModel": choices(
        "manual", "assigned-lookout", "camp-encounter-check", "campaign-defined", "keep-watch-activity"),
    "time.forced-travel.limitUnit": choices("hours", "intervals", "quarter-days", "watches"),
    "time.forced-travel.checkModel": choices(
        "manual-check", "endurance-check", "escalating-check", "escalating-constitution-save"),
    "time.forced-travel.failureConsequence": choices(
        "fatigue", "exhaustion", "fatigue-and-nonlethal-effect", "fatigue-or-mishap"),
    "effects.expedition.accumulationModel": choices("levels", "per-failed-check", "journey-events", "procedure-defined"),
    "effects.expedition.recoveryModel": choices(
        "rest", "rest-and-sleep", "rules-defined-rest", "safe-prolonged-rest", "safe-rest"),
    "effects.expedition.scope": choices("participant", "party", "mount", "vehicle", "expedition"),
    "journey.events.triggerModel": choices("manual-or-landmark", "per-watch-or-landmark", "guide-progress-test"),
    "journey.events.linkMode": choices("standalone", "process-linked", "both"),
    "journey.events.targetingModel": choices("explicit-target", "travel-role"),
    "journey.events.terrainInfluence": choices("manual", "difficulty", "difficulty-and-road"),
    "journey.events.consequenceModel": choices("event", "event-and-fatigue"),
    "journey.process.stageModel": choices("manual-stages", "route-then-events-then-arrival"),
    "journey.process.progressModel": choices("progress-points", "guide-marching-progress"),
    "journey.process.completionModel": choices("explicit-completion", "reach-destination", "final-stage-completion"),
    "journey.process.stageTransitionModel": choices("explicit", "sequential"),
    "journey.process.progressKind": choices("numeric"),
    "journey.process.progressUnit": choices("progress-points", "journey-progress"),
    "journey.process.roleAssignmentModel": choices("current-at-resolution"),
    "journey.process.intervalIntegrationModel": choices("none", "completed-watch-resolution-opportunity"),
    "survival.exposure.evaluationModel": choices("resolved-check"),
    "survival.exposure.targetScope": choices("participant", "party", "mount", "vehicle", "expedition"),
    "survival.exposure.consequenceModel": choices("resolved-structured-consequence")
};

const booleanKeys = new Set([
    "tracksIntraHexProgress", "directionChangesCostProgress", "supportsDeliberateDoubleBack",
    "usesNavigationChecks", "usesPersistentVeer", "campCheck", "roleDriven", "allowNegativeProgress",
    "requiresResolvedTrigger", "blocksRelevantTravelWhileResolutionRequired"
]);

const numberKeys = new Set([
    "startingExitProgressFactor", "nearExitProgressFactor", "farExitProgressFactor", "backExitProgressFactor",
    "directionChangeProgressCostFactor", "baseBudget", "travelChecksPerInterval", "timeCost", "normalTravelLimit",
    "progressFloor", "progressCeiling"
]);

const keyListKeys = new Set([
    "activityKeys", "roleKeys", "resourceKinds", "effectKinds", "triggerSources", "dimensions", "stageKeys"
]);
const mappingKeys = new Set(["terrainAdjustments"]);
const textKeys = new Set(["evaluationInterval"]);

export function compactRuleCatalog(): CompactRuleDescriptor[] {
    return [...rules];
}

export function compactRule(moduleKey: string): CompactRuleDescriptor | null {
    return ruleMap.get(moduleKey) ?? null;
}

export function compactParameter(
    key: string,
    definition: ProcedureParameterDefinition,
    moduleKey: string | null = null): CompactParameterPresentation | null {
    const label = labels[key];
    if (!label) return null;
    if (key === "durationTicks") {
        return { label, help: definition.description, control: "duration" };
    }
    if (booleanKeys.has(key) || definition.type === "boolean") {
        return { label, help: definition.description, control: "boolean" };
    }
    const moduleChoiceKey = moduleKey ? `${moduleKey}.${key}` : key;
    if (choiceSets[moduleChoiceKey]) {
        return { label, help: definition.description, control: "select", choices: choiceSets[moduleChoiceKey] };
    }
    if (numberKeys.has(key) || definition.type === "number" || definition.type === "integer" || definition.type === "decimal") {
        return { label, help: definition.description, control: "number" };
    }
    if (keyListKeys.has(key) || definition.type === "key-list") {
        return { label, help: definition.description, control: "key-list" };
    }
    if (mappingKeys.has(key) || definition.type === "mapping") {
        return { label, help: definition.description, control: "mapping" };
    }
    if (textKeys.has(key) || definition.type === "string") {
        return { label, help: definition.description, control: "text" };
    }
    // Unknown token/model fields remain readable without leaking implementation keys.
    // Current ordinary tabletop enum domains are declared above and remain directly editable.
    return { label, help: definition.description, control: "summary" };
}

export function compactModuleSummary(module: ProcedureModuleComposer): string {
    const descriptor = compactRule(module.moduleKey);
    const fragments: string[] = [];
    for (const [key, definition] of parameterDefinitions(module)) {
        const presentation = compactParameter(key, definition, module.moduleKey);
        if (!presentation) continue;
        const value = module.parameters[key] ?? definition.defaultValue ?? "";
        if (!value) continue;
        if (key === "durationTicks") {
            fragments.push(formatDurationTicks(value));
            continue;
        }
        if (presentation.control === "boolean") {
            if (value.toLowerCase() === "true") fragments.push(presentation.label);
            continue;
        }
        if (fragments.length < 3) {
            fragments.push(`${presentation.label}: ${friendlyStoredValue(key, value)}`);
        }
    }
    return fragments.length > 0
        ? fragments.join(" · ")
        : descriptor?.description ?? module.purpose;
}

export function parameterDefinitions(
    module: ProcedureModuleComposer): Array<[string, ProcedureParameterDefinition]> {
    const definitions = new Map<string, ProcedureParameterDefinition>();
    for (const [key, value] of Object.entries(module.configurationSchema)) definitions.set(key, value);
    for (const [key, value] of Object.entries(module.mechanic.parameterSchema)) definitions.set(key, value);
    for (const key of Object.keys(module.parameters)) {
        if (!definitions.has(key)) {
            definitions.set(key, {
                type: storedParameterType(key, module.parameters[key]),
                required: false,
                description: "Stored parameter not described by the pinned schema. It is preserved losslessly.",
                defaultValue: null
            });
        }
    }
    return [...definitions.entries()];
}

function storedParameterType(key: string, value: string): string {
    if (key.endsWith(".enabled") || value === "true" || value === "false") return "boolean";
    if (key.endsWith(".diceCount") || key.endsWith(".dieSides") || key.endsWith(".modifier") || key.endsWith(".timingSlots")) {
        return "integer";
    }
    if (key.endsWith(".distanceFactor")) return "number";
    return "string";
}

export function friendlyStoredValue(key: string, value: string): string {
    const choice = choiceSets[key]?.find(option => option.value === value);
    if (choice) return choice.label;
    if (value === "true") return "Yes";
    if (value === "false") return "No";
    if (keyListKeys.has(key)) return value.split(";").map(friendlyToken).join(", ");
    if (mappingKeys.has(key)) return value.split(";").map(part => part.replace("=", ": ")).join(", ");
    return friendlyToken(value);
}

export function ticksToDuration(value: string): { amount: number; unit: "minutes" | "hours" | "days" } | null {
    const ticks = Number(value);
    if (!Number.isFinite(ticks) || ticks < 0) return null;
    const ticksPerMinute = 600_000_000;
    const minutes = ticks / ticksPerMinute;
    if (minutes >= 1440 && Number.isInteger(minutes / 1440)) return { amount: minutes / 1440, unit: "days" };
    if (minutes >= 60 && Number.isInteger(minutes / 60)) return { amount: minutes / 60, unit: "hours" };
    return { amount: minutes, unit: "minutes" };
}

export function durationToTicks(amount: number, unit: "minutes" | "hours" | "days"): string {
    const multiplier = unit === "days" ? 24 * 60 : unit === "hours" ? 60 : 1;
    return Math.round(amount * multiplier * 600_000_000).toString();
}

export function formatDurationTicks(value: string): string {
    const duration = ticksToDuration(value);
    if (!duration) return "Configured travel period";
    return `${formatNumber(duration.amount)} ${duration.unit}`;
}

function choices(...values: string[]): Array<{ value: string; label: string }> {
    return values.map(value => ({ value, label: friendlyToken(value) }));
}

function friendlyToken(value: string): string {
    const spaced = value
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .replace(/[._-]+/g, " ")
        .trim();
    if (!spaced) return "Not used";
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
}
