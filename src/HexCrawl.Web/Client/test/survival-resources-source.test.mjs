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

test("focused forced-travel presentation keeps tabletop fields primary and technical contracts advanced", () => {
    assert.match(source, /Current requirement/);
    assert.match(source, /Check: \$\{humanize\(policy\.checkModel/);
    assert.match(source, /Failure consequence: \$\{humanize\(policy\.failureConsequence/);
    assert.match(source, /Resolved travel usage \(\$\{policy\.limitUnit\}\)/);
    assert.match(source, /Affected target/);
    assert.match(source, /Advanced consequence details/);
    assert.match(source, /Advanced policy details/);

    const advancedStart = source.indexOf('advancedSummary.textContent = "Advanced consequence details"');
    const effectKey = source.indexOf('const effectKey = this.input("Failure effect key"');
    const appendAdvanced = source.indexOf("advancedBody.append(effectKey.wrapper", advancedStart);
    assert.ok(effectKey >= 0);
    assert.ok(advancedStart >= 0);
    assert.ok(appendAdvanced > advancedStart);
});
