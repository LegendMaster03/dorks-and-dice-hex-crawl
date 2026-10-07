import assert from "node:assert/strict";
import test from "node:test";

import {
    defaultTravelPreferences,
    loadTravelPreferences,
    mergeRuntimeTravelPreferences,
    normalizeTravelModePreference,
    saveTravelPreferences
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


test("alternate finite pace survives preference reload and stale arbitrary values are rejected", () => {
    const values = new Map();
    const storage = {
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value)
    };
    const current = spatial({ intendedDirection: 1, activePaceKey: null });
    current.id = "pace-proof";

    saveTravelPreferences(current.id, { direction: 1, pace: "fast" }, storage);
    const reloaded = loadTravelPreferences(current, storage);
    assert.equal(reloaded.pace, "fast");
    assert.equal(normalizeTravelModePreference(reloaded.pace, ["normal", "fast", "slow"]), "fast");

    saveTravelPreferences(current.id, { direction: 1, pace: "warp-speed" }, storage);
    const stale = loadTravelPreferences(current, storage);
    assert.equal(stale.pace, "warp-speed");
    assert.equal(normalizeTravelModePreference(stale.pace, ["normal", "fast", "slow"]), "normal");
});

test("authoritative active pace wins over reusable browser preference", () => {
    assert.equal(
        normalizeTravelModePreference("fast", ["normal", "fast", "slow"], "slow"),
        "slow");
});
