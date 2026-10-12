import assert from "node:assert/strict";
import test from "node:test";

import {
    adjacencyForCell,
    adjacencyForIntent,
    createCurrentCellAdjacency,
    screenRelativeAdjacencyLabel
} from "../.test-dist/modules/expeditions/spatial-adjacency.js";
import {
    currentPeriodicCellAdjacency,
    sameWorldCell
} from "../.test-dist/modules/expeditions/current-cell-topology.js";

test("current-cell presentation supports more adjacency interfaces than polygon sides", () => {
    const current = createCurrentCellAdjacency(
        "center",
        [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 0, y: 1 }],
        [
            { id: "top", order: 1, targetCell: "a", targetLabel: "a", intentValue: "A", label: "upper boundary", boundarySegment: [{ x: 0, y: 0 }, { x: 1, y: 0 }], anchor: { x: 0.5, y: 0 }, outwardVector: { x: 0, y: -1 }, traversable: true, disabledReason: null },
            { id: "right-upper", order: 2, targetCell: "b", targetLabel: "b", intentValue: "B", label: "upper-right boundary", boundarySegment: [{ x: 1, y: 0 }, { x: 1, y: 0.5 }], anchor: { x: 1, y: 0.25 }, outwardVector: { x: 1, y: 0 }, traversable: true, disabledReason: null },
            { id: "right-lower", order: 3, targetCell: "c", targetLabel: "c", intentValue: "C", label: "lower-right boundary", boundarySegment: [{ x: 1, y: 0.5 }, { x: 1, y: 1 }], anchor: { x: 1, y: 0.75 }, outwardVector: { x: 1, y: 0 }, traversable: true, disabledReason: null },
            { id: "bottom", order: 4, targetCell: "d", targetLabel: "d", intentValue: "D", label: "lower boundary", boundarySegment: [{ x: 1, y: 1 }, { x: 0, y: 1 }], anchor: { x: 0.5, y: 1 }, outwardVector: { x: 0, y: 1 }, traversable: true, disabledReason: null },
            { id: "left", order: 5, targetCell: "e", targetLabel: "e", intentValue: "E", label: "left boundary", boundarySegment: [{ x: 0, y: 1 }, { x: 0, y: 0 }], anchor: { x: 0, y: 0.5 }, outwardVector: { x: -1, y: 0 }, traversable: true, disabledReason: null }
        ],
        "C");

    assert.equal(current.boundary.length, 4);
    assert.equal(current.adjacencies.length, 5);
    assert.equal(current.selectedAdjacencyId, "right-lower");
    assert.equal(adjacencyForIntent(current, "B")?.targetCell, "b");
    assert.equal(adjacencyForCell(current, "c", (left, right) => left === right)?.intentValue, "C");
});

test("screen-relative labels are derived from supplied adjacency geometry", () => {
    assert.equal(screenRelativeAdjacencyLabel({ x: 0.9, y: 0.5 }), "right boundary");
    assert.equal(screenRelativeAdjacencyLabel({ x: 0.8, y: 0.2 }), "upper-right boundary");
});

test("generalized adapter preserves actual atomic interface identity and polygon shape", () => {
    const currentCell = {
        tilingId: "world-tiling",
        address: { motifCellId: "quad-a", translation: { u: 5, v: -3 } }
    };
    const neighbor = {
        tilingId: "world-tiling",
        address: { motifCellId: "triangle-b", translation: { u: 5, v: -3 } }
    };
    const context = {
        formatVersion: 1,
        currentCell,
        polygon: [{ x: 0, y: 0 }, { x: 2, y: 0 }, { x: 2, y: 1 }, { x: 0, y: 1 }],
        selectedExitInterfaceIndex: 7,
        interfaces: [
            { interfaceIndex: 3, boundarySideIndex: 1, neighborCell: neighbor,
                start: { x: 2, y: 0 }, end: { x: 2, y: 0.4 } },
            { interfaceIndex: 7, boundarySideIndex: 1, neighborCell: neighbor,
                start: { x: 2, y: 0.4 }, end: { x: 2, y: 1 } }
        ]
    };
    const value = currentPeriodicCellAdjacency(context);
    assert.equal(value.boundary.length, 4);
    assert.equal(value.adjacencies.length, 2);
    assert.equal(value.selectedAdjacencyId, value.adjacencies[1].id);
    assert.notEqual(value.adjacencies[0].id, value.adjacencies[1].id);
    assert.equal(adjacencyForIntent(value, 7)?.targetCell, neighbor);
    assert.ok(sameWorldCell(value.currentCell, currentCell));
    assert.ok(!sameWorldCell(value.currentCell, neighbor));
    assert.equal(value.adjacencies[0].outwardVector.x, 1);
    assert.equal(value.adjacencies[1].outwardVector.x, 1);
    assert.ok(value.adjacencies.every(edge => edge.anchor.x > .5));
});

test("generalized adapter rejects malformed interface geometry without fabricating neighbors", () => {
    const base = {
        formatVersion: 1,
        currentCell: { tilingId: "tiling", address: { motifCellId: "a", translation: { u: 0, v: 0 } } },
        polygon: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }],
        selectedExitInterfaceIndex: null
    };
    const edge = { interfaceIndex: 0, boundarySideIndex: 0, neighborCell: base.currentCell,
        start: { x: 0, y: 0 }, end: { x: 1, y: 0 } };
    assert.throws(() => currentPeriodicCellAdjacency({ ...base, interfaces: [edge, edge] }),
        /invalid atomic cell interface/);
    assert.throws(() => currentPeriodicCellAdjacency({
        ...base,
        interfaces: [{ ...edge, start: { x: 0, y: 0 }, end: { x: 0, y: 0 } }]
    }), /degenerate/);
    assert.throws(() => currentPeriodicCellAdjacency({ ...base, formatVersion: 2, interfaces: [] }),
        /Unsupported current-cell topology/);
});
