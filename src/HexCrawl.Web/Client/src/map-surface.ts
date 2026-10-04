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
        help.textContent = "Interactive hex map. Use the visible map controls, arrow keys, plus and minus, or Home for navigation. Shift-drag or middle-drag pans, the wheel zooms, Enter or Space selects and activates the point at the center of the map, and Escape clears the selected hex.";

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

        host.style.position = "relative";
        host.replaceChildren(this.canvas, this.createNavigationToolbar(), help, this.accessibilityStatus);

        this.renderer = new CanvasMapRenderer(this.canvas, this.viewport, this.getWorld, () => this.requestRender());
        this.lifecycle.register("map", () => {
            this.renderer.render();
            this.updateAccessibilityLabel();
        });

        this.canvas.addEventListener("click", event => {
            if (event.button !== 0) return;
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

    private createNavigationToolbar(): HTMLElement {
        const toolbar = document.createElement("div");
        toolbar.setAttribute("role", "group");
        toolbar.setAttribute("aria-label", "Map navigation controls");
        toolbar.style.cssText = "position:absolute;z-index:2;top:.55rem;left:.55rem;display:flex;flex-wrap:wrap;align-items:center;gap:.3rem;max-width:calc(100% - 1.1rem);padding:.35rem;border:1px solid var(--hc-border);border-radius:.5rem;background:var(--hc-surface);box-shadow:0 .1rem .35rem rgba(0,0,0,.18)";

        const label = document.createElement("span");
        label.textContent = "Map controls";
        label.style.cssText = "padding:0 .25rem;color:var(--hc-text-strong);font-size:.78rem;font-weight:700";
        toolbar.append(label);

        const addButton = (text: string, title: string, action: () => void): void => {
            const button = document.createElement("button");
            button.type = "button";
            button.textContent = text;
            button.title = title;
            button.setAttribute("aria-label", title);
            button.style.cssText = "min-width:2.15rem;padding:.3rem .45rem";
            button.addEventListener("click", action);
            toolbar.append(button);
        };

        addButton("←", "Pan map left", () => this.panViewport(48, 0));
        addButton("↑", "Pan map up", () => this.panViewport(0, 48));
        addButton("↓", "Pan map down", () => this.panViewport(0, -48));
        addButton("→", "Pan map right", () => this.panViewport(-48, 0));
        addButton("−", "Zoom map out", () => this.zoomViewport(1 / 1.12));
        addButton("+", "Zoom map in", () => this.zoomViewport(1.12));
        addButton("Reset", "Reset map view", () => this.resetView());
        return toolbar;
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
