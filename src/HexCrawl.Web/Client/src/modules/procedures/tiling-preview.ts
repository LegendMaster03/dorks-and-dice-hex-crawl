import { traceTilePolygon } from "../../tile-polygon-canvas";
import type { TilingCatalogEntry } from "../../procedure-composer-types";

/** Render a bounded, noninteractive patch of the server-realized periodic geometry. */
export function tilingPreview(entry: TilingCatalogEntry | undefined, width = 74, height = 52): HTMLElement {
    const canvas = document.createElement("canvas");
    const ratio = Math.max(1, Math.min(2, Math.ceil(window.devicePixelRatio || 1)));
    canvas.width = width * ratio;
    canvas.height = height * ratio;
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;
    canvas.style.flexShrink = "0";
    canvas.setAttribute("role", "img");
    canvas.setAttribute("aria-label", entry?.displayName ?? "Unlisted tiling");
    const ctx = canvas.getContext("2d");
    const geometry = entry?.realization;
    if (!ctx || !geometry) return canvas;
    const polygons = Object.values(geometry.polygons).filter(p => p.length >= 3);
    if (!polygons.length) return canvas;
    const points = polygons.flat();
    const center = {
        x: points.reduce((sum, p) => sum + p.x, 0) / points.length,
        y: points.reduce((sum, p) => sum + p.y, 0) / points.length
    };
    const lengths = polygons.flatMap(p => p.map((v, i) => {
        const next = p[(i + 1) % p.length];
        return Math.hypot(next.x - v.x, next.y - v.y);
    })).filter(n => Number.isFinite(n) && n > 1e-8);
    if (!lengths.length) return canvas;
    const side = Math.min(...lengths);
    const scale = Math.min(22 / side, 220);
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    ctx.fillStyle = "#88888813";
    ctx.fillRect(0, 0, width, height);
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, width, height);
    ctx.clip();
    ctx.lineWidth = 1;
    ctx.strokeStyle = "#777";
    ctx.fillStyle = "#8b9ba530";
    const { translationU: u, translationV: v } = geometry;
    for (let a = -8; a <= 8; a++) {
        for (let b = -8; b <= 8; b++) {
            const offsetX = a * u.x + b * v.x - center.x;
            const offsetY = a * u.y + b * v.y - center.y;
            for (const polygon of polygons) {
                traceTilePolygon(ctx, polygon, p => ({
                    x: width / 2 + (p.x + offsetX) * scale,
                    y: height / 2 - (p.y + offsetY) * scale
                }));
                ctx.fill();
                ctx.stroke();
            }
        }
    }
    ctx.restore();
    return canvas;
}
