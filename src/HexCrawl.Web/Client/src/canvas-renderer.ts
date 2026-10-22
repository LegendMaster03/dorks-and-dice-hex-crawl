import { sourceMapAssetUrl } from "./api";
import { RasterImageCache } from "./raster-image-cache";
import { mapCategoryColor } from "./map-appearance";
import { traceTilePolygon } from "./tile-polygon-canvas";
import type { DemoWorld, MapRegistrationTransform, SpatialFeature, WorldPoint, WorldCellGeometry } from "./types";

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
    public selectedWorldCell: WorldCellGeometry | null = null;
    public currentWorldCell: WorldCellGeometry | null = null;
    public visibleWorldCells: WorldCellGeometry[] = [];
    public terrainByCell = new Map<string, string>();
    public showGrid = true;
    public showLabels = true;
    public expeditionPosition: WorldPoint | null = null;
    public discoveredSubjectIds = new Set<string>();
    public hiddenSourceMapIds = new Set<string>();
    public registrationPreview: { sourceMapId: string; transform: MapRegistrationTransform } | null = null;
    public reviewOverlay: MapReviewOverlay | null = null;
    private readonly rasterCache = new RasterImageCache();
    private readonly provisionalSourceMapTransforms = new Map<string, MapRegistrationTransform>();

    public constructor(
        private readonly canvas: HTMLCanvasElement,
        private readonly viewport: Viewport,
        private getWorld: () => DemoWorld | null,
        private readonly requestRender: () => void = () => {},
        private readonly fixedSize = false) {}

    public resizeToDisplaySize(): boolean {
        if (this.fixedSize) return false;
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
        if (!ctx) return;

        const ratio = this.fixedSize ? 1 : Math.max(1, window.devicePixelRatio || 1);
        const width = this.canvas.width / ratio;
        const height = this.canvas.height / ratio;
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        ctx.clearRect(0, 0, width, height);

        if (world) {
            this.drawSourceMaps(ctx, world, width, height);
            this.drawReviewOverlay(ctx, width, height);
            this.drawRegions(ctx, world, width, height);
            if (this.showGrid) this.drawGrid(ctx, world, width, height);
            this.drawLinesAndPoints(ctx, world, width, height);
            this.drawLocations(ctx, world, width, height);
            this.drawSelection(ctx, world, width, height);
            if (this.showLabels) this.drawMapLabels(ctx, world, width, height);
        }
        // Mapless V2 sessions still have authoritative cell geometry.
        // Render it without inventing an authored world or source image.
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

    /** Wait for raster layers before encoding an export; never silently drop them. */
    public async waitForSourceMaps(): Promise<void> {
        const world = this.getWorld();
        if (!world) return;
        await Promise.all(world.sourceMaps.filter(map => !this.hiddenSourceMapIds.has(map.id))
            .map(map => new Promise<void>((resolve, reject) => {
                const url = sourceMapAssetUrl(world.id, map.id);
                const timeout = window.setTimeout(() =>
                    reject(new Error(`Map image "${map.name}" did not load for export. Retry after the image is available.`)), 15000);
                const check = (): void => {
                    if (this.rasterCache.get(url, check)) {
                        window.clearTimeout(timeout);
                        resolve();
                    }
                };
                check();
            })));
    }

    public dispose(): void {
        this.rasterCache.dispose();
        this.provisionalSourceMapTransforms.clear();
    }

    private drawSourceMaps(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        const activeUrls = new Set<string>();
        const activeMapIds = new Set<string>();
        for (const map of world.sourceMaps) {
            activeMapIds.add(map.id);
            const url = sourceMapAssetUrl(world.id, map.id);
            activeUrls.add(url);
            if (this.hiddenSourceMapIds.has(map.id)) continue;

            const image = this.rasterCache.get(url, this.requestRender);
            if (!image) continue;

            const previewTransform = this.registrationPreview?.sourceMapId === map.id
                ? this.registrationPreview.transform
                : null;
            const savedTransform = map.alignment?.kind === "Affine" ? map.alignment : null;
            const provisional = previewTransform == null && savedTransform == null;
            if (!provisional) this.provisionalSourceMapTransforms.delete(map.id);
            const transform = previewTransform
                ?? savedTransform
                ?? this.provisionalSourceMapTransform(map.id, image, width, height);
            if (!transform || transform.kind !== "Affine") continue;

            this.drawAffineRaster(ctx, image, transform, width, height, previewTransform != null || provisional);
        }
        this.rasterCache.prune(activeUrls);
        for (const sourceMapId of this.provisionalSourceMapTransforms.keys()) {
            if (!activeMapIds.has(sourceMapId)) this.provisionalSourceMapTransforms.delete(sourceMapId);
        }
    }

    private provisionalSourceMapTransform(
        sourceMapId: string,
        image: CanvasImageSource,
        width: number,
        height: number): MapRegistrationTransform | null {
        const existing = this.provisionalSourceMapTransforms.get(sourceMapId);
        if (existing) return existing;

        const dimensions = rasterSourceDimensions(image);
        if (!dimensions) return null;
        const worldWidth = width / this.viewport.zoom;
        const worldHeight = height / this.viewport.zoom;
        const scale = Math.min(
            (worldWidth * 0.9) / dimensions.width,
            (worldHeight * 0.9) / dimensions.height);
        if (!Number.isFinite(scale) || scale <= 0) return null;

        const transform: MapRegistrationTransform = {
            kind: "Affine",
            m11: scale,
            m12: 0,
            m13: this.viewport.center.x - ((dimensions.width * scale) / 2),
            m21: 0,
            m22: scale,
            m23: this.viewport.center.y - ((dimensions.height * scale) / 2),
            m31: 0,
            m32: 0
        };
        this.provisionalSourceMapTransforms.set(sourceMapId, transform);
        return transform;
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
        if (!world.tiling) return;
        ctx.save();
        ctx.strokeStyle = "rgba(52, 67, 55, .38)";
        ctx.lineWidth = 1;
        for (const cell of this.visibleWorldCells) {
            // Render only authoritative bounded polygon geometry. No inferred
            // coordinates or shape-specific prototype rendering is involved.
            if (cell.id.tilingId !== world.tiling.id || cell.polygon.length < 3) continue;
            traceTilePolygon(ctx, cell.polygon, point => this.toScreen(point, width, height));
            const terrain = this.terrainByCell.get(JSON.stringify(cell.id));
            const color = terrain ? mapCategoryColor(world.mapAppearance, terrain, "terrain") : null;
            if (color) {
                ctx.globalAlpha = 0.48;
                ctx.fillStyle = color;
                ctx.fill();
                ctx.globalAlpha = 1;
            }
            ctx.stroke();
        }
        ctx.restore();
    }

    private drawRegions(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        for (const feature of world.features.filter(feature => feature.kind === "Region" && feature.boundary?.length)) {
            const points = feature.boundary!.map(point => this.toScreen(point, width, height));
            ctx.beginPath();
            points.forEach((point, index) => index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
            ctx.closePath();
            ctx.fillStyle = mapCategoryColor(world.mapAppearance, feature.category, "feature")
                ?? (feature.category === "forest" ? "#4F764D" : "#766A4D");
            ctx.globalAlpha = 0.22;
            ctx.fill();
            ctx.globalAlpha = 1;
            if (this.discoveredSubjectIds.has(feature.id)) {
                ctx.strokeStyle = "#2f6b3b";
                ctx.lineWidth = 3;
                ctx.stroke();
            }
        }
    }

    /** Place labels after drawing geometry, reserving space for locations first. */
    private drawMapLabels(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        const occupied: Array<{ x: number; y: number; width: number; height: number }> = [];
        // Avoid quadratic work on pathological maps; geometry and feature data remain visible.
        const tryLabel = (name: string, category: string, x: number, y: number, centered = false): void => {
            if (occupied.length >= 600 || !Number.isFinite(x) || !Number.isFinite(y)
                || x < -20 || y < -20 || x > width + 20 || y > height + 20) return;
            this.drawFeatureLabel(ctx, name, x, y, occupied, width, height, centered,
                mapCategoryColor(world.mapAppearance, category, "label") ?? "#20251f");
        };
        for (const location of world.locations) {
            const point = this.toScreen(location.position, width, height);
            tryLabel(location.name, location.category, point.x + 11, point.y);
        }
        for (const feature of world.features) {
            if (feature.kind !== "Point" || !feature.position) continue;
            const point = this.toScreen(feature.position, width, height);
            tryLabel(feature.name, feature.category, point.x + 10, point.y);
        }
        for (const feature of world.features) {
            if (feature.kind !== "Line" || !feature.path?.length) continue;
            const position = midpointOnPath(feature.path.map(point => this.toScreen(point, width, height)));
            if (position) tryLabel(feature.name, feature.category, position.x, position.y - 11, true);
        }
        for (const feature of world.features) {
            if (feature.kind !== "Region" || !feature.boundary?.length) continue;
            const position = polygonLabelPoint(feature.boundary.map(point => this.toScreen(point, width, height)));
            if (position) tryLabel(feature.name, feature.category, position.x, position.y, true);
        }
    }

    private drawLinesAndPoints(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        for (const feature of world.features) {
            if (feature.kind === "Line" && feature.path?.length) {
                const points = feature.path.map(point => this.toScreen(point, width, height));
                ctx.beginPath();
                points.forEach((point, index) => index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
                ctx.strokeStyle = mapCategoryColor(world.mapAppearance, feature.category, "feature")
                    ?? (feature.category === "river" ? "#4479a1" : "#775f41");
                ctx.lineWidth = feature.category === "river" ? 4 : 3;
                ctx.stroke();
                if (this.discoveredSubjectIds.has(feature.id)) {
                    ctx.strokeStyle = "#2f6b3b";
                    ctx.lineWidth += 3;
                    ctx.stroke();
                }
                // Draw names in the shared label pass, after locations and geometry.
            } else if (feature.kind === "Point" && feature.position) {
                const point = this.toScreen(feature.position, width, height);
                ctx.beginPath();
                ctx.arc(point.x, point.y, 5, 0, Math.PI * 2);
                ctx.fillStyle = mapCategoryColor(world.mapAppearance, feature.category, "feature") ?? "#4b3d32";
                ctx.fill();
                if (this.discoveredSubjectIds.has(feature.id)) {
                    ctx.beginPath();
                    ctx.arc(point.x, point.y, 9, 0, Math.PI * 2);
                    ctx.strokeStyle = "#2f6b3b";
                    ctx.lineWidth = 2;
                    ctx.stroke();
                }
                // Draw names in the shared label pass, after locations and geometry.
            }
        }
    }

    private drawFeatureLabel(
        ctx: CanvasRenderingContext2D, name: string, anchorX: number, anchorY: number,
        occupied: Array<{ x: number; y: number; width: number; height: number }>,
        viewportWidth: number, viewportHeight: number, centered: boolean, color: string): void {
        const label = name.trim().slice(0, 72);
        if (!label) return;
        ctx.save();
        ctx.font = "600 12px system-ui, sans-serif";
        ctx.textAlign = "left";
        ctx.textBaseline = "middle";
        const textWidth = Math.min(240, ctx.measureText(label).width);
        const rectWidth = textWidth + 6;
        const rectHeight = 17;
        const x = centered ? anchorX - textWidth / 2 : anchorX;
        const candidates = [
            { x, y: anchorY - rectHeight / 2 },
            { x, y: anchorY - rectHeight * 1.65 },
            { x, y: anchorY + rectHeight * .65 },
            { x: anchorX - rectWidth - 12, y: anchorY - rectHeight / 2 }
        ];
        for (const box of candidates) {
            const bounds = { x: box.x - 3, y: box.y, width: rectWidth, height: rectHeight };
            if (bounds.x < 2 || bounds.y < 2 || bounds.x + bounds.width > viewportWidth - 2
                || bounds.y + bounds.height > viewportHeight - 2) continue;
            if (occupied.some(other =>
                bounds.x < other.x + other.width + 3 && bounds.x + bounds.width + 3 > other.x
                && bounds.y < other.y + other.height + 3 && bounds.y + bounds.height + 3 > other.y)) continue;
            occupied.push(bounds);
            ctx.lineWidth = 3;
            ctx.lineJoin = "round";
            const red = parseInt(color.slice(1, 3), 16);
            const green = parseInt(color.slice(3, 5), 16);
            const blue = parseInt(color.slice(5, 7), 16);
            const brightness = (red * .2126) + (green * .7152) + (blue * .0722);
            ctx.strokeStyle = brightness > 145 ? "rgba(0,0,0,.88)" : "rgba(255,255,255,.94)";
            ctx.fillStyle = color;
            const baseline = bounds.y + rectHeight / 2;
            ctx.strokeText(label, box.x, baseline, 240);
            ctx.fillText(label, box.x, baseline, 240);
            break;
        }
        ctx.restore();
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
            // Draw names in the shared label pass, after locations and geometry.
        }
    }

    private drawSelection(ctx: CanvasRenderingContext2D, world: DemoWorld, width: number, height: number): void {
        if (this.selectedWorldCell && world.tiling) {
            const cell = this.selectedWorldCell;
            if (cell.id.tilingId !== world.tiling.id || cell.polygon.length < 3) return;
            const points = cell.polygon.map(point => this.toScreen(point, width, height));
            ctx.beginPath();
            points.forEach((point, index) =>
                index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
            ctx.closePath();
            ctx.fillStyle = "rgba(210, 139, 39, .24)";
            ctx.fill();
            ctx.strokeStyle = "#9b5f12";
            ctx.lineWidth = 3;
            ctx.stroke();
            return;
        }
    }

    private drawExpedition(ctx: CanvasRenderingContext2D, world: DemoWorld | null, width: number, height: number): void {
        // Current cell and inspected cell are separate visual states. Use the
        // authoritative Phase 18 polygon, not an inferred hex-shaped marker.
        if (this.currentWorldCell && (!world || world.tiling?.id === this.currentWorldCell.id.tilingId)
            && this.currentWorldCell.polygon.length >= 3) {
            const corners = this.currentWorldCell.polygon.map(point => this.toScreen(point, width, height));
            ctx.save();
            ctx.beginPath();
            corners.forEach((point, index) =>
                index === 0 ? ctx.moveTo(point.x, point.y) : ctx.lineTo(point.x, point.y));
            ctx.closePath();
            ctx.fillStyle = "rgba(178, 38, 38, .12)";
            ctx.fill();
            ctx.strokeStyle = "rgba(178, 38, 38, .82)";
            ctx.lineWidth = 2;
            ctx.stroke();
            ctx.restore();
        }
        if (!this.expeditionPosition) return;
        const point = this.toScreen(this.expeditionPosition, width, height);
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

function rasterSourceDimensions(source: CanvasImageSource): { width: number; height: number } | null {
    if (typeof ImageBitmap !== "undefined" && source instanceof ImageBitmap) {
        return source.width > 0 && source.height > 0 ? { width: source.width, height: source.height } : null;
    }
    if (typeof HTMLImageElement !== "undefined" && source instanceof HTMLImageElement) {
        const width = source.naturalWidth || source.width;
        const height = source.naturalHeight || source.height;
        return width > 0 && height > 0 ? { width, height } : null;
    }
    if (typeof HTMLCanvasElement !== "undefined" && source instanceof HTMLCanvasElement) {
        return source.width > 0 && source.height > 0 ? { width: source.width, height: source.height } : null;
    }
    if (typeof OffscreenCanvas !== "undefined" && source instanceof OffscreenCanvas) {
        return source.width > 0 && source.height > 0 ? { width: source.width, height: source.height } : null;
    }
    return null;
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

function midpointOnPath(points: readonly WorldPoint[]): WorldPoint | null {
    if (points.length === 0) return null;
    if (points.length === 1) return points[0];
    const lengths = points.slice(1).map((p, index) => distance(p, points[index]));
    const halfway = lengths.reduce((a, b) => a + b, 0) / 2;
    let walked = 0;
    for (let i = 0; i < lengths.length; i++) {
        if (walked + lengths[i] >= halfway) {
            const t = lengths[i] === 0 ? 0 : (halfway - walked) / lengths[i];
            return {
                x: points[i].x + (points[i + 1].x - points[i].x) * t,
                y: points[i].y + (points[i + 1].y - points[i].y) * t
            };
        }
        walked += lengths[i];
    }
    return points[points.length - 1];
}

function polygonLabelPoint(points: readonly WorldPoint[]): WorldPoint | null {
    if (points.length < 3) return null;
    const center = {
        x: points.reduce((sum, p) => sum + p.x, 0) / points.length,
        y: points.reduce((sum, p) => sum + p.y, 0) / points.length
    };
    if (pointInPolygon(center, points)) return center;
    // A concave polygon's vertex-average can be outside its interior.
    // Sample interior positions instead of placing its label on a boundary vertex.
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const point of points) {
        minX = Math.min(minX, point.x);
        minY = Math.min(minY, point.y);
        maxX = Math.max(maxX, point.x);
        maxY = Math.max(maxY, point.y);
    }
    let best: WorldPoint | null = null;
    let clearance = -1;
    for (let row = 1; row <= 9; row++) {
        for (let col = 1; col <= 9; col++) {
            const point = {
                x: minX + (maxX - minX) * col / 10,
                y: minY + (maxY - minY) * row / 10
            };
            if (!pointInPolygon(point, points)) continue;
            let distanceToEdge = Infinity;
            for (let index = 0; index < points.length; index++)
                distanceToEdge = Math.min(distanceToEdge,
                    distanceToSegment(point, points[index], points[(index + 1) % points.length]));
            if (distanceToEdge > clearance) {
                best = point;
                clearance = distanceToEdge;
            }
        }
    }
    return best;
}
