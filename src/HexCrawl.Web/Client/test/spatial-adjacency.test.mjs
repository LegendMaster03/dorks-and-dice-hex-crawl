import assert from "node:assert/strict";
import test from "node:test";

import {
    adjacencyEdgeForCell,
    adjacencyEdgeForDirection,
    adjacencyFeedbackVector,
    createCurrentCellAdjacency,
    currentHexAdjacency,
    outwardArrow,
    sameHex,
    screenRelativeEdgeLabel
} from "../.test-dist/modules/expeditions/spatial-adjacency.js";

test("current-cell adjacency contract does not assume six edges", () => {
    const square = createCurrentCellAdjacency(
        "center",
        [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 0, y: 1 }],
        [
            { id: "top", order: 2, targetCell: "a", directionValue: "A", label: "upper edge", shortLabel: "↑", midpoint: { x: 0.5, y: 0 }, traversable: true, disabledReason: null },
            { id: "right", order: 3, targetCell: "b", directionValue: "B", label: "right edge", shortLabel: "→", midpoint: { x: 1, y: 0.5 }, traversable: true, disabledReason: null },
            { id: "bottom", order: 4, targetCell: "c", directionValue: "C", label: "lower edge", shortLabel: "↓", midpoint: { x: 0.5, y: 1 }, traversable: true, disabledReason: null },
            { id: "left", order: 1, targetCell: "d", directionValue: "D", label: "left edge", shortLabel: "←", midpoint: { x: 0, y: 0.5 }, traversable: true, disabledReason: null }
        ],
        "B");

    assert.equal(square.edges.length, 4);
    assert.deepEqual(square.center, { x: 0.5, y: 0.5 });
    assert.deepEqual(square.edges.map(edge => edge.id), ["left", "top", "right", "bottom"]);
    assert.equal(square.selectedEdgeId, "right");
    assert.equal(adjacencyEdgeForDirection(square, "C")?.targetCell, "c");
    assert.equal(adjacencyEdgeForCell(square, "a", (left, right) => left === right)?.directionValue, "A");
});

test("screen-relative edge labels, arrows, and feedback vectors are geometry-derived", () => {
    assert.equal(screenRelativeEdgeLabel({ x: 0.9, y: 0.5 }), "right edge");
    assert.equal(screenRelativeEdgeLabel({ x: 0.8, y: 0.2 }), "upper-right edge");
    assert.equal(outwardArrow({ x: 0.9, y: 0.5 }), "→");
    assert.equal(outwardArrow({ x: 0.8, y: 0.2 }), "↗");
    assert.deepEqual(adjacencyFeedbackVector({ x: 0.5, y: 0.5 }, { x: 0.9, y: 0.5 }), { x: 1, y: 0 });
    const diagonal = adjacencyFeedbackVector({ x: 0.5, y: 0.5 }, { x: 0.8, y: 0.2 });
    assert.ok(diagonal.x > 0);
    assert.ok(diagonal.y < 0);
    assert.ok(Math.abs(Math.hypot(diagonal.x, diagonal.y) - 1) < 1e-12);
});

test("hex adapter exposes adjacent-cell identity without compass assumptions", () => {
    const adjacency = currentHexAdjacency({ q: 4, r: -2 }, "PointyTop", 1);

    assert.equal(adjacency.edges.length, 6);
    assert.equal(adjacency.selectedEdgeId, "edge-1");
    assert.deepEqual(
        adjacency.edges.map(edge => [edge.label, edge.shortLabel, edge.targetCell]),
        [
            ["right edge", "→", { q: 5, r: -2 }],
            ["upper-right edge", "↗", { q: 5, r: -3 }],
            ["upper-left edge", "↖", { q: 4, r: -3 }],
            ["left edge", "←", { q: 3, r: -2 }],
            ["lower-left edge", "↙", { q: 3, r: -1 }],
            ["lower-right edge", "↘", { q: 4, r: -1 }]
        ]);
    assert.ok(adjacency.edges.every(edge => edge.traversable));
    assert.equal(adjacency.edges.some(edge => /north|south|east|west/i.test(edge.label)), false);
});


test("all six current hex edges retain independent target and feedback identity", () => {
    const adjacency = currentHexAdjacency({ q: 10, r: -4 }, "PointyTop", null);
    const expectedTargets = [
        { q: 11, r: -4 },
        { q: 11, r: -5 },
        { q: 10, r: -5 },
        { q: 9, r: -4 },
        { q: 9, r: -3 },
        { q: 10, r: -3 }
    ];

    assert.deepEqual(adjacency.edges.map(edge => edge.id), [
        "edge-0", "edge-1", "edge-2", "edge-3", "edge-4", "edge-5"
    ]);
    assert.deepEqual(adjacency.edges.map(edge => edge.targetCell), expectedTargets);

    const vectors = adjacency.edges.map(edge =>
        adjacencyFeedbackVector(adjacency.center, edge.midpoint));
    assert.equal(new Set(vectors.map(vector =>
        `${vector.x.toFixed(6)},${vector.y.toFixed(6)}`)).size, 6);

    for (const [index, edge] of adjacency.edges.entries()) {
        const selected = currentHexAdjacency(adjacency.currentCell, "PointyTop", index);
        assert.equal(selected.selectedEdgeId, edge.id);
        assert.deepEqual(adjacencyEdgeForDirection(selected, index)?.targetCell, expectedTargets[index]);
        const vector = vectors[index];
        const radial = {
            x: edge.midpoint.x - adjacency.center.x,
            y: edge.midpoint.y - adjacency.center.y
        };
        assert.ok(vector.x * radial.x + vector.y * radial.y > 0);
    }
});

test("grid rotation changes screen-relative presentation without changing runtime edge identity", () => {
    const unrotated = currentHexAdjacency({ q: 0, r: 0 }, "PointyTop", 0, 0);
    const rotated = currentHexAdjacency({ q: 0, r: 0 }, "PointyTop", 0, 90);

    assert.deepEqual(unrotated.edges[0].targetCell, rotated.edges[0].targetCell);
    assert.equal(unrotated.edges[0].label, "right edge");
    assert.equal(rotated.edges[0].label, "lower edge");
    assert.equal(rotated.edges[0].shortLabel, "↓");
});

test("map target and navigator direction resolve to the same semantic edge", () => {
    const adjacency = currentHexAdjacency({ q: 0, r: 0 }, "FlatTop", null);
    const byDirection = adjacencyEdgeForDirection(adjacency, 2);
    assert.ok(byDirection);

    const byCell = adjacencyEdgeForCell(adjacency, byDirection.targetCell, sameHex);
    assert.equal(byCell?.id, byDirection.id);
    assert.equal(byCell?.label, "upper edge");
});
