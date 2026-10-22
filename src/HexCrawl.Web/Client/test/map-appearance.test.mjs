import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import {
    defaultTerrainColors, defaultFeatureColors, mapCategoryColor
} from "../.test-dist/map-appearance.js";

const read = file => fs.readFileSync(path.resolve("src", file), "utf8");

test("terrain palette has readable defaults without changing terrain data", () => {
    assert.equal(mapCategoryColor(undefined, "FOREST", "terrain"), defaultTerrainColors.forest);
    assert.equal(mapCategoryColor(undefined, "river", "feature"), defaultFeatureColors.river);
    assert.equal(mapCategoryColor(undefined, "unknown", "terrain"), null);
    assert.equal(mapCategoryColor(undefined, "town", "label"), null);
});

test("world appearance overrides category defaults without affecting other maps", () => {
    const custom = {
        terrainColors: { forest: "#112233" },
        featureColors: { river: "#AABBCC" },
        labelColors: { town: "#DDEEFF" }
    };
    assert.equal(mapCategoryColor(custom, " Forest ", "terrain"), "#112233");
    assert.equal(mapCategoryColor(custom, "RIVER", "feature"), "#AABBCC");
    assert.equal(mapCategoryColor(custom, "Town", "label"), "#DDEEFF");
    assert.equal(mapCategoryColor(undefined, "forest", "terrain"), defaultTerrainColors.forest);
});

test("malformed persisted color values never reach the canvas renderer", () => {
    const appearance = { terrainColors: { forest: "red;url()" }, featureColors: {} };
    assert.equal(mapCategoryColor(appearance, "forest", "terrain"), null);
});

test("map styling uses authoritative cell geometry and terrain annotations", () => {
    const surface = read("map-surface.ts");
    const renderer = read("canvas-renderer.ts");
    assert.match(surface, /setTerrainAnnotations/);
    assert.match(surface, /fact\.dimension\.trim\(\)\.toLowerCase\(\) !== "terrain"/);
    assert.match(renderer, /traceTilePolygon\(ctx, cell\.polygon/);
    assert.match(renderer, /terrainByCell\.get\(JSON\.stringify\(cell\.id\)\)/);
});

test("export creates an actual PNG from the map canvas with selectable background", () => {
    const surface = read("map-surface.ts");
    const view = read("modules/worlds/world-editor-view.ts");
    assert.match(surface, /context\.drawImage\(source, 0, 0\)/);
    assert.match(surface, /getBoundedExportCells\(world\.id, world\.version, cells\.length\)/);
    assert.match(surface, /new CanvasMapRenderer\(canvas, viewport/);
    assert.match(surface, /target\.toBlob\(resolve, "image\/png"\)/);
    assert.match(view, /Export visible area \(PNG\)/);
    assert.match(view, /data-export-form/);
    assert.match(view, /Full bounded map/);
    assert.match(view, /data-full-export-size/);
});

test("named points, lines and regions are rendered; geometry validates before request", () => {
    const renderer = read("canvas-renderer.ts");
    const view = read("modules/worlds/world-editor-view.ts");
    assert.match(renderer, /this\.drawFeatureLabel\(ctx, name/);
    assert.match(renderer, /midpointOnPath\(feature\.path/);
    assert.match(renderer, /polygonLabelPoint\(feature\.boundary/);
    assert.match(view, /draft\.length < 2/);
    assert.match(view, /draft\.length < 3/);
    assert.match(view, /A line requires at least two points/);
    assert.match(view, /A region requires at least three points/);
});
