import assert from "node:assert/strict";
import test from "node:test";

import {
    compactParameter,
    compactRuleCatalog,
    parameterDefinitions,
    procedureParameterFacts,
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
