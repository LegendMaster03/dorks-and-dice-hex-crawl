import type { CellTraversalContext, WorldCellId } from "../../types.js";
import {
    createCurrentCellAdjacency,
    fitAdjacencyGeometry,
    screenRelativeAdjacencyLabel,
    type CurrentCellAdjacency,
    type SpatialAdjacencyInterface
} from "./spatial-adjacency.js";

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

