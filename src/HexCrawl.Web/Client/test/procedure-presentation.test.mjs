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


test("all 19 authoring modules have a semantic table presentation", () => {
    const rules = compactRuleCatalog();
    assert.equal(rules.length, 19);
    for (const rule of rules) {
        const parameters = rule.moduleKey === "procedure.helpers"
            ? { "travel.enabled": "true", "travel.diceCount": "2", "travel.dieSides": "6", "travel.modifier": "3" }
            : { exampleValue: "configured" };
        const sections = procedurePresentationSections(rule.moduleKey, parameters);
        assert.ok(sections.length > 0, rule.moduleKey);
        assert.ok(sections.flatMap(section => section.facts).length > 0, rule.moduleKey);
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
