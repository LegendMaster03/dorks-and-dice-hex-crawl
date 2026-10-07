import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const source = fs.readFileSync(
    path.resolve("src/modules/expeditions/survival-resources-panel.ts"),
    "utf8");
const effects = fs.readFileSync(
    path.resolve("src/modules/expeditions/effects-panel.ts"),
    "utf8");
const effectsApi = fs.readFileSync(path.resolve("src/effect-api.ts"), "utf8");

test("survival resource forms expose the generic resource operation contract", () => {
    for (const operation of ["AdjustQuantity", "SetQuantity", "SetState", "SetSupplyDie", "Deplete"]) {
        assert.match(source, new RegExp(`\\"${operation}\\"`));
    }
    assert.match(source, /buildResourceChange\(/);
    assert.match(source, /defaultResourceOperation\(policy\.inventoryModel\)/);
});

test("survival resolution forms preserve explicit targets instead of hard-coding party scope", () => {
    assert.match(source, /targetValue\(selectedScope\(\), target\.value\)/);
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
    assert.match(source, /policy\.failureTargetScope/);
    assert.match(source, /Affected character/);
    assert.match(source, /runtime\.party\.members\.map/);
    assert.match(source, /movementContributors/);
    assert.match(source, /Configure the party before recording a failed check/);
    assert.doesNotMatch(source, /Participant, mount, or vehicle ID when required/);
    assert.match(source, /choices\.length !== 1/);
    assert.match(source, /resolve\.disabled = resolvingFailure && requiresEntity && choices\.length === 0/);
    assert.match(source, /Advanced consequence details/);
    assert.match(source, /Advanced policy details/);

    const advancedStart = source.indexOf('advancedSummary.textContent = "Advanced consequence details"');
    const effectKey = source.indexOf('const effectKey = this.input("Failure effect key"');
    const appendAdvanced = source.indexOf("advancedBody.append(effectKey.wrapper", advancedStart);
    assert.ok(effectKey >= 0);
    assert.ok(advancedStart >= 0);
    assert.ok(appendAdvanced > advancedStart);
});


test("normal effect provenance hides journey audit tokens while technical details preserve them", () => {
    assert.match(effects, /normalProvenanceLabel\(source\)/);
    assert.match(effects, /normalProvenanceNote\(source\)/);
    assert.match(effects, /sourceKey\.startsWith\("journey-"/);
    assert.match(effects, /Provenance: \$\{value\.sourceKind\} · source \$\{value\.sourceKey\}/);
    assert.match(effects, /Consequence audit/);
    assert.doesNotMatch(effects, /humanizeIdentifier\(record\.status\)\}\$\{record\.detail/);
});

test("combined resources and effects UI exposes authoritative active effects without typed IDs", () => {
    assert.match(effectsApi, /\/api\/expeditions\/.*\/effects/);
    assert.match(effects, /state\.activeEffects/);
    assert.match(effects, /Active effects/);
    assert.match(effects, /Pending consequences/);
    assert.match(effects, /Applied \/ resolved consequences/);
    assert.match(effects, /Reduce 1 level/);
    assert.match(effects, /Clear effect/);
    assert.match(effects, /Record resolution/);
    assert.doesNotMatch(effects, /Persistent effect ID/);
    assert.match(effects, /effect\.id/);
    assert.match(effects, /Technical details/);
});


test("readability follow-up renders comparable resource, environment, exposure, foraging, and camp state", () => {
    assert.match(source, /data\.resourceLedger/);
    assert.match(source, /Resource inventory/);
    assert.match(source, /Effective environment/);
    assert.match(source, /Exposure progress/);
    assert.match(source, /Activities used/);
    assert.match(source, /Preparation \/ watch activities/);
    assert.match(source, /Watch model/);
    assert.match(source, /targetSelect\("Target"/);
    assert.match(source, /assignmentMultiSelect/);
    assert.doesNotMatch(source, /Activity assignment IDs, comma-separated/);
    assert.doesNotMatch(source, /Target ID when scope requires it/);
});


test("active persistent effects use a comparable condition ledger", () => {
    assert.match(effects, /data\.effectLedger/);
    assert.match(effects, /Condition \/ effect ledger/);
    assert.match(effects, /Current value/);
    assert.match(effects, /Manage active effects/);
});
