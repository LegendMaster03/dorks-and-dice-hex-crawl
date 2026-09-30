import assert from "node:assert/strict";
import test from "node:test";
import {
    canUseFocusedNonSpatialWatch,
    focusedIntervalHours,
    focusedIntervalUnavailableMessage
} from "../.test-dist/modules/expeditions/focused-interval-policy.js";

function runtime(policy, { spatial = false, procedureRuntime = null } = {}) {
    return {
        procedure: {
            isExecutable: procedureRuntime !== null,
            runtime: procedureRuntime,
            focusedIntervalPolicy: policy
        },
        expedition: { isSpatial: spatial }
    };
}

test("structural procedures can expose focused non-spatial interval bookkeeping", () => {
    const dnd = runtime({
        support: "Supported",
        intervalHours: 1,
        mechanicKey: "fixed-interval-duration",
        mechanicVersion: 1,
        executionHandler: "generic.fixed-interval-duration",
        unsupportedReason: null
    });
    const forbiddenLands = runtime({
        support: "Supported",
        intervalHours: 6,
        mechanicKey: "fixed-interval-duration",
        mechanicVersion: 1,
        executionHandler: "generic.fixed-interval-duration",
        unsupportedReason: null
    });

    assert.equal(dnd.procedure.runtime, null);
    assert.equal(dnd.procedure.isExecutable, false);
    assert.equal(canUseFocusedNonSpatialWatch(dnd), true);
    assert.equal(focusedIntervalHours(dnd), 1);
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
    });

    assert.equal(canUseFocusedNonSpatialWatch(oneRing), false);
    assert.equal(focusedIntervalHours(oneRing), null);
    assert.match(focusedIntervalUnavailableMessage(oneRing), /does not define a repeating interval/i);
});

test("unsupported future interval preserves unavailability instead of using a duration", () => {
    const future = runtime({
        support: "Unsupported",
        intervalHours: null,
        mechanicKey: "future-interval-policy",
        mechanicVersion: 99,
        executionHandler: "future.interval.handler",
        unsupportedReason: "Future interval policy is not supported."
    });

    assert.equal(canUseFocusedNonSpatialWatch(future), false);
    assert.equal(focusedIntervalHours(future), null);
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
