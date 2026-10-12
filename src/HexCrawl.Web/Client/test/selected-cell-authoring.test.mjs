import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import {
    addEnvironmentFact,
    featureHasEnvironmentRules,
    featureIntersectsPolygon,
    measurementFact,
    pointInPolygon,
    removeEnvironmentFact,
    tagFact
} from "../.test-dist/modules/worlds/selected-cell-authoring.js";

const sourceRoot = path.resolve("src");
const square = [
    { x: 0, y: 0 },
    { x: 3, y: 0 },
    { x: 3, y: 3 },
    { x: 0, y: 3 }
];
const environment = annotations => ({ overworldId: "world", version: 1, annotations });

test("world cell membership follows authoritative polygon geometry", () => {
    assert.equal(pointInPolygon({ x: 1, y: 1 }, square), true);
    assert.equal(pointInPolygon({ x: 0, y: 1 }, square), true);
    assert.equal(pointInPolygon({ x: 4, y: 1 }, square), false);
    const rotated = [
        { x: 0, y: 2 }, { x: 2, y: 0 },
        { x: 4, y: 2 }, { x: 2, y: 4 }
    ];
    assert.equal(pointInPolygon({ x: 2, y: 2 }, rotated), true);
    assert.equal(pointInPolygon({ x: 0, y: 0 }, rotated), false);
});

test("continuous lines and regions intersect selected cell polygons regardless of family", () => {
    const line = {
        kind: "Line", position: null, boundary: null,
        path: [{ x: -1, y: 1 }, { x: 4, y: 1 }]
    };
    assert.equal(featureIntersectsPolygon(line, square), true);
    assert.equal(featureIntersectsPolygon(
        { ...line, path: [{ x: -1, y: -1 }, { x: 4, y: -1 }] }, square), false);
    const region = {
        kind: "Region", position: null, path: null,
        category: "forest",
        boundary: [{ x: 2, y: 2 }, { x: 5, y: 2 },
            { x: 5, y: 5 }, { x: 2, y: 5 }]
    };
    assert.equal(featureIntersectsPolygon(region, square), true);
    assert.equal(featureIntersectsPolygon(
        { kind: "Point", position: { x: 4, y: 4 } }, square), false);
});

test("qualified-cell facts retain independent scopes and provenance", () => {
    const cell = {
        tilingId: "tiling-1",
        address: { motifCellId: "motif-a", translation: { u: 0, v: 0 } }
    };
    const terrain = tagFact("terrain", "swamp", "dm:review", "A marsh", () => "terrain-fact");
    const worldFact = measurementFact("temperature", -10, "C", "source:weather", null, () => "world-fact");
    let annotations = addEnvironmentFact([], {
        kind: "Cell", cell, hex: null, featureId: null
    }, terrain, () => "cell-annotation");
    annotations = addEnvironmentFact(annotations, {
        kind: "World", hex: null, featureId: null
    }, worldFact, () => "world-annotation");
    assert.equal(annotations[0].scope.cell.address.motifCellId, "motif-a");
    assert.equal(annotations[0].facts[0].note, "A marsh");
    assert.equal(annotations[1].facts[0].measurement.value, -10);
    const removed = removeEnvironmentFact(annotations, "cell-annotation", "terrain-fact");
    assert.equal(removed.length, 1);
    assert.equal(removed[0].id, "world-annotation");
});

test("feature-scoped rules remain attached to their feature and block deletion", () => {
    const scope = { kind: "SpatialFeature", hex: null, featureId: "road" };
    const fact = tagFact("route", "good-road", "dm", null, () => "route-fact");
    const other = tagFact("visibility", "clear", "dm", null, () => "visibility-fact");
    const annotations = addEnvironmentFact(
        addEnvironmentFact([], scope, fact, () => "route-annotation"),
        scope, other, () => "visibility-annotation");
    assert.equal(featureHasEnvironmentRules(environment(annotations), "road"), true);
    const withoutRoute = removeEnvironmentFact(annotations, "route-annotation", "route-fact");
    assert.equal(featureHasEnvironmentRules(environment(withoutRoute), "road"), true);
    assert.equal(featureHasEnvironmentRules(environment(withoutRoute), "other"), false);
});

test("polygon membership scales across a representative authored world", () => {
    const points = Array.from({ length: 1200 }, (_, i) =>
        ({ x: (i % 41) - 20, y: Math.floor(i / 41) - 20 }));
    const lines = Array.from({ length: 180 }, (_, i) => ({
        kind: "Line", position: null, boundary: null,
        path: [{ x: -20, y: i % 19 - 9 }, { x: 20, y: i % 19 - 9 }]
    }));
    const start = performance.now();
    const selected = points.filter(point => pointInPolygon(point, square));
    const intersecting = lines.filter(feature => featureIntersectsPolygon(feature, square));
    assert.ok(selected.length > 0);
    assert.ok(intersecting.length > 0);
    assert.ok(performance.now() - start < 1500);
});

test("world editor composes existing authorities and does not mutate player knowledge when selecting content", () => {
    const source = fs.readFileSync(path.join(sourceRoot, "modules/worlds/world-editor-view.ts"), "utf8");
    assert.match(source, /setCellSelectionHandler/);
    assert.doesNotMatch(source, /setHexSelectionHandler/);
    assert.match(source, /pointInPolygon\(location.position, cell.polygon\)/);
    assert.match(source, /featureIntersectsPolygon\(feature, cell.polygon\)/);
    assert.match(source, /const replaceTerrain = async/);
    assert.match(source, /api\.createLocation/);
    assert.match(source, /api\.createFeature/);
    assert.match(source, /select\(featureForm, "kind"\)\.value/);
    assert.match(source, /kind: "SpatialFeature"/);
    assert.match(source, /featureHasEnvironmentRules/);
    assert.match(source, /confirmDeletion/);
    assert.match(source, /window\.confirm/);
    assert.match(source, /This can not be undone/);
    assert.doesNotMatch(source, /revealSubject|discoverSubject|PlayerKnowledge|playerKnowledge/);
});
