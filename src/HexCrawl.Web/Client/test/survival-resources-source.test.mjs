import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const source = fs.readFileSync(
    path.resolve("src/modules/expeditions/survival-resources-panel.ts"),
    "utf8");

test("survival resource forms expose the generic resource operation contract", () => {
    for (const operation of ["AdjustQuantity", "SetQuantity", "SetState", "SetSupplyDie", "Deplete"]) {
        assert.match(source, new RegExp(`\\"${operation}\\"`));
    }
    assert.match(source, /buildResourceChange\(/);
    assert.match(source, /defaultResourceOperation\(policy\.inventoryModel\)/);
});

test("survival resolution forms preserve explicit targets instead of hard-coding party scope", () => {
    assert.match(source, /targetValue\(scope\.control\.value as ExpeditionEffectScope, target\.control\.value\)/);
    assert.match(source, /targetValue\(targetScope, target\.control\.value\)/);
});

test("foraging does not silently turn a negative resolved gain positive", () => {
    assert.doesNotMatch(source, /Math\.abs\(/);
    assert.match(source, /negative input is not silently reinterpreted/);
});
