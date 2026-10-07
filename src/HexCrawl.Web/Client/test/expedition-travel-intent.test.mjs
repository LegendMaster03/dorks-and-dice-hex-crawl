import assert from "node:assert/strict";
import test from "node:test";

import {
    defaultTravelPreferences,
    loadTravelPreferences,
    mergeRuntimeTravelPreferences,
    normalizeTravelDirectionPreference,
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

function storageWith(entries = []) {
    const values = new Map(entries);
    return {
        values,
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value)
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

test("runtime intended course is the complete direction authority, including explicit clear", () => {
    const current = { direction: 4, pace: "fast" };
    assert.deepEqual(
        mergeRuntimeTravelPreferences(
            spatial({ intendedDirection: 1, activePaceKey: "slow" }),
            current),
        { direction: 1, pace: "slow" });
    assert.deepEqual(
        mergeRuntimeTravelPreferences(
            spatial({ intendedDirection: null, activePaceKey: null, hexProgress: { value: 0.5 } }),
            current),
        { direction: null, pace: "fast" });
});

test("nonspatial runtime does not fabricate or rewrite spatial travel intent", () => {
    const current = { direction: 3, pace: "careful" };
    const runtime = { expedition: { isSpatial: false } };
    assert.equal(mergeRuntimeTravelPreferences(runtime, current), current);
});

test("browser storage retains pace but can not override or resurrect server course", () => {
    const runtime = spatial({ intendedDirection: 2, activePaceKey: null });
    runtime.id = "course-proof";
    const storage = storageWith([
        ["hex-crawl.expedition.course-proof.travel-intent", JSON.stringify({ direction: 4, pace: "fast" })]
    ]);

    const loaded = loadTravelPreferences(runtime, storage);
    assert.deepEqual(loaded, { direction: 2, pace: "fast" });
    assert.deepEqual(
        JSON.parse(storage.values.get("hex-crawl.expedition.course-proof.travel-intent")),
        { pace: "fast" });

    const explicitlyCleared = spatial({ intendedDirection: null, activePaceKey: null });
    explicitlyCleared.id = runtime.id;
    assert.deepEqual(
        loadTravelPreferences(explicitlyCleared, storage),
        { direction: null, pace: "fast" });
});

test("saving reusable preferences persists pace only", () => {
    const storage = storageWith();
    saveTravelPreferences("pace-only", { direction: 5, pace: "slow" }, storage);
    const raw = storage.values.get("hex-crawl.expedition.pace-only.travel-intent");
    assert.deepEqual(JSON.parse(raw), { pace: "slow" });
});

test("alternate finite pace survives preference reload and stale arbitrary values are rejected", () => {
    const storage = storageWith();
    const current = spatial({ intendedDirection: 1, activePaceKey: null });
    current.id = "pace-proof";

    saveTravelPreferences(current.id, { direction: 1, pace: "fast" }, storage);
    const reloaded = loadTravelPreferences(current, storage);
    assert.equal(reloaded.direction, 1);
    assert.equal(reloaded.pace, "fast");
    assert.equal(normalizeTravelModePreference(reloaded.pace, ["normal", "fast", "slow"]), "fast");

    storage.setItem(
        "hex-crawl.expedition.pace-proof.travel-intent",
        JSON.stringify({ direction: 5, pace: "warp-speed" }));
    const stale = loadTravelPreferences(current, storage);
    assert.equal(stale.direction, 1);
    assert.equal(stale.pace, "warp-speed");
    assert.equal(normalizeTravelModePreference(stale.pace, ["normal", "fast", "slow"]), "normal");
});

test("authoritative active pace wins over reusable browser preference", () => {
    assert.equal(
        normalizeTravelModePreference("fast", ["normal", "fast", "slow"], "slow"),
        "slow");
});

test("authoritative runtime course is constrained by the current cell edge set", () => {
    assert.equal(normalizeTravelDirectionPreference(4, [0, 2, 4]), 4);
    assert.equal(normalizeTravelDirectionPreference(99, [0, 2, 4]), null);
    assert.equal(normalizeTravelDirectionPreference(null, [0, 2, 4]), null);
    assert.equal(normalizeTravelDirectionPreference(2, []), null);
});
