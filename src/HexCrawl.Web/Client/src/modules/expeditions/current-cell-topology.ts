import { hexCorners, regularHexCorners } from "../../hex-math.js";
import type { GridDefinition, HexCoordinate, HexOrientation } from "../../types.js";
import {
    createCurrentCellAdjacency,
    fitAdjacencyGeometry,
    screenRelativeAdjacencyLabel,
    type AdjacencyPoint,
    type CurrentCellAdjacency,
    type SpatialAdjacencyInterface
} from "./spatial-adjacency.js";

export const CURRENT_HEX_GJH_NOTATION = "6/m30/r(h1)";

const axialSteps = [
    { q: 1, r: 0 },
    { q: 1, r: -1 },
    { q: 0, r: -1 },
    { q: -1, r: 0 },
    { q: -1, r: 1 },
    { q: 0, r: 1 }
] as const;

// Current runtime direction identity maps to these boundary interfaces for both
// pointy-top and flat-top regular hexagons. This adapter is the only place the
// current six-direction runtime is translated into the topology-neutral model.
const directionBoundaryCorners = [
    [0, 1],
    [5, 0],
    [4, 5],
    [3, 4],
    [2, 3],
    [1, 2]
] as const;

export type CurrentRuntimeCellTopologyInput = {
    currentCell: HexCoordinate;
    tilingGjhNotation: string;
    selectedDirection: number | null;
    worldGrid: GridDefinition | null;
    abstractOrientation: HexOrientation | null;
};

export function currentRuntimeCellAdjacency(
    input: CurrentRuntimeCellTopologyInput): CurrentCellAdjacency<HexCoordinate, number> {
    if (input.tilingGjhNotation !== CURRENT_HEX_GJH_NOTATION) {
        throw new Error(
            `The current runtime adapter does not yet support tiling '${input.tilingGjhNotation}'.`);
    }

    const boundary = input.worldGrid
        ? hexCorners(input.worldGrid, input.currentCell)
        : regularHexCorners(input.abstractOrientation ?? "PointyTop");

    const cellCenter = input.worldGrid
        ? hexCenter(input.worldGrid, input.currentCell)
        : { x: 0, y: 0 };

    const raw: SpatialAdjacencyInterface<HexCoordinate, number>[] =
        axialSteps.map((step, direction) => {
            const [startIndex, endIndex] = directionBoundaryCorners[direction];
            const targetCell = {
                q: input.currentCell.q + step.q,
                r: input.currentCell.r + step.r
            };
            const boundarySegment: AdjacencyPoint[] = [
                boundary[startIndex],
                boundary[endIndex]
            ];
            return {
                id: `adjacency-${direction}`,
                order: direction,
                targetCell,
                targetLabel: `cell ${targetCell.q}, ${targetCell.r}`,
                intentValue: direction,
                label: "",
                boundarySegment,
                anchor: midpoint(boundarySegment[0], boundarySegment[1]),
                outwardVector: outwardNormal(
                    boundarySegment[0],
                    boundarySegment[1],
                    cellCenter),
                traversable: true,
                disabledReason: null
            };
        });

    const fitted = fitAdjacencyGeometry(boundary, raw);
    const labeled = fitted.adjacencies.map(adjacency => ({
        ...adjacency,
        label: screenRelativeAdjacencyLabel(adjacency.anchor)
    }));
    return createCurrentCellAdjacency(
        input.currentCell,
        fitted.boundary,
        labeled,
        input.selectedDirection);
}

export function sameHexCell(left: HexCoordinate, right: HexCoordinate): boolean {
    return left.q === right.q && left.r === right.r;
}

function midpoint(left: AdjacencyPoint, right: AdjacencyPoint): AdjacencyPoint {
    return {
        x: (left.x + right.x) / 2,
        y: (left.y + right.y) / 2
    };
}

function hexCenter(grid: GridDefinition, cell: HexCoordinate): AdjacencyPoint {
    const corners = hexCorners(grid, cell);
    return {
        x: corners.reduce((sum, point) => sum + point.x, 0) / corners.length,
        y: corners.reduce((sum, point) => sum + point.y, 0) / corners.length
    };
}

function outwardNormal(
    start: AdjacencyPoint,
    end: AdjacencyPoint,
    cellCenter: AdjacencyPoint): AdjacencyPoint {
    const dx = end.x - start.x;
    const dy = end.y - start.y;
    const length = Math.hypot(dx, dy);
    if (length <= Number.EPSILON) {
        throw new Error("A current-cell adjacency interface requires non-zero boundary geometry.");
    }

    const anchor = midpoint(start, end);
    const first = { x: -dy / length, y: dx / length };
    const towardAnchor = { x: anchor.x - cellCenter.x, y: anchor.y - cellCenter.y };
    return first.x * towardAnchor.x + first.y * towardAnchor.y >= 0
        ? first
        : { x: -first.x, y: -first.y };
}
