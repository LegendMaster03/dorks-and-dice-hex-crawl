import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import {
    movementReferenceFieldMarkup, preferredPartyMovementUnit
} from "../.test-dist/modules/expeditions/party-movement-rendering.js";
import { resolvePeriodicPace } from "../.test-dist/modules/expeditions/periodic-travel-pace.js";

const src = (...parts) => fs.readFileSync(path.join("src", ...parts), "utf8");
const mockRuntime = (physicalDistance = null, distanceUnit = null) => ({
    expedition: {
        isSpatial: true,
        currentCell: { tilingId: "world", address: { motifCellId:"A", translation: {u:0,v:0} } },
        distanceTraveled: physicalDistance
    },
    party: { baseMovement: null },
    context: { hexCenterDistance: null },
    movementComposition: {
        suggestedExpectedDistance: null,
        effectiveDistanceUnit: distanceUnit
    }
});

test("F1: rendered party movement fields work for an uncalibrated cell-step world with empty references", () => {
    const runtime = mockRuntime();
    const preferred = preferredPartyMovementUnit(runtime, null);
    assert.equal(preferred, null);
    const rendered = ["perHour", "perWatch", "perMarch"]
        .map((key, index) => movementReferenceFieldMarkup(
            key, ["Per hour", "Per watch", "Per march"][index], null, preferred)).join("");
    assert.equal((rendered.match(/data-movement-key=/g) ?? []).length, 3);
    assert.equal((rendered.match(/data-movement-value/g) ?? []).length, 3);
    assert.equal((rendered.match(/data-movement-value type="number" min="0" step="any" value=""/g) ?? []).length, 3);
    assert.equal((rendered.match(/<option value="" selected>Choose a unit<\/option>/g) ?? []).length, 3);
    assert.doesNotMatch(rendered, /undefined|null|NaN/);
    // Rendering an EMPTY optional rate must not create a world calibration
    // or a party movement reference, even if the input selector defaults to miles.
    assert.equal(runtime.party.baseMovement, null);
    assert.equal(runtime.expedition.distanceTraveled, null);
    assert.equal(runtime.context.hexCenterDistance, null);
    const controller = src("modules", "expeditions", "expedition-party-sheet.ts");
    assert.match(controller, /return movementReferenceFieldMarkup\(key, label, distance, this\.preferredUnit\(\)\)/);
    assert.match(controller, /return preferredPartyMovementUnit\(/);
});

test("F1: stored party units take precedence without requiring physical calibration", () => {
    const kilometers = {kind:"Kilometer", symbol:"km", metersPerUnit:1000};
    const references = { perHour:null, perWatch:{value:7,unit:kilometers}, perMarch:null };
    const preferred = preferredPartyMovementUnit(mockRuntime(), references);
    assert.equal(preferred.kind, "Kilometer");
    const rendered = movementReferenceFieldMarkup("perWatch", "Per watch", references.perWatch, preferred);
    assert.match(rendered, /value="7"/);
    assert.match(rendered, /<option value="Kilometer" selected>/);
    const calibrated = mockRuntime({value:10,unit:kilometers});
    assert.equal(preferredPartyMovementUnit(calibrated,null).kind, "Kilometer");
});

test("F2: zero pace choices are fixed server-default normal, not an invalid form", () => {
    assert.deepEqual(resolvePeriodicPace([], "", null), {paceKey:"normal", needsChoice:false});
    assert.deepEqual(resolvePeriodicPace([], "", "normal"), {paceKey:"normal", needsChoice:false});
    assert.deepEqual(resolvePeriodicPace(["steady"], "", null), {paceKey:"steady", needsChoice:false});
    assert.deepEqual(resolvePeriodicPace(["slow","fast"],"fast",null), {paceKey:"fast", needsChoice:true});
    assert.deepEqual(resolvePeriodicPace(["slow","fast"],"unknown",null), {paceKey:"slow", needsChoice:true});
    assert.deepEqual(resolvePeriodicPace(["slow","fast"],"fast","slow"), {paceKey:"slow", needsChoice:true});
    const view = src("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(view, /resolvePeriodicPace\(modes, pace\.value, state\.activePaceKey\)/);
    assert.match(view, /modes\.length \? modes : \[effective\.paceKey\]/);
    assert.match(view, /pace\.value = effective\.paceKey/);
    assert.match(view, /resolvePeriodicPace\([\s\S]*?choice\("pace"\)\.value, state\.activePaceKey\)\.paceKey/);
    assert.doesNotMatch(view, /modes\.length === 0;\s*\n\s*required<HTMLButtonElement>\("\[data-advance\]"/);
});

test("F3: navigator uses the map overlay at desktop and mobile sizes", () => {
    const css = src("phase15-styles.ts");
    const view = src("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(view, /hc-periodic-expedition/);
    assert.match(view, /<div class="hc-map-frame"><div class="hc-map-host" data-map><\/div><div data-cell-navigator aria-label="Map travel compass"><\/div><\/div>/);
    assert.match(css, /\.hc-periodic-expedition \.hc-map-frame > \[data-cell-navigator\] \{\s*position:absolute; z-index:6; top:\.8rem; left:\.8rem;/);
    assert.match(css, /\.hc-periodic-expedition \.hc-map-frame > \[data-cell-navigator\] \.hc-adjacency-navigator \{\s*position:relative; inset:auto; width:100%; height:100%; margin:0;/);
    assert.match(css, /\.hc-periodic-expedition \.hc-map-frame > \[data-cell-navigator\] \{ top:\.55rem; left:\.55rem; width:7\.5rem; \}/);
});
