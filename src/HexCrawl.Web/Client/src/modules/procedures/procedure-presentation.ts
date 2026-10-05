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
    allowNegativeProgress: "Allow lost progress",
    roleAssignmentModel: "Role assignment",
    intervalIntegrationModel: "Travel integration"
};

const choiceSets: Record<string, Array<{ value: string; label: string }>> = {
    travelResolution: [
        { value: "ContinuousDistance", label: "Continuous distance" },
        { value: "HexSteps", label: "Whole cell steps" }
    ],
    actualDistanceResolution: [
        { value: "Fixed", label: "Fixed / authoritative distance" },
        { value: "VariableResolved", label: "Resolve variable distance" }
    ],
    cadence: [
        { value: "None", label: "No routine check" },
        { value: "PerWatch", label: "Each travel period" },
        { value: "PerDay", label: "Each day" }
    ],
    assignmentScope: [
        { value: "participant", label: "Participant" },
        { value: "party", label: "Party" },
        { value: "role", label: "Role" }
    ]
};

const booleanKeys = new Set([
    "tracksIntraHexProgress", "directionChangesCostProgress", "supportsDeliberateDoubleBack",
    "usesNavigationChecks", "usesPersistentVeer", "campCheck", "roleDriven", "allowNegativeProgress",
    "requiresResolvedTrigger", "blocksRelevantTravelWhileResolutionRequired"
]);

const numberKeys = new Set([
    "startingExitProgressFactor", "nearExitProgressFactor", "farExitProgressFactor", "backExitProgressFactor",
    "directionChangeProgressCostFactor", "baseBudget", "travelChecksPerInterval", "timeCost", "normalTravelLimit"
]);

const keyListKeys = new Set(["activityKeys", "roleKeys", "resourceKinds", "effectKinds", "triggerSources"]);
const mappingKeys = new Set(["terrainAdjustments"]);

export function compactRuleCatalog(): CompactRuleDescriptor[] {
    return [...rules];
}

export function compactRule(moduleKey: string): CompactRuleDescriptor | null {
    return ruleMap.get(moduleKey) ?? null;
}

export function compactParameter(
    key: string,
    definition: ProcedureParameterDefinition): CompactParameterPresentation | null {
    const label = labels[key];
    if (!label) return null;
    if (key === "durationTicks") {
        return { label, help: definition.description, control: "duration" };
    }
    if (booleanKeys.has(key) || definition.type === "boolean") {
        return { label, help: definition.description, control: "boolean" };
    }
    if (choiceSets[key]) {
        return { label, help: definition.description, control: "select", choices: choiceSets[key] };
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
    // Token/model fields remain readable in Compact but are intentionally edited in Advanced
    // until the generic catalog declares a safe choice set. Compact never exposes the raw key.
    return { label, help: definition.description, control: "summary" };
}

export function compactModuleSummary(module: ProcedureModuleComposer): string {
    const descriptor = compactRule(module.moduleKey);
    const fragments: string[] = [];
    for (const [key, definition] of parameterDefinitions(module)) {
        const presentation = compactParameter(key, definition);
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
    return [...definitions.entries()];
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
