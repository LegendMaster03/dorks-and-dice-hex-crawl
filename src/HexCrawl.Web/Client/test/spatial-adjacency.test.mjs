import assert from "node:assert/strict";
import test from "node:test";

import { regularHexCorners } from "../.test-dist/hex-math.js";
import {
    adjacencyForCell,
    adjacencyForIntent,
    adjacencyFeedbackVector,
    createCurrentCellAdjacency,
    screenRelativeAdjacencyLabel
} from "../.test-dist/modules/expeditions/spatial-adjacency.js";
import {
    CURRENT_HEX_GJH_NOTATION,
    currentRuntimeCellAdjacency,
    sameHexCell
} from "../.test-dist/modules/expeditions/current-cell-topology.js";

test("current-cell presentation supports more adjacency interfaces than polygon sides", () => {
    const current = createCurrentCellAdjacency(
        "center",
        [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 1, y: 1 }, { x: 0, y: 1 }],
        [
            { id: "top", order: 1, targetCell: "a", targetLabel: "a", intentValue: "A", label: "upper boundary", boundarySegment: [{ x: 0, y: 0 }, { x: 1, y: 0 }], anchor: { x: 0.5, y: 0 }, traversable: true, disabledReason: null },
            { id: "right-upper", order: 2, targetCell: "b", targetLabel: "b", intentValue: "B", label: "upper-right boundary", boundarySegment: [{ x: 1, y: 0 }, { x: 1, y: 0.5 }], anchor: { x: 1, y: 0.25 }, traversable: true, disabledReason: null },
            { id: "right-lower", order: 3, targetCell: "c", targetLabel: "c", intentValue: "C", label: "lower-right boundary", boundarySegment: [{ x: 1, y: 0.5 }, { x: 1, y: 1 }], anchor: { x: 1, y: 0.75 }, traversable: true, disabledReason: null },
            { id: "bottom", order: 4, targetCell: "d", targetLabel: "d", intentValue: "D", label: "lower boundary", boundarySegment: [{ x: 1, y: 1 }, { x: 0, y: 1 }], anchor: { x: 0.5, y: 1 }, traversable: true, disabledReason: null },
            { id: "left", order: 5, targetCell: "e", targetLabel: "e", intentValue: "E", label: "left boundary", boundarySegment: [{ x: 0, y: 1 }, { x: 0, y: 0 }], anchor: { x: 0, y: 0.5 }, traversable: true, disabledReason: null }
        ],
        "C");

    assert.equal(current.boundary.length, 4);
    assert.equal(current.adjacencies.length, 5);
    assert.equal(current.selectedAdjacencyId, "right-lower");
    assert.equal(adjacencyForIntent(current, "B")?.targetCell, "b");
    assert.equal(adjacencyForCell(current, "c", (left, right) => left === right)?.intentValue, "C");
});

test("screen-relative labels and feedback vectors are derived from supplied adjacency geometry", () => {
    assert.equal(screenRelativeAdjacencyLabel({ x: 0.9, y: 0.5 }), "right boundary");
    assert.equal(screenRelativeAdjacencyLabel({ x: 0.8, y: 0.2 }), "upper-right boundary");
    assert.deepEqual(adjacencyFeedbackVector({ x: 0.5, y: 0.5 }, { x: 0.9, y: 0.5 }), { x: 1, y: 0 });
});

test("world-backed navigator geometry comes from the same authoritative cell corners as the map", () => {
    const grid = {
        id: "grid",
        orientation: "FlatTop",
        coordinateConvention: "AxialQr",
        origin: { x: 13, y: -7 },
        rotationDegrees: 0,
        hexRadiusWorldUnits: 4,
        neighborCenterDistance: { value: 6, unit: { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 } }
    };
    const current = currentRuntimeCellAdjacency({
        currentCell: { q: 4, r: -2 },
        tilingGjhNotation: CURRENT_HEX_GJH_NOTATION,
        selectedDirection: 1,
        worldGrid: grid,
        abstractOrientation: null
    });

    assert.equal(current.adjacencies.length, 6);
    assert.equal(current.selectedAdjacencyId, "adjacency-1");
    const raw = regularHexCorners("FlatTop");
    const rawWidth = Math.max(...raw.map(p => p.x)) - Math.min(...raw.map(p => p.x));
    const rawHeight = Math.max(...raw.map(p => p.y)) - Math.min(...raw.map(p => p.y));
    const shownWidth = Math.max(...current.boundary.map(p => p.x)) - Math.min(...current.boundary.map(p => p.x));
    const shownHeight = Math.max(...current.boundary.map(p => p.y)) - Math.min(...current.boundary.map(p => p.y));
    assert.ok(Math.abs(shownWidth / shownHeight - rawWidth / rawHeight) < 1e-12);
    assert.ok(Math.abs(shownWidth / shownHeight - (2 / Math.sqrt(3))) < 1e-12);
});

test("mapless geometry is resolved before the navigator consumes it", () => {
    const current = currentRuntimeCellAdjacency({
        currentCell: { q: 0, r: 0 },
        tilingGjhNotation: CURRENT_HEX_GJH_NOTATION,
        selectedDirection: null,
        worldGrid: null,
        abstractOrientation: "PointyTop"
    });
    assert.equal(current.boundary.length, 6);
    assert.equal(current.adjacencies.length, 6);
    assert.ok(current.adjacencies.every(value => value.boundarySegment.length === 2));
});

test("current runtime adapter rejects an unsupported tiling outside the generic navigator", () => {
    assert.throws(() => currentRuntimeCellAdjacency({
        currentCell: { q: 0, r: 0 },
        tilingGjhNotation: "future/tiling",
        selectedDirection: null,
        worldGrid: null,
        abstractOrientation: "PointyTop"
    }), /does not yet support tiling/);
});

test("map target and navigator intent resolve to the same adjacency interface", () => {
    const current = currentRuntimeCellAdjacency({
        currentCell: { q: 0, r: 0 },
        tilingGjhNotation: CURRENT_HEX_GJH_NOTATION,
        selectedDirection: null,
        worldGrid: null,
        abstractOrientation: "FlatTop"
    });
    const byIntent = adjacencyForIntent(current, 2);
    assert.ok(byIntent);
    const byCell = adjacencyForCell(current, byIntent.targetCell, sameHexCell);
    assert.equal(byCell?.id, byIntent.id);
});
