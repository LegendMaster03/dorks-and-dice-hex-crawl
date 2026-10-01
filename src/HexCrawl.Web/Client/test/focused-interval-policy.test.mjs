import assert from "node:assert/strict";
import test from "node:test";
import {
    canUseFocusedNonSpatialWatch,
    focusedIntervalHours,
    focusedIntervalProcedurePresentation,
    focusedIntervalUnavailableMessage
} from "../.test-dist/modules/expeditions/focused-interval-policy.js";

function runtime(policy, { spatial = false, procedureRuntime = null, procedureName = "Procedure" } = {}) {
    return {
        procedure: {
            name: procedureName,
            isExecutable: procedureRuntime !== null,
            runtime: procedureRuntime,
            focusedIntervalPolicy: policy
        },
        expedition: { isSpatial: spatial }
    };
}

test("executable non-spatial procedures retain focused bookkeeping without structural labeling", () => {
    const executable = runtime({
        support: "Supported",
        intervalHours: 4,
        mechanicKey: "fixed-interval-duration",
        mechanicVersion: 1,
        executionHandler: "generic.fixed-interval-duration",
        unsupportedReason: null
    }, {
        procedureRuntime: { intervalHours: 4 },
        procedureName: "Simple Fixed Distance"
    });

    const presentation = focusedIntervalProcedurePresentation(executable);
    assert.equal(canUseFocusedNonSpatialWatch(executable), true);
    assert.equal(focusedIntervalHours(executable), 4);
    assert.equal(presentation.procedureLabel, "Simple Fixed Distance");
    assert.equal(presentation.executionLabel, "Executable");
    assert.doesNotMatch(presentation.procedureLabel, /structural/i);
});

test("structural procedures can expose focused non-spatial interval bookkeeping", () => {
    const dnd = runtime({
        support: "Supported",
        intervalHours: 1,
        mechanicKey: "fixed-interval-duration",
        mechanicVersion: 1,
        executionHandler: "generic.fixed-interval-duration",
        unsupportedReason: null
    }, { procedureName: "D&D 5.5e / 2024" });
    const forbiddenLands = runtime({
        support: "Supported",
        intervalHours: 6,
        mechanicKey: "fixed-interval-duration",
        mechanicVersion: 1,
        executionHandler: "generic.fixed-interval-duration",
        unsupportedReason: null
    }, { procedureName: "Forbidden Lands" });

    const presentation = focusedIntervalProcedurePresentation(dnd);
    assert.equal(dnd.procedure.runtime, null);
    assert.equal(dnd.procedure.isExecutable, false);
    assert.equal(canUseFocusedNonSpatialWatch(dnd), true);
    assert.equal(focusedIntervalHours(dnd), 1);
    assert.equal(presentation.procedureLabel, "D&D 5.5e / 2024 · structural");
    assert.equal(presentation.executionLabel, "Structural");
    assert.equal(canUseFocusedNonSpatialWatch(forbiddenLands), true);
    assert.equal(focusedIntervalHours(forbiddenLands), 6);
});

test("no repeating interval does not fabricate Watch / Time capability", () => {
    const oneRing = runtime({
        support: "None",
        intervalHours: null,
        mechanicKey: null,
        mechanicVersion: null,
        executionHandler: null,
        unsupportedReason: null
    }, { procedureName: "The One Ring" });

    const presentation = focusedIntervalProcedurePresentation(oneRing);
    assert.equal(canUseFocusedNonSpatialWatch(oneRing), false);
    assert.equal(focusedIntervalHours(oneRing), null);
    assert.equal(presentation.executionLabel, "Structural");
    assert.match(presentation.procedureLabel, /structural/i);
    assert.match(focusedIntervalUnavailableMessage(oneRing), /does not define a repeating interval/i);
});

test("unsupported future interval preserves unavailability instead of using a duration", () => {
    const future = runtime({
        support: "Unsupported",
        intervalHours: null,
        mechanicKey: "fixed-interval-duration",
        mechanicVersion: 99,
        executionHandler: "future.interval.handler",
        unsupportedReason: "Future interval policy is not supported."
    });

    const presentation = focusedIntervalProcedurePresentation(future);
    assert.equal(canUseFocusedNonSpatialWatch(future), false);
    assert.equal(focusedIntervalHours(future), null);
    assert.equal(presentation.executionLabel, "Structural");
    assert.notEqual(presentation.executionLabel, "Executable");
    assert.equal(focusedIntervalUnavailableMessage(future), "Future interval policy is not supported.");
});

test("focused interval support does not weaken spatial full-runtime requirements", () => {
    const spatialStructural = runtime({
        support: "Supported",
        intervalHours: 6,
        mechanicKey: "fixed-interval-duration",
        mechanicVersion: 1,
        executionHandler: "generic.fixed-interval-duration",
        unsupportedReason: null
    }, { spatial: true });

    assert.equal(canUseFocusedNonSpatialWatch(spatialStructural), false);
    assert.equal(spatialStructural.procedure.runtime, null);
});
