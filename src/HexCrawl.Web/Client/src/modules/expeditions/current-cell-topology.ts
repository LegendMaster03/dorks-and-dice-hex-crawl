import { hexCorners, regularHexCorners } from "../../hex-math.js";
import type { CellTraversalContext, GridDefinition, HexCoordinate, HexOrientation, WorldCellId } from "../../types.js";
import {
    createCurrentCellAdjacency,
    fitAdjacencyGeometry,
    screenRelativeAdjacencyLabel,
    type AdjacencyPoint,
    type CurrentCellAdjacency,
    type SpatialAdjacencyInterface
} from "./spatial-adjacency.js";

export const CURRENT_HEX_DS_SYMBOL = "<1:1,1,1:6,3>";

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
    tilingDsSymbol: string;
    selectedDirection: number | null;
    worldGrid: GridDefinition | null;
    abstractOrientation: HexOrientation | null;
};

export function currentRuntimeCellAdjacency(
    input: CurrentRuntimeCellTopologyInput): CurrentCellAdjacency<HexCoordinate, number> {
    if (input.tilingDsSymbol !== CURRENT_HEX_DS_SYMBOL) {
        throw new Error(
            `The current runtime adapter does not yet support tiling '${input.tilingDsSymbol}'.`);
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

/**
 * Adapts the Phase 18 authoritative polygon and atomic edges to the existing
 * geometry-neutral navigator. Indices identify interfaces, never polygon sides.
 * This adapter does not reconstruct a tiling or a traversal path.
 */
export function currentPeriodicCellAdjacency(
    context: CellTraversalContext): CurrentCellAdjacency<WorldCellId, number> {
    if (context.formatVersion !== 1) {
        throw new Error("Unsupported current-cell topology response version.");
    }
    if (!Array.isArray(context.polygon) || context.polygon.length < 3
        || context.polygon.some(point => !Number.isFinite(point.x) || !Number.isFinite(point.y))) {
        throw new Error("The world returned invalid current-cell polygon geometry.");
    }
    const signedArea = context.polygon.reduce((area, vertex, index) => {
        const next = context.polygon[(index + 1) % context.polygon.length];
        // Localize large world coordinates before computing cross products.
        const origin = context.polygon[0];
        return area + (vertex.x - origin.x) * (next.y - origin.y)
            - (next.x - origin.x) * (vertex.y - origin.y);
    }, 0);
    if (!Number.isFinite(signedArea) || signedArea === 0) {
        throw new Error("The current-cell polygon has no usable area.");
    }
    const seen = new Set<number>();
    const raw: SpatialAdjacencyInterface<WorldCellId, number>[] = context.interfaces.map(edge => {
        if (!Number.isSafeInteger(edge.interfaceIndex) || edge.interfaceIndex < 0
            || seen.has(edge.interfaceIndex)
            || ![edge.start, edge.end].every(point =>
                Number.isFinite(point.x) && Number.isFinite(point.y))) {
            throw new Error("The world returned an invalid atomic cell interface.");
        }
        seen.add(edge.interfaceIndex);
        const dx = edge.end.x - edge.start.x;
        const dy = edge.end.y - edge.start.y;
        const length = Math.hypot(dx, dy);
        if (!(length > 0) || !Number.isFinite(length)) {
            throw new Error("The world returned a degenerate atomic cell interface.");
        }
        const outwardVector = signedArea > 0
            ? { x: dy / length, y: -dx / length }
            : { x: -dy / length, y: dx / length };
        return {
            id: `${context.currentCell.tilingId}:${context.currentCell.address.motifCellId}:${context.currentCell.address.translation.u}:${context.currentCell.address.translation.v}:${edge.interfaceIndex}`,
            order: edge.interfaceIndex,
            targetCell: edge.neighborCell,
            targetLabel: `adjacent cell via interface ${edge.interfaceIndex + 1}`,
            intentValue: edge.interfaceIndex,
            label: "",
            boundarySegment: [edge.start, edge.end],
            anchor: { x: edge.start.x + dx / 2, y: edge.start.y + dy / 2 },
            outwardVector,
            traversable: true,
            disabledReason: null
        };
    });
    const fitted = fitAdjacencyGeometry(context.polygon, raw);
    return createCurrentCellAdjacency(
        context.currentCell,
        fitted.boundary,
        fitted.adjacencies.map(edge => ({
            ...edge,
            label: screenRelativeAdjacencyLabel(edge.anchor)
        })),
        context.selectedExitInterfaceIndex);
}

export function sameWorldCell(left: WorldCellId, right: WorldCellId): boolean {
    return left.tilingId === right.tilingId
        && left.address.motifCellId === right.address.motifCellId
        && left.address.translation.u === right.address.translation.u
        && left.address.translation.v === right.address.translation.v;
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
