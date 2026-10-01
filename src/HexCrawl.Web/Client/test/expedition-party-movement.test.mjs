import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import {
    convertDistanceValue,
    suggestedWatchDistance
} from "../.test-dist/modules/expeditions/expedition-party-movement.js";

const mile = { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 };
const kilometer = { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 };

function runtime({ suggestion = null, targetUnit = mile, baseMovement = null } = {}) {
    return {
        movementComposition: { suggestedExpectedDistance: suggestion },
        party: { baseMovement },
        expedition: {
            isSpatial: true,
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

test("watch suggestion presents the server-derived movement composition", () => {
    assert.equal(suggestedWatchDistance(runtime({
        suggestion: { value: 6, unit: mile }
    })), 6);
});

test("watch suggestion performs only presentational unit conversion", () => {
    assert.equal(suggestedWatchDistance(runtime({
        suggestion: { value: 1, unit: kilometer },
        targetUnit: mile
    })), 0.621371192237334);
});

test("client does not reconstruct movement from party reference state", () => {
    const result = suggestedWatchDistance(runtime({
        baseMovement: {
            perHour: { value: 3, unit: mile },
            perWatch: { value: 12, unit: mile },
            perMarch: null,
            limitingMemberId: null,
            note: null
        }
    }));

    assert.equal(result, null);
});

test("server movement suggestion is optional", () => {
    assert.equal(suggestedWatchDistance(runtime()), null);
});

test("movement suggestion source does not contain watch-duration or party-reference math", () => {
    const source = fs.readFileSync(new URL("../src/modules/expeditions/expedition-party-movement.ts", import.meta.url), "utf8");
    assert.doesNotMatch(source, /baseMovement/);
    assert.doesNotMatch(source, /activeWatchRemainingHours/);
    assert.doesNotMatch(source, /intervalHours/);
});
