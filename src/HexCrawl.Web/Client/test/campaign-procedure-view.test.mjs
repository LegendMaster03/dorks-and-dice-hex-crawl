import assert from "node:assert/strict";
import test from "node:test";

import {
    procedureHelperMechanics,
    procedureMechanicLines,
    campaignProcedureSummary
} from "../.test-dist/campaign-procedure-view.js";

const advancedRuntime = {
    intervalHours: 4,
    travelResolution: "ContinuousDistance",
    actualDistanceResolution: "VariableResolved",
    encounterCadence: "PerWatch",
    usesNavigationChecks: true,
    usesPersistentVeer: true,
    tracksIntraHexProgress: true,
    directionChangesCostProgress: true,
    supportsDeliberateDoubleBack: true,
    startingExitProgressFactor: 0.5,
    nearExitProgressFactor: 0.5,
    farExitProgressFactor: 1,
    backExitProgressFactor: 0.5,
    directionChangeProgressCostFactor: 1 / 6,
    resolutionHelpers: {
        travel: {
            roll: { diceCount: 2, dieSides: 6, modifier: 3 },
            distanceFactorPerRollPoint: 0.1
        },
        navigation: {
            checkRoll: { diceCount: 1, dieSides: 20, modifier: 0 }
        },
        encounter: {
            checkRoll: { diceCount: 1, dieSides: 8, modifier: 0 },
            wanderingResults: [1],
            keyedLocationResults: [8],
            timingSlots: 8
        }
    }
};

const advanced = {
    procedureId: "00000000-0000-0000-0000-000000000001",
    revision: 1,
    key: "alexandrian-advanced",
    name: "Alexandrian Advanced",
    isExecutable: true,
    runtime: advancedRuntime,
    modules: []
};

test("procedure summary uses human-readable cadence labels", () => {
    assert.equal(
        campaignProcedureSummary(advanced),
        "4 h watches · resolved variable distance · navigation checks · encounters per watch");
});

test("procedure mechanics describe the configured travel, navigation, progress, and encounter helpers", () => {
    const lines = procedureMechanicLines(advanced);
    assert.equal(lines[0], "Watch: 4 h; encounter cadence per watch.");
    assert.equal(lines[1], "Travel: continuous distance with a separately resolved expected and actual distance.");
    assert.match(lines[2], /veer is persistent/);
    assert.match(lines[2], /double-back supported/);
    assert.match(lines[3], /exit factors start 0\.5, near 0\.5, far 1, back 0\.5/);
    assert.match(lines[3], /direction-change cost factor 0\.167/);
    assert.equal(
        lines[4],
        "Travel helper: actual distance = expected distance × 2d6+3 total × 0.1.");
    assert.equal(
        lines[5],
        "Navigation helper: 1d20 + the entered situational modifier vs. the DM-confirmed DC; a failed check uses the DM-confirmed non-zero veer.");
    assert.equal(
        lines[6],
        "Encounter helper: 1d8; wandering on 1, keyed location on 8; encounter time uses 1d8 equal watch slots.");
});

test("procedure mechanics do not advertise incompatible retained helper components", () => {
    const hexSteps = {
        ...advanced,
        runtime: {
            ...advancedRuntime,
            travelResolution: "HexSteps",
            actualDistanceResolution: "Fixed",
            usesNavigationChecks: false,
            encounterCadence: "None",
            tracksIntraHexProgress: false,
            directionChangesCostProgress: false
        }
    };

    const lines = procedureMechanicLines(hexSteps);
    assert.ok(lines.includes("Travel: resolved hex-step movement."));
    assert.ok(lines.includes("Navigation: procedure checks disabled."));
    assert.ok(lines.includes("Progress: discrete hex steps; no intra-hex progress tracking."));
    assert.ok(lines.includes("Automatic helpers: configured components are not applicable to the active procedure mechanics."));
    assert.doesNotMatch(lines.join("\n"), /actual distance = expected distance/);
    assert.doesNotMatch(lines.join("\n"), /Navigation helper:/);
    assert.doesNotMatch(lines.join("\n"), /Encounter helper:/);
});

test("helper mechanics expose component-specific formulas for inline controls", () => {
    const mechanics = procedureHelperMechanics(advanced);
    assert.equal(mechanics.travel, "actual distance = expected distance × 2d6+3 total × 0.1.");
    assert.equal(mechanics.navigation, "1d20 + the entered situational modifier vs. the DM-confirmed DC; a failed check uses the DM-confirmed non-zero veer.");
    assert.equal(mechanics.encounter, "1d8; wandering on 1, keyed location on 8; encounter time uses 1d8 equal watch slots.");
});

test("structural procedures remain presentable without an executable runtime projection", () => {
    const structural = {
        procedureId: "00000000-0000-0000-0000-000000000002",
        revision: 1,
        key: "structural-proof",
        name: "Structural Proof",
        isExecutable: false,
        runtime: null,
        modules: [{
            moduleKey: "journey.process",
            moduleName: "Journey Process",
            mechanicKey: "multi-stage-expedition-process",
            mechanicVersion: 1,
            executionHandler: "procedure.declarative-contract",
            automationLevel: "Manual",
            parameters: {}
        }]
    };

    assert.equal(
        campaignProcedureSummary(structural),
        "1 materialized modules · structural procedure · not executable by the current runtime");
    assert.deepEqual(procedureHelperMechanics(structural), { travel: null, navigation: null, encounter: null });
    assert.match(procedureMechanicLines(structural)[0], /multi-stage-expedition-process v1/);
});
