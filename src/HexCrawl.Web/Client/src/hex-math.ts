import type { GridDefinition, HexCoordinate, WorldPoint } from "./types";

const SQRT3 = Math.sqrt(3);

export function hexDistance(a: HexCoordinate, b: HexCoordinate): number {
    const aS = -a.q - a.r;
    const bS = -b.q - b.r;
    return (Math.abs(a.q - b.q) + Math.abs(a.r - b.r) + Math.abs(aS - bS)) / 2;
}

export function hexToWorld(grid: GridDefinition, hex: HexCoordinate): WorldPoint {
    const radius = grid.hexRadiusWorldUnits;
    const local = grid.orientation === "PointyTop"
        ? { x: radius * SQRT3 * (hex.q + hex.r / 2), y: radius * 1.5 * hex.r }
        : { x: radius * 1.5 * hex.q, y: radius * SQRT3 * (hex.r + hex.q / 2) };
    const rotated = rotate(local, grid.rotationDegrees);
    return { x: rotated.x + grid.origin.x, y: rotated.y + grid.origin.y };
}

export function worldToHex(grid: GridDefinition, point: WorldPoint): HexCoordinate {
    const local = rotate({ x: point.x - grid.origin.x, y: point.y - grid.origin.y }, -grid.rotationDegrees);
    const radius = grid.hexRadiusWorldUnits;

    const fractional = grid.orientation === "PointyTop"
        ? {
            q: ((SQRT3 / 3) * local.x - (1 / 3) * local.y) / radius,
            r: ((2 / 3) * local.y) / radius
        }
        : {
            q: ((2 / 3) * local.x) / radius,
            r: (-(1 / 3) * local.x + (SQRT3 / 3) * local.y) / radius
        };

    return cubeRound(fractional.q, fractional.r);
}

export function hexCorners(grid: GridDefinition, hex: HexCoordinate): WorldPoint[] {
    const center = hexToWorld(grid, hex);
    const startAngle = grid.orientation === "PointyTop" ? -30 : 0;
    return Array.from({ length: 6 }, (_, index) => {
        const angle = degreesToRadians(startAngle + index * 60 + grid.rotationDegrees);
        return {
            x: center.x + grid.hexRadiusWorldUnits * Math.cos(angle),
            y: center.y + grid.hexRadiusWorldUnits * Math.sin(angle)
        };
    });
}

export function visibleHexBounds(grid: GridDefinition, worldCorners: WorldPoint[], margin = 3): { minQ: number; maxQ: number; minR: number; maxR: number } {
    const hexes = worldCorners.map(point => worldToHex(grid, point));
    return {
        minQ: Math.min(...hexes.map(hex => hex.q)) - margin,
        maxQ: Math.max(...hexes.map(hex => hex.q)) + margin,
        minR: Math.min(...hexes.map(hex => hex.r)) - margin,
        maxR: Math.max(...hexes.map(hex => hex.r)) + margin
    };
}

function cubeRound(q: number, r: number): HexCoordinate {
    const s = -q - r;
    let rq = Math.round(q);
    let rr = Math.round(r);
    const rs = Math.round(s);
    const qDiff = Math.abs(rq - q);
    const rDiff = Math.abs(rr - r);
    const sDiff = Math.abs(rs - s);

    if (qDiff > rDiff && qDiff > sDiff) rq = -rr - rs;
    else if (rDiff > sDiff) rr = -rq - rs;

    return { q: rq, r: rr };
}

function rotate(point: WorldPoint, degrees: number): WorldPoint {
    if (degrees === 0) return point;
    const radians = degreesToRadians(degrees);
    const cos = Math.cos(radians);
    const sin = Math.sin(radians);
    return { x: point.x * cos - point.y * sin, y: point.x * sin + point.y * cos };
}

function degreesToRadians(degrees: number): number {
    return degrees * Math.PI / 180;
}
