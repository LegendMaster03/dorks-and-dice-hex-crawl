import type { WorldPoint } from "./types";

/**
 * A periodic viewport request has a finite candidate-enumeration budget.
 * An overflow can be recovered by reducing visible world area while keeping
 * the map center fixed. Never increase the server limit or loop at max zoom.
 */
export function nextBoundedEnumerationZoom(currentZoom: number): number | null {
    const maxZoom = 220;
    if (!Number.isFinite(currentZoom) || currentZoom <= 0 || currentZoom >= maxZoom)
        return null;
    const next = Math.min(maxZoom, currentZoom * 1.4);
    return next > currentZoom ? next : null;
}

export class Viewport {
    public center: WorldPoint = { x: 0, y: 0 };
    public zoom = 56;

    public screenToWorld(x: number, y: number, width: number, height: number): WorldPoint {
        return {
            x: this.center.x + (x - width / 2) / this.zoom,
            y: this.center.y + (y - height / 2) / this.zoom
        };
    }

    public worldToScreen(point: WorldPoint, width: number, height: number): WorldPoint {
        return {
            x: width / 2 + (point.x - this.center.x) * this.zoom,
            y: height / 2 + (point.y - this.center.y) * this.zoom
        };
    }

    public panByPixels(dx: number, dy: number): void {
        this.center = {
            x: this.center.x - dx / this.zoom,
            y: this.center.y - dy / this.zoom
        };
    }

    public zoomAt(factor: number, screenX: number, screenY: number, width: number, height: number): void {
        const before = this.screenToWorld(screenX, screenY, width, height);
        this.zoom = Math.min(220, Math.max(12, this.zoom * factor));
        const after = this.screenToWorld(screenX, screenY, width, height);
        this.center = {
            x: this.center.x + (before.x - after.x),
            y: this.center.y + (before.y - after.y)
        };
    }
}
