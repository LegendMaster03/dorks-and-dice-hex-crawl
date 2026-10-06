import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const sourceRoot = path.resolve(here, "../src");
const expedition = fs.readFileSync(
    path.join(sourceRoot, "modules/expeditions/expedition-view.ts"),
    "utf8");
const assistant = fs.readFileSync(
    path.join(sourceRoot, "modules/assistants/expedition-assistant-view.ts"),
    "utf8");

test("unified non-spatial expedition gates interval work on focused interval support", () => {
    assert.match(expedition, /else if \(canUseFocusedNonSpatialWatch\(runtime\)\)/);
    assert.match(expedition, /This procedure advances its configured interval without spatial position, course, pace, hex progress, or map state/);
    assert.match(expedition, /openNonSpatialWatchWorkspace/);
    assert.doesNotMatch(expedition, /renderNonSpatialTracker/);
});

test("focused utility separates non-spatial interval bookkeeping from full runtime execution", () => {
    assert.match(assistant, /mode === "travel" && !runtime\.expedition\.isSpatial/);
    assert.match(assistant, /return canUseFocusedNonSpatialWatch\(runtime\)/);
    assert.match(assistant, /function nonSpatialWatchForm[\s\S]*requireFocusedIntervalHours\(runtime\)/);
    assert.match(assistant, /function travelForm[\s\S]*requireProcedureRuntime\(runtime\)/);
    assert.match(assistant, /function navigationForm[\s\S]*spatialState\(runtime\)/);
    assert.match(assistant, /function encounterForm[\s\S]*requireProcedureRuntime\(runtime\)/);
});

test("non-spatial utility status distinguishes executable procedures from structural focused capability", () => {
    assert.match(assistant, /focusedIntervalProcedurePresentation\(runtime\)/);
    assert.match(assistant, /statusCell\("Procedure", focusedPresentation\.procedureLabel\)/);
    assert.match(assistant, /execution\s*\? statusCell\("Execution", focusedPresentation\.executionLabel\)\s*:\s*statusCell\("Focused interval", "Supported"\)/);
    assert.doesNotMatch(
        assistant,
        /statusCell\("Procedure", `\$\{runtime\.procedure\.name\} · structural`\),\s*statusCell\("Focused interval", "Supported"\)/s);
});
