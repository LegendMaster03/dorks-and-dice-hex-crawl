import type { WorldPoint, WorldCellId, CellInterface } from "../../types.js";

/**
 * Map picking may suggest an intent only when exactly one authoritative
 * atomic interface reaches the selected cell. A distant cell or multiple
 * interfaces reaching the same cell is not an unambiguous travel course.
 */
export function uniquelyAdjacentInterface(
    target: WorldCellId,
    interfaces: readonly Pick<CellInterface, "interfaceIndex" | "neighborCell">[]
): number | null {
    const matches = interfaces.filter(edge =>
        edge.neighborCell.tilingId === target.tilingId
        && edge.neighborCell.address.motifCellId === target.address.motifCellId
        && edge.neighborCell.address.translation.u === target.address.translation.u
        && edge.neighborCell.address.translation.v === target.address.translation.v);
    return matches.length === 1 ? matches[0].interfaceIndex : null;
}

/**
 * Regenerate map-selection guidance from authoritative position and the
 * currently selected boundary. Hints do not imply or commit movement.
 */
export function currentCellInspectionHint(
    geometry: { id: WorldCellId; center: WorldPoint } | null,
    currentCell: WorldCellId,
    interfaces: readonly Pick<CellInterface, "interfaceIndex" | "neighborCell">[],
    selectedExit: number | null
): string {
    if (!geometry)
        return "Select a cell on the map to inspect its player knowledge and GM actions.";
    const id = geometry.id;
    if (id.tilingId === currentCell.tilingId
        && id.address.motifCellId === currentCell.address.motifCellId
        && id.address.translation.u === currentCell.address.translation.u
        && id.address.translation.v === currentCell.address.translation.v)
        return "Selected cell is the party's current position.";
    const adjacent = uniquelyAdjacentInterface(id, interfaces);
    if (adjacent !== null && selectedExit === adjacent)
        return "Adjacent cell selected as travel intent. The party has not moved.";
    if (adjacent !== null)
        return "An adjacent cell is selected for inspection. Choose its boundary to set travel intent.";
    return `Selected cell at ${geometry.center.x.toFixed(2)}, ${geometry.center.y.toFixed(2)}. Inspection does not change travel intent.`;
}

/**
 * Presentation-only containment for revealing authored point subjects in an
 * explicitly selected authoritative cell polygon. Does not determine cell
 * identity or traversal, which belong exclusively to the server.
 */
export function polygonContainsSubject(polygon: readonly WorldPoint[], point: WorldPoint): boolean {
    if (polygon.length < 3 || !Number.isFinite(point.x) || !Number.isFinite(point.y)) return false;
    let inside = false;
    for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
        const a = polygon[j], b = polygon[i];
        if (![a.x, a.y, b.x, b.y].every(Number.isFinite)) return false;
        const cross = (point.x - a.x) * (b.y - a.y)
            - (point.y - a.y) * (b.x - a.x);
        const span = Math.hypot(b.x - a.x, b.y - a.y);
        const tolerance = span * 1e-9;
        if (span > 0 && Math.abs(cross) <= tolerance
            && point.x >= Math.min(a.x, b.x) - 1e-9
            && point.x <= Math.max(a.x, b.x) + 1e-9
            && point.y >= Math.min(a.y, b.y) - 1e-9
            && point.y <= Math.max(a.y, b.y) + 1e-9) return true;
        if ((a.y > point.y) !== (b.y > point.y)
            && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
    }
    return inside;
}
