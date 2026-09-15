import type { WorldPoint } from "./types";

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
