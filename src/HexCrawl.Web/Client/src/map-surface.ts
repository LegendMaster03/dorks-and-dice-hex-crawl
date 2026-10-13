import { CanvasMapRenderer } from "./canvas-renderer";
import type { HexCrawlApi } from "./api";
import { RenderLifecycle } from "./render-lifecycle";
import { resolveLatestWorldCellSelection } from "./map-selection-policy";
import type { Overworld, WorldPoint, WorldCellGeometry } from "./types";
import { Viewport } from "./viewport";

export class MapSurface {
    private static nextAccessibilityId = 0;

    public readonly canvas: HTMLCanvasElement;
    public readonly renderer: CanvasMapRenderer;
    private readonly viewport = new Viewport();
    private readonly lifecycle = new RenderLifecycle();
    private readonly resizeObserver: ResizeObserver;
    private readonly accessibilityHelp: HTMLElement;
    private readonly accessibilityStatus: HTMLElement;
    private disposed = false;
    private clickInterceptor: ((point: WorldPoint) => boolean) | null = null;
    private reviewSelectionHandler: ((id: string) => void) | null = null;
    private cellSelectionHandler: ((cell: WorldCellGeometry | null) => void) | null = null;
    private loadedCells: { worldKey: string; bounds: { minX: number; minY: number; maxX: number; maxY: number } } | null = null;
    private pendingCellsKey: string | null = null;
    private requestedCellsKey: string | null = null;
    private cellRequestGeneration = 0;
    private cellSelectionGeneration = 0;

    public constructor(
        host: HTMLElement,
        private readonly getWorld: () => Overworld | null,
        private readonly onWorldClick?: (point: WorldPoint) => void,
        private readonly api?: HexCrawlApi) {
        const accessibilityId = ++MapSurface.nextAccessibilityId;
        this.accessibilityHelp = document.createElement("p");
        this.accessibilityHelp.id = `hc-map-help-${accessibilityId}`;
        this.accessibilityHelp.className = "hc-sr-only";
        this.accessibilityHelp.textContent = "Interactive world map. Drag to pan, use the wheel to zoom, arrow keys pan, plus and minus zoom, Home resets the view, Enter or Space selects a cell at the map center, and Escape clears the selection. Click inside a cell rather than on a shared edge.";

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
        this.canvas.setAttribute("aria-describedby", `${this.accessibilityHelp.id} ${this.accessibilityStatus.id}`);
        this.canvas.setAttribute("aria-keyshortcuts", "ArrowUp ArrowDown ArrowLeft ArrowRight + - Home Enter Space Escape");
        host.replaceChildren(this.canvas, this.accessibilityHelp, this.accessibilityStatus);

        const initialWorld = this.getWorld();
        if (initialWorld?.tiling) {
            this.viewport.center = { ...initialWorld.tiling.origin };
        }
        this.renderer = new CanvasMapRenderer(this.canvas, this.viewport, this.getWorld, () => this.requestRender());
        this.lifecycle.register("map", () => {
            this.loadVisibleCells();
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

    public attach(host: HTMLElement): void {
        if (this.disposed) throw new Error("Can not attach a disposed map surface.");
        this.resizeObserver.disconnect();
        host.replaceChildren(this.canvas, this.accessibilityHelp, this.accessibilityStatus);
        this.resizeObserver.observe(host);
        this.requestRender();
    }

    public setClickInterceptor(interceptor: ((point: WorldPoint) => boolean) | null): void {
        this.clickInterceptor = interceptor;
    }

    public setReviewSelectionHandler(handler: ((id: string) => void) | null): void {
        this.reviewSelectionHandler = handler;
    }

    public setCellSelectionHandler(handler: ((cell: WorldCellGeometry | null) => void) | null): void {
        this.cellSelectionHandler = handler;
    }

    private loadVisibleCells(): void {
        const world = this.getWorld();
        if (!world?.tiling || !this.api) return;
        const width = this.canvas.clientWidth;
        const height = this.canvas.clientHeight;
        if (!(width > 0 && height > 0) || !Number.isFinite(this.viewport.zoom)) return;
        const corners = [
            this.viewport.screenToWorld(0, 0, width, height),
            this.viewport.screenToWorld(width, 0, width, height),
            this.viewport.screenToWorld(width, height, width, height),
            this.viewport.screenToWorld(0, height, width, height)
        ];
        const extent = {
            minX: Math.min(...corners.map(p => p.x)),
            minY: Math.min(...corners.map(p => p.y)),
            maxX: Math.max(...corners.map(p => p.x)),
            maxY: Math.max(...corners.map(p => p.y))
        };
        // The world aggregate version also changes for facts, map images and
        // names. Only the authoritative topology revision invalidates
        // the polygon cache or selected qualified cell.
        const worldKey = `${world.id}:${world.tiling.id}:${world.tiling.revision}`;
        const cached = this.loadedCells;
        if (cached && cached.worldKey !== worldKey) {
            this.loadedCells = null;
            this.renderer.visibleWorldCells = [];
            this.renderer.selectedWorldCell = null;
            this.cellSelectionGeneration++;
            this.cellSelectionHandler?.(null);
        }
        if (cached?.worldKey === worldKey
            && extent.minX >= cached.bounds.minX && extent.maxX <= cached.bounds.maxX
            && extent.minY >= cached.bounds.minY && extent.maxY <= cached.bounds.maxY) return;
        const padX = Math.max((extent.maxX - extent.minX) * .25, 0);
        const padY = Math.max((extent.maxY - extent.minY) * .25, 0);
        const bounds = {
            minX: extent.minX - padX, minY: extent.minY - padY,
            maxX: extent.maxX + padX, maxY: extent.maxY + padY
        };
        if (Object.values(bounds).some(value => !Number.isFinite(value))) return;
        const queryKey = `${worldKey}:${Object.values(bounds).join(":")}`;
        if (queryKey === this.pendingCellsKey || queryKey === this.requestedCellsKey) return;
        this.renderer.visibleWorldCells = [];
        this.pendingCellsKey = queryKey;
        const generation = ++this.cellRequestGeneration;
        void this.api.listVisibleCells(world.id, bounds).then(cells => {
            if (this.disposed || generation !== this.cellRequestGeneration) return;
            this.loadedCells = { worldKey, bounds };
            this.renderer.visibleWorldCells = cells;
            this.accessibilityStatus.textContent = `${cells.length} cells available in the visible map area.`;
            this.requestRender();
        }).catch(() => {
            if (this.disposed || generation !== this.cellRequestGeneration) return;
            this.accessibilityStatus.textContent =
                "Cell boundaries are unavailable at this zoom. Zoom in or retry the map.";
            // Do not retry the same overflowing viewport on every animation frame.
            this.requestRender();
        }).finally(() => {
            if (generation === this.cellRequestGeneration) {
                this.pendingCellsKey = null;
                this.requestedCellsKey = queryKey;
            }
        });
    }

    public requestRender(): void {
        if (!this.disposed) this.lifecycle.requestRender();
    }

    public centerOn(point: WorldPoint): void {
        if (!Number.isFinite(point.x) || !Number.isFinite(point.y))
            throw new Error("Map center requires a finite world position.");
        this.viewport.center = { ...point };
        this.requestRender();
    }

    public resetView(): void {
        const world = this.getWorld();
        this.viewport.center = world?.tiling
            ? { ...world.tiling.origin }
            : !world && this.renderer.currentWorldCell
                ? { ...this.renderer.currentWorldCell.center }
                : { x: 0, y: 0 };
        this.viewport.zoom = 56;
        this.announceView("Map view reset");
        this.requestRender();
    }

    public dispose(): void {
        this.disposed = true;
        this.clickInterceptor = null;
        this.reviewSelectionHandler = null;
        this.cellSelectionHandler = null;
        this.cellRequestGeneration++;
        this.cellSelectionGeneration++;
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
        if (world?.tiling && this.api) {
            void this.activateWorldCell(world.id, world.tiling.id, point);
            return;
        }
        this.accessibilityStatus.textContent =
            `Selected map position ${point.x.toFixed(2)}, ${point.y.toFixed(2)}.`;
        this.onWorldClick?.(point);
    }

    private async activateWorldCell(worldId: string, tilingId: string, point: WorldPoint): Promise<void> {
        // Feature/location authoring consumes the exact clicked world point
        // immediately. A shared cell boundary may be ambiguous for selection,
        // but must not discard that valid geometry placement.
        this.onWorldClick?.(point);
        if (!this.api) return;
        const generation = ++this.cellSelectionGeneration;
        const version = this.getWorld()?.version;
        const revision = this.getWorld()?.tiling?.revision;
        const isCurrent = () => !this.disposed && generation === this.cellSelectionGeneration
            && this.getWorld()?.id === worldId
            && this.getWorld()?.tiling?.id === tilingId
            && this.getWorld()?.tiling?.revision === revision
            && this.getWorld()?.version === version;
        try {
            await resolveLatestWorldCellSelection(
                () => this.api!.lookupWorldCell(worldId, point),
                id => this.api!.resolveWorldCell(worldId, id),
                isCurrent,
                tilingId,
                status => {
                    this.accessibilityStatus.textContent = status === "Ambiguous"
                        ? "Multiple cells meet here. Select a point inside the desired cell."
                        : "No cell is at that map point.";
                },
                geometry => {
                    this.renderer.selectedWorldCell = geometry;
                    this.accessibilityStatus.textContent = "Selected a world cell.";
                    this.cellSelectionHandler?.(geometry);
                    this.requestRender();
                });
        } catch {
            if (isCurrent()) this.accessibilityStatus.textContent =
                "Cell selection failed. Retry after the map is available.";
        }
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
            this.renderer.selectedWorldCell = null;
            this.cellSelectionGeneration++;
            this.accessibilityStatus.textContent = "Selected cell cleared.";
            this.cellSelectionHandler?.(null);
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
        const parts = ["Interactive world map"];
        if (this.renderer.currentWorldCell) parts.push("Current expedition cell is outlined");
        if (this.renderer.selectedWorldCell) parts.push("A world cell is selected");
        if (world) {
            parts.push(`${world.locations.length} authored locations and ${world.features.length} authored map features`);
        }
        this.canvas.setAttribute("aria-label", parts.join(". "));
    }
}