import assert from "node:assert/strict";
import test from "node:test";

import {
    adjacencyEdgeForCell,
    adjacencyEdgeForDirection,
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
    assert.deepEqual(square.edges.map(edge => edge.id), ["left", "top", "right", "bottom"]);
    assert.equal(square.selectedEdgeId, "right");
    assert.equal(adjacencyEdgeForDirection(square, "C")?.targetCell, "c");
    assert.equal(adjacencyEdgeForCell(square, "a", (left, right) => left === right)?.directionValue, "A");
});

test("screen-relative edge labels and arrows are geometry-derived", () => {
    assert.equal(screenRelativeEdgeLabel({ x: 0.9, y: 0.5 }), "right edge");
    assert.equal(screenRelativeEdgeLabel({ x: 0.8, y: 0.2 }), "upper-right edge");
    assert.equal(outwardArrow({ x: 0.9, y: 0.5 }), "→");
    assert.equal(outwardArrow({ x: 0.8, y: 0.2 }), "↗");
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
