import assert from "node:assert/strict";
import test from "node:test";
import {
    composerSection,
    createPendingOverride,
    executionSummary,
    groupComposerModules,
    inputSourceLabel,
    parameterDefinitions,
    parseKeyListParameter,
    parseMapParameter,
    serializeKeyListParameter,
    serializeMapParameter,
    withBehavior,
    withParameter
} from "../.test-dist/modules/procedures/procedure-composer-model.js";
import { parseToolRoute } from "../.test-dist/tool-route.js";

function mechanic(overrides = {}) {
    return {
        key: "terrain-movement-policy",
        displayName: "Terrain movement policy",
        description: "Generic terrain relationships.",
        version: 1,
        executionHandler: "procedure.declarative-contract",
        automationLevel: "Assisted",
        executionSupport: "Declarative",
        inputs: [],
        outputs: ["movement.terrain-adjustment"],
        parameterSchema: {
            adjustmentModel: { type: "enum", required: true, description: null, defaultValue: null },
            terrainAdjustments: { type: "map<string>", required: true, description: null, defaultValue: null },
            activityKeys: { type: "key-list", required: true, description: null, defaultValue: null },
            enabled: { type: "boolean", required: true, description: null, defaultValue: null },
            count: { type: "integer", required: true, description: null, defaultValue: null },
            factor: { type: "number", required: true, description: null, defaultValue: null }
        },
        compatibilityTags: ["movement"],
        ...overrides
    };
}

function moduleValue(overrides = {}) {
    return {
        moduleKey: "movement.terrain",
        category: "Movement",
        displayName: "Terrain relationship",
        purpose: "Controls terrain adjustments.",
        executionStage: "movement",
        reads: [],
        produces: ["movement.terrain-adjustment"],
        requiredDependencies: [],
        optionalDependencies: [],
        presentationMetadata: {},
        mechanic: mechanic(),
        alternatives: [mechanic()],
        configurationSchema: {},
        parameters: {
            adjustmentModel: "multiplier",
            terrainAdjustments: "arctic=fast-if-appropriately-equipped;forest=slow",
            activityKeys: "travel;forage",
            enabled: "true",
            count: "1",
            factor: "0.5",
            "future.parameter": "preserved"
        },
        requiredInputs: [],
        outputs: ["movement.terrain-adjustment"],
        dependencyIssues: [],
        isModified: false,
        modificationCount: 0,
        validationIssues: [],
        ...overrides
    };
}

test("Composer routes are first-class tool routes", () => {
    assert.deepEqual(parseToolRoute("/procedures"), { kind: "procedures" });
    assert.deepEqual(
        parseToolRoute("/procedures/abc%20123"),
        { kind: "procedure", procedureId: "abc 123" });
});

test("generic categories map to the required composer areas without preset identity", () => {
    assert.equal(composerSection("Time"), "Time");
    assert.equal(composerSection("Movement"), "Movement");
    assert.equal(composerSection("Party procedure"), "Party Organization");
    assert.equal(composerSection("Navigation"), "Navigation");
    assert.equal(composerSection("Exploration"), "Exploration");
    assert.equal(composerSection("Encounters"), "Encounters");
    assert.equal(composerSection("Survival/resources"), "Survival");
    assert.equal(composerSection("Environment/effects"), "Survival");
    assert.equal(composerSection("Journey processes"), "Journey Processes");

    const groups = groupComposerModules([
        moduleValue({ category: "Journey processes", moduleKey: "journey.process" }),
        moduleValue({ category: "Time", moduleKey: "time.interval" }),
        moduleValue({ category: "Party procedure", moduleKey: "party.activities" })
    ]);
    assert.deepEqual(groups.map(group => group.section), [
        "Time",
        "Party Organization",
        "Journey Processes"
    ]);
});

test("parameter definitions cover every current schema shape and preserve unknown stored parameters", () => {
    const definitions = new Map(parameterDefinitions(moduleValue()));
    assert.equal(definitions.get("adjustmentModel")?.type, "enum");
    assert.equal(definitions.get("terrainAdjustments")?.type, "map<string>");
    assert.equal(definitions.get("activityKeys")?.type, "key-list");
    assert.equal(definitions.get("enabled")?.type, "boolean");
    assert.equal(definitions.get("count")?.type, "integer");
    assert.equal(definitions.get("factor")?.type, "number");
    assert.equal(definitions.get("future.parameter")?.type, "string");
});

test("structured terrain maps preserve symbolic generic values", () => {
    const input = "arctic=fast-if-appropriately-equipped;forest=slow;road=normal";
    const parsed = parseMapParameter(input);
    assert.deepEqual(parsed, [
        { key: "arctic", value: "fast-if-appropriately-equipped" },
        { key: "forest", value: "slow" },
        { key: "road", value: "normal" }
    ]);
    assert.equal(serializeMapParameter(parsed), input);
});

test("key-list parameters round-trip through the generic list editor", () => {
    assert.deepEqual(parseKeyListParameter("travel;forage;watch"), ["travel", "forage", "watch"]);
    assert.equal(serializeKeyListParameter(["travel", " forage ", "", "watch"]), "travel;forage;watch");
});

test("independent pending overrides for the same module receive different IDs", () => {
    const module = moduleValue();
    const first = createPendingOverride(module);
    const second = createPendingOverride(module);

    assert.notEqual(first.overrideId, second.overrideId);
    assert.match(first.overrideId, /^composer-movement\.terrain-/);
    assert.match(second.overrideId, /^composer-movement\.terrain-/);
});

test("pending behavior and parameter changes coalesce into one module override", () => {
    const module = moduleValue();
    const initial = createPendingOverride(module);
    const firstParameter = withParameter(module, initial, "terrainAdjustments", "desert=slow");
    const secondParameter = withParameter(module, firstParameter, "activityKeys", "travel;watch");
    const changedBehavior = withBehavior(module, secondParameter, "alternate-terrain-policy", 2);

    assert.equal(firstParameter.overrideId, initial.overrideId);
    assert.equal(secondParameter.overrideId, initial.overrideId);
    assert.equal(changedBehavior.overrideId, initial.overrideId);
    assert.equal(changedBehavior.moduleKey, module.moduleKey);
    assert.equal(changedBehavior.replacementMechanicKey, "alternate-terrain-policy");
    assert.equal(changedBehavior.replacementMechanicVersion, 2);
    assert.deepEqual(changedBehavior.parameters, {
        terrainAdjustments: "desert=slow",
        activityKeys: "travel;watch"
    });
});

test("dependency source labels preserve the complete generic source set", () => {
    assert.deepEqual(
        ["SelectedModule", "Dm", "OptionalProvider", "ExternalState"].map(inputSourceLabel),
        ["selected module", "DM / manual input", "optional provider", "external / runtime state"]);
});

test("execution summaries distinguish declarative, native, and unsupported mechanics", () => {
    assert.match(executionSummary(moduleValue()), /structural\/declarative/);
    assert.match(executionSummary(moduleValue({
        mechanic: mechanic({ executionSupport: "Native", automationLevel: "Automatic" })
    })), /Automatic native/);
    assert.match(executionSummary(moduleValue({
        mechanic: mechanic({ executionSupport: "Unsupported", executionHandler: "future.handler", version: 42 })
    })), /Unsupported handler\/version/);
});
