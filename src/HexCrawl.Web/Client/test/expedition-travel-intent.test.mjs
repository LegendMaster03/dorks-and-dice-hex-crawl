import assert from "node:assert/strict";
import test from "node:test";

import {
    defaultTravelPreferences,
    mergeRuntimeTravelPreferences
} from "../.test-dist/modules/expeditions/expedition-travel-intent.js";

function spatial(overrides = {}) {
    return {
        expedition: {
            isSpatial: true,
            intendedDirection: null,
            activePaceKey: null,
            ...overrides
        }
    };
}

test("new spatial travel intent starts from authoritative runtime state", () => {
    assert.deepEqual(
        defaultTravelPreferences(spatial({ intendedDirection: 2, activePaceKey: "careful" })),
        { direction: 2, pace: "careful" });
    assert.deepEqual(
        defaultTravelPreferences(spatial()),
        { direction: null, pace: "normal" });
});

test("partial progress preserves reusable course and pace when runtime does not replace them", () => {
    const current = { direction: 4, pace: "fast" };
    const partial = spatial({ intendedDirection: null, activePaceKey: null, hexProgress: { value: 0.5 } });

    assert.deepEqual(mergeRuntimeTravelPreferences(partial, current), current);
});

test("authoritative runtime course or pace replaces the corresponding reusable preference", () => {
    const current = { direction: 4, pace: "fast" };
    assert.deepEqual(
        mergeRuntimeTravelPreferences(
            spatial({ intendedDirection: 1, activePaceKey: "slow" }),
            current),
        { direction: 1, pace: "slow" });
});

test("nonspatial runtime does not fabricate or rewrite spatial travel intent", () => {
    const current = { direction: 3, pace: "careful" };
    const runtime = { expedition: { isSpatial: false } };
    assert.equal(mergeRuntimeTravelPreferences(runtime, current), current);
});
