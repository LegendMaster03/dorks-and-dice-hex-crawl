import { HexCrawlApi } from "./api";
import { CanvasMapRenderer } from "./canvas-renderer";
import { worldToHex } from "./hex-math";
import { RenderLifecycle } from "./render-lifecycle";
import { ensureStyles } from "./styles";
import { deriveToolRoute } from "./tool-route";
import type { DemoWorld } from "./types";
import { Viewport } from "./viewport";

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) throw new Error("Hex Crawl requires #tool-root.");

ensureStyles();
void boot(root);

async function boot(rootElement: HTMLElement): Promise<void> {
    const shell = renderShell(rootElement);
    const viewport = new Viewport();
    let world: DemoWorld | null = null;
    let orientation: "pointy" | "flat" = "pointy";
    let scale = 12;
    let unit: "mi" | "km" = "mi";
    const lifecycle = new RenderLifecycle();
    const renderer = new CanvasMapRenderer(shell.canvas, viewport, () => world);
    lifecycle.register("map", () => renderer.render());

    const { api, context } = await HexCrawlApi.create(rootElement);
    shell.hostMode.textContent = context ? `Embedded: ${context.toolSlug}` : "Standalone";

    const refreshWorld = async (): Promise<void> => {
        shell.error.hidden = true;
        try {
            world = await api.getDemoWorld(orientation, scale, unit);
            shell.scaleValue.textContent = `${world.grid.neighborCenterDistance.value} ${world.grid.neighborCenterDistance.unit.symbol} center-to-center`;
            updateCoordinate();
            lifecycle.requestRender();
        } catch (error) {
            shell.error.hidden = false;
            shell.error.textContent = error instanceof Error ? error.message : String(error);
        }
    };

    shell.orientation.addEventListener("change", () => {
        orientation = shell.orientation.value === "flat" ? "flat" : "pointy";
        void refreshWorld();
    });
    shell.unit.addEventListener("change", () => {
        unit = shell.unit.value === "km" ? "km" : "mi";
        void refreshWorld();
    });
    shell.scale.addEventListener("change", () => {
        const parsed = Number(shell.scale.value);
        scale = Number.isFinite(parsed) && parsed > 0 ? parsed : 12;
        shell.scale.value = String(scale);
        void refreshWorld();
    });
    shell.reset.addEventListener("click", () => {
        viewport.center = { x: 0, y: 0 };
        viewport.zoom = 56;
        lifecycle.requestRender();
    });

    const resizeObserver = new ResizeObserver(() => lifecycle.requestRender());
    resizeObserver.observe(shell.canvas);

    let pointerId: number | null = null;
    let lastX = 0;
    let lastY = 0;
    let moved = false;

    shell.canvas.addEventListener("pointerdown", event => {
        pointerId = event.pointerId;
        lastX = event.clientX;
        lastY = event.clientY;
        moved = false;
        shell.canvas.setPointerCapture(event.pointerId);
        shell.canvas.dataset.dragging = "true";
    });

    shell.canvas.addEventListener("pointermove", event => {
        updateCoordinate(event);
        if (pointerId !== event.pointerId) return;
        const dx = event.clientX - lastX;
        const dy = event.clientY - lastY;
        if (Math.abs(dx) + Math.abs(dy) > 1) moved = true;
        viewport.panByPixels(dx, dy);
        lastX = event.clientX;
        lastY = event.clientY;
        lifecycle.requestRender();
    });

    shell.canvas.addEventListener("pointerup", event => {
        if (pointerId !== event.pointerId) return;
        shell.canvas.releasePointerCapture(event.pointerId);
        shell.canvas.dataset.dragging = "false";
        pointerId = null;
        if (!moved && world) {
            const point = eventToWorld(event);
            renderer.selectedHex = worldToHex(world.grid, point);
            shell.selected.textContent = `${renderer.selectedHex.q}, ${renderer.selectedHex.r}`;
            lifecycle.requestRender();
        }
    });

    shell.canvas.addEventListener("pointerleave", () => {
        if (pointerId === null) shell.hover.textContent = "—";
    });

    shell.canvas.addEventListener("wheel", event => {
        event.preventDefault();
        const rect = shell.canvas.getBoundingClientRect();
        viewport.zoomAt(event.deltaY < 0 ? 1.12 : 1 / 1.12, event.clientX - rect.left, event.clientY - rect.top, rect.width, rect.height);
        lifecycle.requestRender();
    }, { passive: false });

    window.addEventListener("popstate", () => {
        shell.route.textContent = routeFor(rootElement, context?.toolBasePath ?? "/");
    });

    function eventToWorld(event: PointerEvent) {
        const rect = shell.canvas.getBoundingClientRect();
        return viewport.screenToWorld(event.clientX - rect.left, event.clientY - rect.top, rect.width, rect.height);
    }

    function updateCoordinate(event?: PointerEvent): void {
        if (!world) return;
        const rect = shell.canvas.getBoundingClientRect();
        const point = event
            ? viewport.screenToWorld(event.clientX - rect.left, event.clientY - rect.top, rect.width, rect.height)
            : viewport.center;
        const hex = worldToHex(world.grid, point);
        shell.hover.textContent = `${hex.q}, ${hex.r}`;
    }

    shell.route.textContent = routeFor(rootElement, context?.toolBasePath ?? "/");
    await refreshWorld();
}

function renderShell(rootElement: HTMLElement) {
    rootElement.replaceChildren();
    const app = document.createElement("section");
    app.className = "hc-app";
    app.innerHTML = `
        <div class="hc-toolbar">
            <label class="hc-control">Orientation
                <select data-role="orientation"><option value="pointy">Pointy top</option><option value="flat">Flat top</option></select>
            </label>
            <label class="hc-control">Scale
                <input data-role="scale" type="number" min="0.01" step="0.5" value="12">
            </label>
            <label class="hc-control">Unit
                <select data-role="unit"><option value="mi">Miles</option><option value="km">Kilometers</option></select>
            </label>
            <button data-role="reset" type="button">Reset view</button>
            <span data-role="scale-value"></span>
        </div>
        <div class="hc-stage">
            <canvas class="hc-canvas" data-role="canvas"></canvas>
            <div class="hc-overlay">Hex under cursor: <strong data-role="hover">—</strong><br>Selected: <strong data-role="selected">—</strong></div>
        </div>
        <div class="hc-status">
            <span>Host: <strong data-role="host-mode">Loading</strong></span>
            <span>Route: <strong data-role="route">/</strong></span>
            <span>Pan: drag · Zoom: wheel · Select: click</span>
        </div>
        <div class="hc-error" data-role="error" hidden></div>
    `;
    rootElement.append(app);

    const required = <T extends Element>(selector: string): T => {
        const element = app.querySelector<T>(selector);
        if (!element) throw new Error(`Missing Hex Crawl UI element ${selector}.`);
        return element;
    };

    return {
        canvas: required<HTMLCanvasElement>("[data-role=canvas]"),
        orientation: required<HTMLSelectElement>("[data-role=orientation]"),
        scale: required<HTMLInputElement>("[data-role=scale]"),
        unit: required<HTMLSelectElement>("[data-role=unit]"),
        reset: required<HTMLButtonElement>("[data-role=reset]"),
        scaleValue: required<HTMLElement>("[data-role=scale-value]"),
        hover: required<HTMLElement>("[data-role=hover]"),
        selected: required<HTMLElement>("[data-role=selected]"),
        hostMode: required<HTMLElement>("[data-role=host-mode]"),
        route: required<HTMLElement>("[data-role=route]"),
        error: required<HTMLElement>("[data-role=error]")
    };
}

function routeFor(rootElement: HTMLElement, basePath: string): string {
    return deriveToolRoute(basePath, window.location.pathname, rootElement.dataset.toolRoute);
}
