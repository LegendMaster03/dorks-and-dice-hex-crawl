import assert from "node:assert/strict";
import test from "node:test";
import {
    addEnvironmentFact, featureIntersectsPolygon, pointInPolygon,
    removeEnvironmentFact, tagFact
} from "../.test-dist/modules/worlds/selected-cell-authoring.js";

test("nonhex polygons use returned geometry for map-object inspection", () => {
    const triangle = [
        { x: 0, y: 0 }, { x: 4, y: 0 }, { x: 2, y: 3 }
    ];
    assert.ok(pointInPolygon({ x: 2, y: 1 }, triangle));
    assert.ok(pointInPolygon({ x: 2, y: 0 }, triangle));
    assert.ok(!pointInPolygon({ x: 4, y: 3 }, triangle));
    assert.ok(featureIntersectsPolygon({
        kind: "Line", position: null, boundary: null,
        path: [{ x: -1, y: 1 }, { x: 5, y: 1 }]
    }, triangle));
    assert.ok(!featureIntersectsPolygon({
        kind: "Point", position: { x: -5, y: -1 }, path: null, boundary: null
    }, triangle));
    const polygon = [
        { x: 0, y: 0 }, { x: 2, y: 0 }, { x: 3, y: 1 },
        { x: 2, y: 2 }, { x: 0, y: 2 }
    ];
    assert.ok(pointInPolygon({ x: 1, y: 1 }, polygon));
});

test("generalized cell facts preserve qualified identity without axial hex fallback", () => {
    const id = {
        tilingId: "world-1",
        address: { motifCellId: "shape-2", translation: { u: 6, v: -3 } }
    };
    const fact = tagFact("terrain", "forest", "manual-world", null, () => "fact-1");
    const annotations = addEnvironmentFact([], {
        kind: "Cell", hex: null, featureId: null, cell: id
    }, fact, () => "annotation-1");
    assert.equal(annotations.length, 1);
    assert.deepEqual(annotations[0].scope.cell, id);
    assert.equal(annotations[0].scope.hex, null);
    assert.equal(annotations[0].facts[0].tag, "forest");
    assert.deepEqual(removeEnvironmentFact(annotations, "annotation-1", "fact-1"), []);
});
