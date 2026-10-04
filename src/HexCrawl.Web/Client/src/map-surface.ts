import { CanvasMapRenderer } from "./canvas-renderer";
import { worldToHex } from "./hex-math";
import { RenderLifecycle } from "./render-lifecycle";
import type { Overworld, WorldPoint } from "./types";
import { Viewport } from "./viewport";

export class MapSurface {
    private static nextAccessibilityId = 0;

    public readonly canvas: HTMLCanvasElement;
    public readonly renderer: CanvasMapRenderer;
    private readonly viewport = new Viewport();
    private readonly lifecycle = new RenderLifecycle();
    private readonly resizeObserver: ResizeObserver;
    private readonly accessibilityStatus: HTMLElement;
    private disposed = false;
    private clickInterceptor: ((point: WorldPoint) => boolean) | null = null;
    private reviewSelectionHandler: ((id: string) => void) | null = null;

    public constructor(
        host: HTMLElement,
        private readonly getWorld: () => Overworld | null,
        private readonly onWorldClick?: (point: WorldPoint) => void) {
        const accessibilityId = ++MapSurface.nextAccessibilityId;
        const help = document.createElement("p");
        help.id = `hc-map-help-${accessibilityId}`;
        help.className = "hc-sr-only";
        help.textContent = "Interactive hex map. Drag to pan, use the wheel to zoom, arrow keys pan, plus and minus zoom, Home resets the view, Enter or Space selects and activates the point at the center of the map, and Escape clears the selected hex.";

        this.accessibilityStatus = document.createElement("p");
        this.accessibilityStatus.id = `hc-map-status-${accessibilityId}`;
        this.accessibilityStatus.className = "hc-sr-only";
        this.accessibilityStatus.setAttribute("role", "status");
        this.accessibilityStatus.setAttribute("aria-live", "polite");
        this.accessibilityStatus.setAttribute("aria-atomic", "true");

        this.canvas = document.createElement("canvas");
        this.canvas.className = "hc-map-canvas";
        this.canvas.tabIndex = 0;
        this.canvas.setAttribute("role", "region");
        this.canvas.setAttribute("aria-describedby", `${help.id} ${this.accessibilityStatus.id}`);
        this.canvas.setAttribute("aria-keyshortcuts", "ArrowUp ArrowDown ArrowLeft ArrowRight + - Home Enter Space Escape");
        host.replaceChildren(this.canvas, help, this.accessibilityStatus);

        this.renderer = new CanvasMapRenderer(this.canvas, this.viewport, this.getWorld, () => this.requestRender());
        this.lifecycle.register("map", () => {
            this.renderer.render();
            this.updateAccessibilityLabel();
        });

        let suppressNextClick = false;
        this.canvas.addEventListener("click", event => {
            if (event.button !== 0) return;
            if (suppressNextClick) {
                suppressNextClick = false;
                return;
            }
            const rect = this.canvas.getBoundingClientRect();
            this.activatePoint(this.viewport.screenToWorld(
                event.clientX - rect.left,
                event.clientY - rect.top,
                rect.width,
                rect.height));
        });
        this.canvas.addEventListener("keydown", event => this.handleKeyDown(event));
        this.canvas.addEventListener("wheel", event => {
            event.preventDefault();
            const rect = this.canvas.getBoundingClientRect();
            this.viewport.zoomAt(
                event.deltaY < 0 ? 1.12 : 1 / 1.12,
                event.clientX - rect.left,
                event.clientY - rect.top,
                rect.width,
                rect.height);
            this.announceView("Map zoom changed");
            this.requestRender();
        }, { passive: false });

        const dragThresholdPixels = 4;
        let activePointerId: number | null = null;
        let pointerButton = 0;
        let dragging = false;
        let startX = 0;
        let startY = 0;
        let lastX = 0;
        let lastY = 0;

        this.canvas.addEventListener("pointerdown", event => {
            if (event.button !== 0 && event.button !== 1) return;
            activePointerId = event.pointerId;
            pointerButton = event.button;
            dragging = event.button === 1;
            startX = lastX = event.clientX;
            startY = lastY = event.clientY;
            this.canvas.setPointerCapture(event.pointerId);
            if (dragging) this.canvas.style.cursor = "grabbing";
        });
        this.canvas.addEventListener("pointermove", event => {
            if (activePointerId !== event.pointerId) return;
            if (!dragging) {
                if (Math.hypot(event.clientX - startX, event.clientY - startY) < dragThresholdPixels) return;
                dragging = true;
                this.canvas.style.cursor = "grabbing";
            }

            this.viewport.panByPixels(event.clientX - lastX, event.clientY - lastY);
            lastX = event.clientX;
            lastY = event.clientY;
            event.preventDefault();
            this.requestRender();
        });
        const finishPointer = (event: PointerEvent, canceled: boolean): void => {
            if (activePointerId !== event.pointerId) return;
            const wasDragging = dragging;
            const wasLeftButton = pointerButton === 0;
            if (this.canvas.hasPointerCapture(event.pointerId)) this.canvas.releasePointerCapture(event.pointerId);
            activePointerId = null;
            dragging = false;
            this.canvas.style.cursor = "";
            if (wasDragging && wasLeftButton && !canceled) {
                suppressNextClick = true;
                window.setTimeout(() => { suppressNextClick = false; }, 0);
            }
            if (wasDragging) this.announceView("Map panned");
        };
        this.canvas.addEventListener("pointerup", event => finishPointer(event, false));
        this.canvas.addEventListener("pointercancel", event => finishPointer(event, true));

        this.resizeObserver = new ResizeObserver(() => this.requestRender());
        this.resizeObserver.observe(host);
        this.requestRender();
    }

    public setClickInterceptor(interceptor: ((point: WorldPoint) => boolean) | null): void {
        this.clickInterceptor = interceptor;
    }

    public setReviewSelectionHandler(handler: ((id: string) => void) | null): void {
        this.reviewSelectionHandler = handler;
    }

    public requestRender(): void {
        if (!this.disposed) this.lifecycle.requestRender();
    }

    public resetView(): void {
        this.viewport.center = { x: 0, y: 0 };
        this.viewport.zoom = 56;
        this.announceView("Map view reset");
        this.requestRender();
    }

    public dispose(): void {
        this.disposed = true;
        this.clickInterceptor = null;
        this.reviewSelectionHandler = null;
        this.resizeObserver.disconnect();
        this.renderer.dispose();
    }

    private activatePoint(point: WorldPoint): void {
        if (this.clickInterceptor?.(point)) return;

        const reviewId = this.renderer.hitTestReview(point);
        if (reviewId && this.reviewSelectionHandler) {
            this.reviewSelectionHandler(reviewId);
            return;
        }

        const world = this.getWorld();
        if (world) {
            const grid = this.renderer.gridPreview ?? world.grid;
            const selected = worldToHex(grid, point);
            this.renderer.selectedHex = selected;
            this.accessibilityStatus.textContent = `Selected hex q ${selected.q}, r ${selected.r}.`;
            this.requestRender();
        } else {
            this.accessibilityStatus.textContent = `Selected map position ${point.x.toFixed(2)}, ${point.y.toFixed(2)}.`;
        }

        this.onWorldClick?.(point);
    }

    private handleKeyDown(event: KeyboardEvent): void {
        if (event.key === "ArrowLeft") this.panViewport(48, 0);
        else if (event.key === "ArrowRight") this.panViewport(-48, 0);
        else if (event.key === "ArrowUp") this.panViewport(0, 48);
        else if (event.key === "ArrowDown") this.panViewport(0, -48);
        else if (event.key === "+" || event.key === "=") this.zoomViewport(1.12);
        else if (event.key === "-" || event.key === "_") this.zoomViewport(1 / 1.12);
        else if (event.key === "Home") this.resetView();
        else if (event.key === "Enter" || event.key === " ") {
            const rect = this.canvas.getBoundingClientRect();
            this.activatePoint(this.viewport.screenToWorld(rect.width / 2, rect.height / 2, rect.width, rect.height));
        } else if (event.key === "Escape") {
            this.renderer.selectedHex = null;
            this.accessibilityStatus.textContent = "Selected hex cleared.";
            this.requestRender();
        } else {
            return;
        }

        event.preventDefault();
    }

    private panViewport(dx: number, dy: number): void {
        this.viewport.panByPixels(dx, dy);
        this.announceView("Map panned");
        this.requestRender();
    }

    private zoomViewport(factor: number): void {
        const rect = this.canvas.getBoundingClientRect();
        this.viewport.zoomAt(factor, rect.width / 2, rect.height / 2, rect.width, rect.height);
        this.announceView("Map zoom changed");
        this.requestRender();
    }

    private announceView(prefix: string): void {
        this.accessibilityStatus.textContent =
            `${prefix}. Map center is ${this.viewport.center.x.toFixed(2)}, ${this.viewport.center.y.toFixed(2)}.`;
    }

    private updateAccessibilityLabel(): void {
        const world = this.getWorld();
        const parts = ["Interactive hex map"];
        if (this.renderer.expeditionHex) {
            parts.push(`Current expedition hex q ${this.renderer.expeditionHex.q}, r ${this.renderer.expeditionHex.r}`);
        }
        if (this.renderer.selectedHex) {
            parts.push(`Selected hex q ${this.renderer.selectedHex.q}, r ${this.renderer.selectedHex.r}`);
        }
        if (world) {
            parts.push(`${world.locations.length} authored locations and ${world.features.length} authored map features`);
        }
        this.canvas.setAttribute("aria-label", parts.join(". "));
    }
}
