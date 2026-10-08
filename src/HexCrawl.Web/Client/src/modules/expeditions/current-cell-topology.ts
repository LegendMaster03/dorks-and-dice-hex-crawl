import { hexCorners, regularHexCorners } from "../../hex-math";
import type { GridDefinition, HexCoordinate, HexOrientation } from "../../types";
import {
    createCurrentCellAdjacency,
    fitAdjacencyGeometry,
    screenRelativeAdjacencyLabel,
    type AdjacencyPoint,
    type CurrentCellAdjacency,
    type SpatialAdjacencyInterface
} from "./spatial-adjacency";

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
