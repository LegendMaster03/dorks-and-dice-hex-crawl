import { sourceMapAssetUrl } from "./api";
import { hexCorners, hexToWorld, visibleHexBounds } from "./hex-math";
import { RasterImageCache } from "./raster-image-cache";
import type { DemoWorld, HexCoordinate, MapRegistrationTransform, SpatialFeature, WorldPoint } from "./types";

export type MapReviewGeometry = {
    id: string;
    kind: "Point" | "Line" | "Region";
    position: WorldPoint | null;
    points: WorldPoint[];
};

export type MapReviewOverlay = {
    items: MapReviewGeometry[];
    selectedId: string | null;
};
import { Viewport } from "./viewport";

export class CanvasMapRenderer {
    public selectedHex: HexCoordinate | null = null;
    public expeditionHex: HexCoordinate | null = null;
    public discoveredSubjectIds = new Set<string>();
    public hiddenSourceMapIds = new Set<string>();
    public registrationPreview: { sourceMapId: string; transform: MapRegistrationTransform } | null = null;
    public reviewOverlay: MapReviewOverlay | null = null;
    private readonly rasterCache = new RasterImageCache();

    public constructor(
        private readonly canvas: HTMLCanvasElement,
        private readonly viewport: Viewport,
        private getWorld: () => DemoWorld | null,
        private readonly requestRender: () => void = () => {}) {}

    public resizeToDisplaySize(): boolean {
        const ratio = Math.max(1, window.devicePixelRatio || 1);
        const width = Math.max(1, Math.floor(this.canvas.clientWidth * ratio));
        const height = Math.max(1, Math.floor(this.canvas.clientHeight * ratio));
        if (this.canvas.width === width && this.canvas.height === height) return false;
        this.canvas.width = width;
        this.canvas.height = height;
        return true;
    }

    public render(): void {
        this.resizeToDisplaySize();
        const world = this.getWorld();
        const ctx = this.canvas.getContext("2d");
        if (!ctx || !world) return;

        const ratio = Math.max(1, window.devicePixelRatio || 1);
        const width = this.canvas.width / ratio;
        const height = this.canvas.height / ratio;
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        ctx.clearRect(0, 0, width, height);

        this.drawSourceMaps(ctx, world, width, height);
        this.drawReviewOverlay(ctx, width, height);
        this.drawRegions(ctx, world, width, height);
        this.drawGrid(ctx, world, width, height);
        this.drawLinesAndPoints(ctx, world.features, width, height);
        this.drawLocations(ctx, world, width, height);
        this.drawSelection(ctx, world, width, height);
        this.drawExpedition(ctx, world, width, height);
    }

    public hitTestReview(point: WorldPoint): string | null {
        const overlay = this.reviewOverlay;
        if (!overlay) return null;
        const tolerance = 12 / this.viewport.zoom;

        for (const item of [...overlay.items].reverse()) {
            if (item.kind === "Point" && item.position) {
                if (distance(point, item.position) <= tolerance) return item.id;
                continue;
            }

            if (item.kind === "Region" && item.points.length >= 3 && pointInPolygon(point, item.points)) {
                return item.id;
            }

            for (let index = 1; index < item.points.length; index++) {
                if (distanceToSegment(point, item.points[index - 1], item.points[index]) <= tolerance) {
                    return item.id;
                }
            }
            if (item.kind === "Region"
                && item.points.length >= 3
                && distanceToSegment(
                    point,
                    item.points[item.points.length - 1],
                    item.points[0]) <= tolerance) {
                return item.id;
            }
        }

        return null;
    }

    public dispose(): void {
        this.rasterCache.dispose();
    }

    private drawSourceMaps(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        const activeUrls = new Set<string>();
        for (const map of world.sourceMaps) {
            const url = sourceMapAssetUrl(world.id, map.id);
            activeUrls.add(url);
            if (this.hiddenSourceMapIds.has(map.id)) continue;
            const transform = this.registrationPreview?.sourceMapId === map.id
                ? this.registrationPreview.transform
                : map.alignment;
            if (!transform || transform.kind !== "Affine") continue;
            const image = this.rasterCache.get(url, this.requestRender);
            if (!image) continue;
            this.drawAffineRaster(ctx, image, transform, width, height, this.registrationPreview?.sourceMapId === map.id);
        }
        this.rasterCache.prune(activeUrls);
    }

    private drawAffineRaster(
        ctx: CanvasRenderingContext2D,
        image: CanvasImageSource,
        transform: MapRegistrationTransform,
        width: number,
        height: number,
        preview: boolean): void {
        const zoom = this.viewport.zoom;
        const a = zoom * transform.m11;
        const b = zoom * transform.m21;
        const c = zoom * transform.m12;
        const d = zoom * transform.m22;
        const e = width / 2 + zoom * (transform.m13 - this.viewport.center.x);
        const f = height / 2 + zoom * (transform.m23 - this.viewport.center.y);
        ctx.save();
        ctx.globalAlpha = preview ? 0.72 : 1;
        ctx.transform(a, b, c, d, e, f);
        ctx.drawImage(image, 0, 0);
        ctx.restore();
    }

    private drawReviewOverlay(
        ctx: CanvasRenderingContext2D,
        width: number,
        height: number): void {
        const overlay = this.reviewOverlay;
        if (!overlay) return;

        for (const item of overlay.items) {
            const selected = item.id === overlay.selectedId;
            ctx.save();
            ctx.strokeStyle = selected ? "rgba(166, 63, 30, .98)" : "rgba(180, 111, 28, .78)";
            ctx.fillStyle = selected ? "rgba(210, 91, 46, .26)" : "rgba(222, 164, 72, .16)";
            ctx.lineWidth = selected ? 5 : 3;
            ctx.setLineDash(selected ? [] : [8, 5]);

            if (item.kind === "Point" && item.position) {
                const point = this.toScreen(item.position, width, height);
                ctx.beginPath();
                ctx.arc(point.x, point.y, selected ? 11 : 8, 0, Math.PI * 2);
                ctx.fill();
                ctx.stroke();
            } else if (item.points.length > 0) {
                const points = item.points.map(point => this.toScreen(point, width, height));
                ctx.beginPath();
                points.forEach((point, index) =>
                    index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
                if (item.kind === "Region") {
                    ctx.closePath();
                    ctx.fill();
                }
                ctx.stroke();
            }

            ctx.restore();
        }
    }

    private drawGrid(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        const corners = [
            this.viewport.screenToWorld(0, 0, width, height),
            this.viewport.screenToWorld(width, 0, width, height),
            this.viewport.screenToWorld(width, height, width, height),
            this.viewport.screenToWorld(0, height, width, height)
        ];
        const bounds = visibleHexBounds(world.grid, corners);
        ctx.strokeStyle = "rgba(52, 67, 55, .38)";
        ctx.lineWidth = 1;
        ctx.font = "11px ui-monospace, SFMono-Regular, Menlo, monospace";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillStyle = "rgba(35, 49, 39, .72)";

        for (let q = bounds.minQ; q <= bounds.maxQ; q++) {
            for (let r = bounds.minR; r <= bounds.maxR; r++) {
                const hex = { q, r };
                const screenCorners = hexCorners(world.grid, hex).map(point => this.toScreen(point, width, height));
                ctx.beginPath();
                screenCorners.forEach((point, index) => index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
                ctx.closePath();
                ctx.stroke();
                if (this.viewport.zoom >= 38) {
                    const center = this.toScreen(hexToWorld(world.grid, hex), width, height);
                    ctx.fillText(`${q},${r}`, center.x, center.y);
                }
            }
        }
    }

    private drawRegions(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        for (const feature of world.features.filter(feature => feature.kind === "Region" && feature.boundary?.length)) {
            const points = feature.boundary!.map(point => this.toScreen(point, width, height));
            ctx.beginPath();
            points.forEach((point, index) => index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
            ctx.closePath();
            ctx.fillStyle = feature.category === "forest" ? "rgba(79, 118, 77, .22)" : "rgba(118, 106, 77, .18)";
            ctx.fill();
            if (this.discoveredSubjectIds.has(feature.id)) {
                ctx.strokeStyle = "#2f6b3b";
                ctx.lineWidth = 3;
                ctx.stroke();
            }
        }
    }

    private drawLinesAndPoints(ctx: CanvasRenderingContext2D, features: SpatialFeature[], width: number, height: number): void {
        for (const feature of features) {
            if (feature.kind === "Line" && feature.path?.length) {
                const points = feature.path.map(point => this.toScreen(point, width, height));
                ctx.beginPath();
                points.forEach((point, index) => index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
                ctx.strokeStyle = feature.category === "river" ? "#4479a1" : "#775f41";
                ctx.lineWidth = feature.category === "river" ? 4 : 3;
                ctx.stroke();
                if (this.discoveredSubjectIds.has(feature.id)) {
                    ctx.strokeStyle = "#2f6b3b";
                    ctx.lineWidth += 3;
                    ctx.stroke();
                }
            } else if (feature.kind === "Point" && feature.position) {
                const point = this.toScreen(feature.position, width, height);
                ctx.beginPath();
                ctx.arc(point.x, point.y, 5, 0, Math.PI * 2);
                ctx.fillStyle = "#4b3d32";
                ctx.fill();
                if (this.discoveredSubjectIds.has(feature.id)) {
                    ctx.beginPath();
                    ctx.arc(point.x, point.y, 9, 0, Math.PI * 2);
                    ctx.strokeStyle = "#2f6b3b";
                    ctx.lineWidth = 2;
                    ctx.stroke();
                }
            }
        }
    }

    private drawLocations(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        ctx.font = "600 12px system-ui, sans-serif";
        ctx.textAlign = "left";
        ctx.textBaseline = "middle";
        for (const location of world.locations) {
            const point = this.toScreen(location.position, width, height);
            ctx.beginPath();
            ctx.arc(point.x, point.y, 7, 0, Math.PI * 2);
            ctx.fillStyle = location.discoverability === "Hidden" ? "#7b3d52" : "#302d29";
            ctx.fill();
            if (this.discoveredSubjectIds.has(location.id)) {
                ctx.beginPath();
                ctx.arc(point.x, point.y, 11, 0, Math.PI * 2);
                ctx.strokeStyle = "#2f6b3b";
                ctx.lineWidth = 3;
                ctx.stroke();
            }
            ctx.fillStyle = "#1f2420";
            ctx.fillText(location.name, point.x + 10, point.y);
        }
    }

    private drawSelection(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        if (!this.selectedHex) return;
        const points = hexCorners(world.grid, this.selectedHex).map(point => this.toScreen(point, width, height));
        ctx.beginPath();
        points.forEach((point, index) => index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
        ctx.closePath();
        ctx.fillStyle = "rgba(210, 139, 39, .24)";
        ctx.fill();
        ctx.strokeStyle = "#9b5f12";
        ctx.lineWidth = 3;
        ctx.stroke();
    }

    private drawExpedition(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        if (!this.expeditionHex) return;
        const point = this.toScreen(hexToWorld(world.grid, this.expeditionHex), width, height);
        ctx.beginPath();
        ctx.arc(point.x, point.y, 10, 0, Math.PI * 2);
        ctx.fillStyle = "#b22626";
        ctx.fill();
        ctx.strokeStyle = "#fff";
        ctx.lineWidth = 3;
        ctx.stroke();
        ctx.font = "700 11px system-ui, sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillStyle = "#fff";
        ctx.fillText("E", point.x, point.y + .5);
    }

    private toScreen(point: WorldPoint, width: number, height: number): WorldPoint {
        return this.viewport.worldToScreen(point, width, height);
    }
}

function distance(a: WorldPoint, b: WorldPoint): number {
    return Math.hypot(a.x - b.x, a.y - b.y);
}

function distanceToSegment(point: WorldPoint, start: WorldPoint, end: WorldPoint): number {
    const dx = end.x - start.x;
    const dy = end.y - start.y;
    const lengthSquared = (dx * dx) + (dy * dy);
    if (lengthSquared <= Number.EPSILON) return distance(point, start);
    const t = Math.max(0, Math.min(1,
        (((point.x - start.x) * dx) + ((point.y - start.y) * dy)) / lengthSquared));
    return distance(point, {
        x: start.x + (t * dx),
        y: start.y + (t * dy)
    });
}

function pointInPolygon(point: WorldPoint, polygon: readonly WorldPoint[]): boolean {
    let inside = false;
    for (let index = 0, previous = polygon.length - 1; index < polygon.length; previous = index++) {
        const a = polygon[index];
        const b = polygon[previous];
        const intersects = ((a.y > point.y) !== (b.y > point.y))
            && (point.x < ((b.x - a.x) * (point.y - a.y) / ((b.y - a.y) || Number.EPSILON)) + a.x);
        if (intersects) inside = !inside;
    }
    return inside;
}
