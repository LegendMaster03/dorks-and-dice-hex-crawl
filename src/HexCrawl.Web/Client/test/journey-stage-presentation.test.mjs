import assert from "node:assert/strict";
import test from "node:test";
import { orderedJourneyStageDefinitions } from "../.test-dist/modules/expeditions/journey-stage-presentation.js";

test("journey stage presentation follows authoritative stageOrder even when definitions are shuffled", () => {
    const stages = [
        { stageKey: "arrival", displayName: "Arrival" },
        { stageKey: "approach", displayName: "Approach" },
        { stageKey: "pass", displayName: "Pass" }
    ];

    const ordered = orderedJourneyStageDefinitions(
        ["approach", "pass", "arrival"],
        stages);

    assert.deepEqual(
        ordered.map(stage => stage.stageKey),
        ["approach", "pass", "arrival"]);
    assert.deepEqual(
        ordered.map(stage => stage.displayName),
        ["Approach", "Pass", "Arrival"]);
});

test("journey stage presentation rejects a stageOrder entry without a definition", () => {
    assert.throws(
        () => orderedJourneyStageDefinitions(
            ["approach", "missing"],
            [{ stageKey: "approach" }]),
        /missing stage "missing"/);
});
