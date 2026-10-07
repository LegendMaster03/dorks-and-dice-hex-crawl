import assert from "node:assert/strict";
import test from "node:test";

import {
    compactParameter,
    compactRuleCatalog,
    parameterDefinitions,
    procedureParameterFacts,
    procedurePresentationSections,
    friendlyStoredValue,
    durationToTicks,
    formatDurationTicks,
    ticksToDuration
} from "../.test-dist/modules/procedures/procedure-presentation.js";

const enumDefinition = {
    type: "enum",
    required: true,
    description: null,
    defaultValue: null
};

test("Compact converts canonical interval ticks to tabletop duration controls", () => {
    const ticks = durationToTicks(4, "hours");
    assert.equal(ticks, "144000000000");
    assert.deepEqual(ticksToDuration(ticks), { amount: 4, unit: "hours" });
    assert.equal(formatDurationTicks(ticks), "4 hours");
    assert.equal(formatDurationTicks(durationToTicks(1, "days")), "1 day");

    const presentation = compactParameter("durationTicks", {
        type: "integer",
        required: true,
        description: "Serialized duration ticks.",
        defaultValue: ticks
    }, "time.interval");
    assert.equal(presentation?.control, "duration");
    assert.equal(presentation?.help, "Set the length of one travel period.");
});

test("Compact starting-exit help describes the runtime exit requirement rather than prepaid progress", () => {
    const presentation = compactParameter("startingExitProgressFactor", {
        type: "number",
        required: true,
        description: null,
        defaultValue: "0.5"
    }, "movement.hex-progress");

    assert.equal(presentation?.control, "number");
    assert.match(presentation?.help ?? "", /required to leave the starting hex/i);
    assert.match(presentation?.help ?? "", /hex center distance/i);
    assert.doesNotMatch(presentation?.help ?? "", /already counted/i);
});


test("Compact exposes current ordinary enum domains as editable selects", () => {
    const cases = [
        ["movement.budget", "budgetModel", "journey-progress"],
        ["navigation.outcome", "recognitionModel", "procedure-check"],
        ["time.forced-travel", "checkModel", "escalating-constitution-save"],
        ["survival.resources", "inventoryModel", "supply-die"],
        ["journey.events", "linkMode", "both"],
        ["journey.process", "stageTransitionModel", "sequential"],
        ["journey.process", "completionModel", "reach-destination"],
        ["survival.exposure", "targetScope", "party"]
    ];

    for (const [moduleKey, key, expectedValue] of cases) {
        const presentation = compactParameter(key, enumDefinition, moduleKey);
        assert.equal(presentation?.control, "select", `${moduleKey}.${key}`);
        assert.ok(
            presentation?.choices?.some(choice => choice.value === expectedValue),
            `${moduleKey}.${key} should include ${expectedValue}`);
    }
});

test("Compact exposes Phase 12 journey stage and progress controls", () => {
    const stageKeys = compactParameter("stageKeys", {
        type: "key-list",
        required: false,
        description: null,
        defaultValue: null
    }, "journey.process");
    const progressFloor = compactParameter("progressFloor", {
        type: "number",
        required: false,
        description: null,
        defaultValue: null
    }, "journey.process");
    const progressCeiling = compactParameter("progressCeiling", {
        type: "number",
        required: false,
        description: null,
        defaultValue: null
    }, "journey.process");

    assert.equal(stageKeys?.control, "key-list");
    assert.equal(progressFloor?.control, "number");
    assert.equal(progressCeiling?.control, "number");
});

test("Resolution helpers expose editable generic fields including legacy pinned parameters", () => {
    const enabled = compactParameter("travel.enabled", {
        type: "boolean",
        required: true,
        description: "Travel helper",
        defaultValue: null
    }, "procedure.helpers");
    const resultSet = compactParameter("encounter.wanderingResults", {
        type: "string",
        required: false,
        description: "Encounter results",
        defaultValue: null
    }, "procedure.helpers");

    assert.equal(enabled?.control, "boolean");
    assert.equal(resultSet?.control, "text");

    const definitions = parameterDefinitions({
        configurationSchema: {},
        mechanic: { parameterSchema: {} },
        parameters: {
            "travel.enabled": "true",
            "travel.diceCount": "2",
            "travel.distanceFactor": "0.1"
        }
    });
    const byKey = new Map(definitions);
    assert.equal(byKey.get("travel.enabled")?.type, "boolean");
    assert.equal(byKey.get("travel.diceCount")?.type, "integer");
    assert.equal(byKey.get("travel.distanceFactor")?.type, "number");
});

test("Compact includes environmental exposure as a survival rule", () => {
    const exposure = compactRuleCatalog().find(rule => rule.moduleKey === "survival.exposure");
    assert.ok(exposure);
    assert.equal(exposure.group, "Survival & resources");
    assert.equal(exposure.label, "Environmental exposure");
});


test("Inspect and Compact share semantic duration, boolean, progress, and enum formatting", () => {
    assert.deepEqual(
        procedureParameterFacts("time.interval", { durationTicks: "144000000000" }),
        [{ label: "Travel period", value: "4 hours" }]);

    assert.deepEqual(
        procedureParameterFacts("navigation.check", {
            usesNavigationChecks: "true",
            usesPersistentVeer: "false"
        }),
        [
            { label: "Checks required", value: "Yes" },
            { label: "Persistent off-course state", value: "No" }
        ]);

    assert.deepEqual(
        procedureParameterFacts("movement.hex-progress", {
            startingExitProgressFactor: "0.5",
            farExitProgressFactor: "1"
        }),
        [
            { label: "Starting exit progress", value: "50% of a cell crossing" },
            { label: "Far exit progress", value: "100% of a cell crossing" }
        ]);

    assert.equal(
        friendlyStoredValue("budgetModel", "journey-progress", "movement.budget"),
        "Journey progress");
});

test("Compact terminology presents journeys and automation as tabletop concepts", () => {
    const journey = compactRuleCatalog().find(rule => rule.moduleKey === "journey.process");
    const events = compactRuleCatalog().find(rule => rule.moduleKey === "journey.events");
    const automation = compactRuleCatalog().find(rule => rule.moduleKey === "procedure.helpers");

    assert.equal(journey?.group, "Journey & events");
    assert.equal(events?.group, "Journey & events");
    assert.equal(automation?.group, "Automation");
    assert.equal(automation?.label, "Automatic resolution");
    assert.match(automation?.description ?? "", /generate supported travel, navigation, and encounter results/i);
});


test("Compact travel catalog order is presentation-only rather than claimed execution authority", () => {
    const travel = compactRuleCatalog()
        .filter(rule => rule.group === "Travel flow")
        .sort((left, right) => left.order - right.order);

    assert.ok(travel.length > 0);
    assert.equal(new Set(travel.map(rule => rule.order)).size, travel.length);
});


test("semantic value formatting preserves decimal numbers and decimal mappings", () => {
    assert.equal(friendlyStoredValue("distanceFactor", "0.5"), "0.5");
    assert.equal(
        friendlyStoredValue("terrainAdjustments", "rough=0.5;severe=0.25"),
        "Rough: 0.5, Severe: 0.25");
    assert.equal(friendlyStoredValue("baseBudget", "1"), "1");
});


test("all 19 authoring modules use their semantic layouts with representative real parameters", () => {
    const samples = {
        "time.interval": { durationTicks: "144000000000" },
        "party.activities": { assignmentScope: "participant", roleKeys: "navigator;scout", activityBudgetModel: "one-per-watch", activityKeys: "navigate;scout" },
        "movement.budget": { budgetModel: "watch-distance", baseBudget: "6", budgetUnit: "mi", limitingScope: "party", travelModeKeys: "normal;fast;slow" },
        "movement.terrain": { adjustmentModel: "multiplier", terrainAdjustments: "clear=1;rough=0.75;difficult=0.5", routeAdjustmentModel: "route-improves-cost", weatherAdjustmentModel: "symbolic" },
        "movement.resolution": { travelResolution: "continuous-distance", actualDistanceResolution: "fixed" },
        "movement.hex-progress": { tracksIntraHexProgress: "true", startingExitProgressFactor: "0", nearExitProgressFactor: "0.5", farExitProgressFactor: "1", backExitProgressFactor: "1", directionChangesCostProgress: "true", directionChangeProgressCostFactor: "0.5", supportsDeliberateDoubleBack: "true" },
        "navigation.check": { usesNavigationChecks: "true", usesPersistentVeer: "true" },
        "navigation.outcome": { checkTriggerModel: "per-watch", failureStateModel: "lost", directionalErrorModel: "persistent-veer", recognitionModel: "procedure-check", reorientationModel: "resolved-check" },
        "encounters.cadence": { cadence: "per-watch" },
        "encounters.schedule": { scheduleModel: "travel-and-camp", travelChecksPerInterval: "1", campCheck: "true", terrainProbabilityModel: "table" },
        "survival.resources": { resourceKinds: "food;water;supplies", inventoryModel: "counted", consumptionModel: "resolved-quantity", consumptionInterval: "travel-day" },
        "exploration.foraging": { timeCost: "1", timeUnit: "watch", movementTradeoff: "half-movement", resolutionModel: "skill-check" },
        "survival.camping": { timeCost: "1", timeUnit: "watch", resolutionModel: "fortify-camp", watchModel: "assigned" },
        "time.forced-travel": { normalTravelLimit: "8", limitUnit: "hours", checkModel: "escalating-constitution-save", failureConsequence: "fatigue" },
        "survival.exposure": { dimensions: "temperature;precipitation", evaluationInterval: "per-watch", evaluationModel: "procedure-check", targetScope: "party", consequenceModel: "fatigue" },
        "effects.expedition": { effectKinds: "fatigue;exhaustion", scope: "participant", accumulationModel: "levels", recoveryModel: "rest" },
        "journey.process": { stageModel: "ordered", stageKeys: "approach;crossing;arrival", progressModel: "accumulated", progressKind: "numeric", progressUnit: "legs", progressFloor: "0", progressCeiling: "6", allowNegativeProgress: "false", roleDriven: "true", roleAssignmentModel: "current-at-resolution", stageTransitionModel: "sequential", completionModel: "stage-completion", intervalIntegrationModel: "none" },
        "journey.events": { triggerModel: "explicit", triggerSources: "process-progress;stage-transition", linkMode: "process-linked", targetingModel: "role-or-party", terrainInfluence: "snapshot", consequenceModel: "expedition-consequence", requiresResolvedTrigger: "false", blocksRelevantTravelWhileResolutionRequired: "true" },
        "procedure.helpers": { "travel.enabled": "true", "travel.diceCount": "2", "travel.dieSides": "6", "travel.modifier": "3", "travel.distanceFactor": "0.1", "navigation.enabled": "true", "navigation.diceCount": "1", "navigation.dieSides": "20", "navigation.modifier": "0", "encounter.enabled": "true", "encounter.diceCount": "1", "encounter.dieSides": "6", "encounter.modifier": "0", "encounter.wanderingResults": "1;2", "encounter.keyedLocationResults": "6", "encounter.timingSlots": "4" }
    };
    const expectedSectionKeys = {
        "time.interval": ["timing"],
        "party.activities": ["assignment", "allowance"],
        "movement.budget": ["budget"],
        "movement.terrain": ["terrain", "context"],
        "movement.resolution": ["resolution"],
        "movement.hex-progress": ["progress", "course-change"],
        "navigation.check": ["check"],
        "navigation.outcome": ["trigger", "failure", "recovery"],
        "encounters.cadence": ["cadence"],
        "encounters.schedule": ["schedule", "context"],
        "survival.resources": ["inventory", "consumption"],
        "exploration.foraging": ["cost", "resolution"],
        "survival.camping": ["cost", "resolution"],
        "time.forced-travel": ["limit", "resolution", "consequence"],
        "survival.exposure": ["conditions", "resolution", "consequence"],
        "effects.expedition": ["effect", "application", "recovery"],
        "journey.process": ["stages", "progress", "roles", "transition"],
        "journey.events": ["trigger", "target", "resolution"],
        "procedure.helpers": ["travel", "navigation", "encounter"]
    };

    const rules = compactRuleCatalog();
    assert.equal(rules.length, 19);
    assert.equal(Object.keys(samples).length, 19);
    assert.deepEqual(
        new Set(Object.keys(samples)),
        new Set(rules.map(rule => rule.moduleKey)));

    for (const rule of rules) {
        const parameters = samples[rule.moduleKey];
        const sections = procedurePresentationSections(rule.moduleKey, parameters);
        assert.ok(sections.length > 0, rule.moduleKey);
        for (const key of expectedSectionKeys[rule.moduleKey]) {
            assert.ok(sections.some(section => section.key === key), `${rule.moduleKey} should render semantic section ${key}`);
        }
        assert.ok(
            sections.flatMap(section => section.facts).every(fact => fact.value !== ""),
            `${rule.moduleKey} should preserve representative values`);
        assert.ok(
            !sections.some(section => section.key === "configuration"),
            `${rule.moduleKey} should not fall back to a generic-only presentation`);
    }
});

test("semantic procedure presentation keeps mappings aligned and helper dice settings as formulas", () => {
    const terrain = procedurePresentationSections("movement.terrain", {
        adjustmentModel: "multiplier",
        terrainAdjustments: "clear=1;broken=0.75;difficult=0.5",
        routeAdjustmentModel: "route-improves-cost"
    });
    const terrainFact = terrain.flatMap(section => section.facts)
        .find(fact => fact.label === "Terrain costs");
    assert.deepEqual(terrainFact?.entries, [
        { label: "Clear", value: "1" },
        { label: "Broken", value: "0.75" },
        { label: "Difficult", value: "0.5" }
    ]);

    const helpers = procedurePresentationSections("procedure.helpers", {
        "travel.enabled": "true",
        "travel.diceCount": "2",
        "travel.dieSides": "6",
        "travel.modifier": "3",
        "travel.distanceFactor": "0.1",
        "navigation.enabled": "true",
        "navigation.diceCount": "1",
        "navigation.dieSides": "20",
        "navigation.modifier": "0"
    });
    assert.equal(
        helpers.find(section => section.key === "travel")?.facts.find(fact => fact.label === "Roll")?.value,
        "2d6 + 3");
    assert.equal(
        helpers.find(section => section.key === "navigation")?.facts.find(fact => fact.label === "Roll")?.value,
        "1d20");
});

test("journey presentation separates stages, progress bounds, roles, and transitions", () => {
    const sections = procedurePresentationSections("journey.process", {
        stageModel: "manual-stages",
        stageKeys: "approach;crossing;arrival",
        progressModel: "progress-points",
        progressFloor: "-2",
        progressCeiling: "8",
        allowNegativeProgress: "true",
        roleDriven: "true",
        stageTransitionModel: "sequential",
        completionModel: "final-stage-completion"
    });
    assert.deepEqual(sections.map(section => section.label), [
        "Stages",
        "Progress and bounds",
        "Roles",
        "Transitions and completion"
    ]);
    assert.deepEqual(
        sections[0].facts.find(fact => fact.label === "Journey stages")?.items,
        ["Approach", "Crossing", "Arrival"]);
});
