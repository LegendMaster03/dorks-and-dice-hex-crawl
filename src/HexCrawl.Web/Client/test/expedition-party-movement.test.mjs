import test from "node:test";
import assert from "node:assert/strict";
import {
    convertDistanceValue,
    suggestedWatchDistance
} from "../.test-dist/modules/expeditions/expedition-party-movement.js";

const mile = { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
const kilometer = { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 };

function runtime({
    baseMovement = null,
    activeWatchNumber = null,
    activeWatchTotalHours = null,
    activeWatchRemainingHours = null,
    watchHours = 4,
    targetUnit = mile
} = {}) {
    return {
        profile: { watchHours },
        party: { baseMovement },
        remainingWatchHours: activeWatchRemainingHours ?? watchHours,
        expedition: {
            isSpatial: true,
            activeWatchNumber,
            activeWatchTotalHours,
            activeWatchRemainingHours,
            distanceTraveled: { value: 0, unit: targetUnit }
        }
    };
}

test("distance conversion uses explicit physical unit metadata", () => {
    assert.equal(convertDistanceValue({ value: 12, unit: mile }, mile), 12);
    assert.equal(
        convertDistanceValue({ value: 12, unit: mile }, kilometer),
        19.312128);
    assert.equal(
        convertDistanceValue(
            { value: 3, unit: { kind: "Custom", symbol: "hex", metersPerUnit: null } },
            mile),
        null);
});

test("new watch prefers explicit per-watch movement reference", () => {
    const result = suggestedWatchDistance(runtime({
        baseMovement: {
            perHour: { value: 2, unit: mile },
            perWatch: { value: 10, unit: mile },
            perMarch: null,
            limitingMemberId: null,
            note: null
        }
    }));

    assert.equal(result, 10);
});

test("new watch can derive a suggestion from per-hour movement", () => {
    const result = suggestedWatchDistance(runtime({
        watchHours: 4,
        baseMovement: {
            perHour: { value: 3, unit: mile },
            perWatch: null,
            perMarch: null,
            limitingMemberId: null,
            note: null
        }
    }));

    assert.equal(result, 12);
});

test("resumed watch only scales a per-hour reference to remaining time", () => {
    const result = suggestedWatchDistance(runtime({
        activeWatchNumber: 1,
        activeWatchTotalHours: 4,
        activeWatchRemainingHours: 2,
        baseMovement: {
            perHour: { value: 3, unit: mile },
            perWatch: { value: 12, unit: mile },
            perMarch: null,
            limitingMemberId: null,
            note: null
        }
    }));

    assert.equal(result, 6);
});

test("partial watch does not guess from a per-watch-only reference", () => {
    const result = suggestedWatchDistance(runtime({
        activeWatchNumber: 1,
        activeWatchTotalHours: 4,
        activeWatchRemainingHours: 2,
        baseMovement: {
            perHour: null,
            perWatch: { value: 12, unit: mile },
            perMarch: null,
            limitingMemberId: null,
            note: null
        }
    }));

    assert.equal(result, null);
});

test("party movement reference is optional", () => {
    assert.equal(suggestedWatchDistance(runtime()), null);
});
