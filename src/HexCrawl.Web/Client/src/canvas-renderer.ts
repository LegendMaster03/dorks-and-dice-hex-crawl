import { hexCorners, visibleHexBounds } from "./hex-math";
import type { DemoWorld, HexCoordinate, SpatialFeature, WorldPoint } from "./types";
import { Viewport } from "./viewport";

export class CanvasMapRenderer {
    public selectedHex: HexCoordinate | null = null;

    public constructor(
        private readonly canvas: HTMLCanvasElement,
        private readonly viewport: Viewport,
        private getWorld: () => DemoWorld | null) {}

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

        this.drawRegions(ctx, world, width, height);
        this.drawGrid(ctx, world, width, height);
        this.drawLinesAndPoints(ctx, world.features, width, height);
        this.drawLocations(ctx, world, width, height);
        this.drawSelection(ctx, world, width, height);
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
                    const center = this.toScreen({
                        x: screenCorners.reduce((sum, point) => sum + point.x, 0) / 6,
                        y: screenCorners.reduce((sum, point) => sum + point.y, 0) / 6
                    }, width, height, true);
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
            } else if (feature.kind === "Point" && feature.position) {
                const point = this.toScreen(feature.position, width, height);
                ctx.beginPath();
                ctx.arc(point.x, point.y, 5, 0, Math.PI * 2);
                ctx.fillStyle = "#4b3d32";
                ctx.fill();
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

    private toScreen(point: WorldPoint, width: number, height: number, alreadyScreen = false): WorldPoint {
        return alreadyScreen ? point : this.viewport.worldToScreen(point, width, height);
    }
}
