import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { resolveHexStartCell } from "../.test-dist/modules/expeditions/world-cell-start.js";

const file = (...segments) => fs.readFileSync(path.join("src", ...segments), "utf8");
const world = {
    id: "world-1",
    tiling: { id: "tiling-1" },
    grid: {
        origin: { x: 2, y: -3 },
        hexRadiusWorldUnits: 5,
        orientation: "PointyTop",
        rotationDegrees: 30
    }
};
const id = (tilingId = "tiling-1") => ({
    tilingId,
    address: { motifCellId: "accepted-hex-motif", translation: { u: 7, v: -4 } }
});

test("hex q/r start resolves one authoritative server cell without guessing motif identity", async () => {
    let captured;
    const address = await resolveHexStartCell({
        lookupWorldCell: async (worldId, point) => {
            captured = { worldId, point };
            return { status: "Unique", candidates: [id()] };
        }
    }, world, { q: 7, r: -4 });
    assert.equal(captured.worldId, world.id);
    assert.ok(Number.isFinite(captured.point.x) && Number.isFinite(captured.point.y));
    assert.deepEqual(address, id().address);
});

test("cell start rejects absent, ambiguous and foreign tiling selections", async () => {
    for (const lookup of [
        { status: "NotFound", candidates: [] },
        { status: "Ambiguous", candidates: [id(), id()] },
        { status: "Unique", candidates: [id("other-tiling")] }
    ]) {
        await assert.rejects(
            resolveHexStartCell({ lookupWorldCell: async () => lookup }, world, { q: 0, r: 0 }),
            /authoritative|current world/);
    }
    await assert.rejects(
        resolveHexStartCell({ lookupWorldCell: async () => {
            assert.fail("invalid coordinates should never trigger a lookup");
        } }, world, { q: Number.MAX_SAFE_INTEGER + 1, r: 0 }),
        /safe integers/);
});

test("active expedition start entry points use cell APIs and no retired V1 starts", () => {
    const home = file("modules","home","tool-home-view.ts");
    const editor = file("modules","worlds","world-editor-view.ts");
    const setup = file("modules","expeditions","expedition-setup.ts");
    const api = file("api.ts");
    assert.match(home, /api\.startStandaloneCellSession\(/);
    assert.match(home, /api\.startCellExpedition\(/);
    assert.match(home, /api\.startStandaloneSession\(/);
    assert.match(setup, /api\.startCellExpedition\(/);
    assert.match(editor, /enhanceExpeditionSetup\(/);
    assert.doesNotMatch(home + editor + setup, /api\.(startConfiguredExpedition|startExpedition)\(/);
    assert.match(api, /"POST", "\/api\/expeditions\/cells"/);
    assert.match(api, /"POST", "\/api\/expeditions"/);
});

test("renderer shows actual cell polygon and party in mapless and legacy hex worlds", () => {
    const renderer = file("canvas-renderer.ts");
    const map = file("map-surface.ts");
    const periodic = file("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(renderer, /if \(!ctx\) return/);
    assert.doesNotMatch(renderer, /if \(!ctx \|\| !world\) return/);
    assert.match(renderer, /this\.drawExpedition\(ctx, world, width, height\)/);
    assert.match(renderer, /!world \|\| world\.tiling\?\.id === this\.currentWorldCell\.id\.tilingId/);
    assert.match(map, /public centerOn\(point: WorldPoint\)/);
    assert.match(periodic, /if \(!world\) map\.centerOn\(topology\.position\)/);
});

test("mapless cell workbench does not require a fabricated overworld", () => {
    const view = file("modules","expeditions","expedition-view.ts");
    const periodic = file("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(view, /runtime\.expedition\.currentCell/);
    assert.doesNotMatch(periodic, /if \(!initialWorld \|\|/);
    assert.match(periodic, /world \? api\.getOverworld\(world\.id\) : Promise\.resolve\(null\)/);
    assert.match(periodic, /"Mapless cell travel"/);
    assert.match(periodic, /world\?\.locations \?\? \[\]/);
});
