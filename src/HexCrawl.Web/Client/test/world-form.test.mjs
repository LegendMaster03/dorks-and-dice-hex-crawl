import assert from "node:assert/strict";
import test from "node:test";
import { canonicalDistanceUnit, customUnitFieldsVisible } from "../.test-dist/modules/worlds/world-form.js";

test("custom physical-distance units expose their metadata fields", () => {
    assert.equal(customUnitFieldsVisible("Mile"), false);
    assert.equal(customUnitFieldsVisible("Kilometer"), false);
    assert.equal(customUnitFieldsVisible("Custom"), true);
});

test("distance units use their canonical physical conversions", () => {
    assert.deepEqual(canonicalDistanceUnit("Mile"),
        { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 });
    assert.deepEqual(canonicalDistanceUnit("Kilometer"),
        { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 });
    assert.deepEqual(canonicalDistanceUnit("Custom", " league ", 4828.032),
        { kind: "Custom", symbol: "league", metersPerUnit: 4828.032 });
});

test("custom distance units require a valid physical conversion", () => {
    assert.throws(() => canonicalDistanceUnit("Custom", "u", null), /greater than zero/);
    assert.throws(() => canonicalDistanceUnit("Custom", "", 1), /symbol is required/);
    assert.throws(() => canonicalDistanceUnit("Custom", "u", -1), /greater than zero/);
});
