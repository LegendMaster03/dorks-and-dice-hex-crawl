import { CanvasMapRenderer } from "./canvas-renderer";
import { RenderLifecycle } from "./render-lifecycle";
import type { Overworld, WorldPoint } from "./types";
import { Viewport } from "./viewport";

export class MapSurface {
    public readonly canvas: HTMLCanvasElement;
    public readonly renderer: CanvasMapRenderer;
    private readonly viewport = new Viewport();
    private readonly lifecycle = new RenderLifecycle();
    private readonly resizeObserver: ResizeObserver;
    private disposed = false;

    public constructor(
        host: HTMLElement,
        getWorld: () => Overworld | null,
        onWorldClick?: (point: WorldPoint) => void) {
        this.canvas = document.createElement("canvas");
        this.canvas.className = "hc-map-canvas";
        this.canvas.tabIndex = 0;
        host.replaceChildren(this.canvas);
        this.renderer = new CanvasMapRenderer(this.canvas, this.viewport, getWorld);
        this.lifecycle.register("map", () => this.renderer.render());

        this.canvas.addEventListener("click", event => {
            if (!onWorldClick || event.button !== 0) return;
            const rect = this.canvas.getBoundingClientRect();
            onWorldClick(this.viewport.screenToWorld(
                event.clientX - rect.left,
                event.clientY - rect.top,
                rect.width,
                rect.height));
        });
        this.canvas.addEventListener("wheel", event => {
            event.preventDefault();
            const rect = this.canvas.getBoundingClientRect();
            this.viewport.zoomAt(
                event.deltaY < 0 ? 1.12 : 1 / 1.12,
                event.clientX - rect.left,
                event.clientY - rect.top,
                rect.width,
                rect.height);
            this.requestRender();
        }, { passive: false });

        let dragging = false;
        let lastX = 0;
        let lastY = 0;
        this.canvas.addEventListener("pointerdown", event => {
            if (event.button !== 1 && !event.shiftKey) return;
            dragging = true;
            lastX = event.clientX;
            lastY = event.clientY;
            this.canvas.setPointerCapture(event.pointerId);
        });
        this.canvas.addEventListener("pointermove", event => {
            if (!dragging) return;
            this.viewport.panByPixels(event.clientX - lastX, event.clientY - lastY);
            lastX = event.clientX;
            lastY = event.clientY;
            this.requestRender();
        });
        this.canvas.addEventListener("pointerup", () => { dragging = false; });

        this.resizeObserver = new ResizeObserver(() => this.requestRender());
        this.resizeObserver.observe(host);
        this.requestRender();
    }

    public requestRender(): void {
        if (!this.disposed) this.lifecycle.requestRender();
    }

    public resetView(): void {
        this.viewport.center = { x: 0, y: 0 };
        this.viewport.zoom = 56;
        this.requestRender();
    }

    public dispose(): void {
        this.disposed = true;
        this.resizeObserver.disconnect();
    }
}
