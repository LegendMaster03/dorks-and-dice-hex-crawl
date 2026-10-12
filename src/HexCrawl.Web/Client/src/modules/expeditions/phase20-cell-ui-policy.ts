import type { WorldPoint } from "../../types.js";

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
