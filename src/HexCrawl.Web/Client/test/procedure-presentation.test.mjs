import assert from "node:assert/strict";
import test from "node:test";

import {
    compactParameter,
    compactRuleCatalog,
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
});

test("Compact exposes current ordinary enum domains as editable selects", () => {
    const cases = [
        ["movement.budget", "budgetModel", "journey-progress"],
        ["navigation.outcome", "recognitionModel", "procedure-check"],
        ["time.forced-travel", "checkModel", "escalating-constitution-save"],
        ["survival.resources", "inventoryModel", "supply-die"],
        ["journey.process", "stageTransitionModel", "sequential"],
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

test("Compact includes environmental exposure as a survival rule", () => {
    const exposure = compactRuleCatalog().find(rule => rule.moduleKey === "survival.exposure");
    assert.ok(exposure);
    assert.equal(exposure.group, "Survival & resources");
    assert.equal(exposure.label, "Environmental exposure");
});
