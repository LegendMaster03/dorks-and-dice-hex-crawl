import type { HexCoordinate, HexOrientation } from "../../types";

export type AdjacencyPoint = {
    x: number;
    y: number;
};

export type SpatialAdjacencyEdge<TCell, TDirection> = {
    id: string;
    order: number;
    targetCell: TCell;
    directionValue: TDirection;
    label: string;
    shortLabel: string;
    midpoint: AdjacencyPoint;
    traversable: boolean;
    disabledReason: string | null;
};

export type CurrentCellAdjacency<TCell, TDirection> = {
    currentCell: TCell;
    polygon: AdjacencyPoint[];
    edges: SpatialAdjacencyEdge<TCell, TDirection>[];
    selectedEdgeId: string | null;
};

export function createCurrentCellAdjacency<TCell, TDirection>(
    currentCell: TCell,
    polygon: AdjacencyPoint[],
    edges: SpatialAdjacencyEdge<TCell, TDirection>[],
    selectedDirection: TDirection | null = null): CurrentCellAdjacency<TCell, TDirection> {
    const selected = selectedDirection === null
        ? null
        : edges.find(edge => Object.is(edge.directionValue, selectedDirection)) ?? null;
    return {
        currentCell,
        polygon: [...polygon],
        edges: [...edges].sort((left, right) => left.order - right.order),
        selectedEdgeId: selected?.id ?? null
    };
}

export function adjacencyEdgeForCell<TCell, TDirection>(
    adjacency: CurrentCellAdjacency<TCell, TDirection>,
    target: TCell,
    sameCell: (left: TCell, right: TCell) => boolean): SpatialAdjacencyEdge<TCell, TDirection> | null {
    return adjacency.edges.find(edge => sameCell(edge.targetCell, target)) ?? null;
}

export function adjacencyEdgeForDirection<TCell, TDirection>(
    adjacency: CurrentCellAdjacency<TCell, TDirection>,
    direction: TDirection): SpatialAdjacencyEdge<TCell, TDirection> | null {
    return adjacency.edges.find(edge => Object.is(edge.directionValue, direction)) ?? null;
}

export function screenRelativeEdgeLabel(midpoint: AdjacencyPoint): string {
    const horizontal = midpoint.x < 0.38 ? "left" : midpoint.x > 0.62 ? "right" : "";
    const vertical = midpoint.y < 0.38 ? "upper" : midpoint.y > 0.62 ? "lower" : "";
    const position = [vertical, horizontal].filter(Boolean).join("-");
    return `${position || "center"} edge`;
}

export function outwardArrow(midpoint: AdjacencyPoint): string {
    const angle = Math.atan2(midpoint.y - 0.5, midpoint.x - 0.5);
    const octant = (Math.round(angle / (Math.PI / 4)) + 8) % 8;
    return ["→", "↘", "↓", "↙", "←", "↖", "↑", "↗"][octant];
}

const axialSteps = [
    { q: 1, r: 0 },
    { q: 1, r: -1 },
    { q: 0, r: -1 },
    { q: -1, r: 0 },
    { q: -1, r: 1 },
    { q: 0, r: 1 }
] as const;

const pointyPolygon: AdjacencyPoint[] = [
    { x: 0.5, y: 0 },
    { x: 1, y: 0.25 },
    { x: 1, y: 0.75 },
    { x: 0.5, y: 1 },
    { x: 0, y: 0.75 },
    { x: 0, y: 0.25 }
];

const flatPolygon: AdjacencyPoint[] = [
    { x: 0.25, y: 0 },
    { x: 0.75, y: 0 },
    { x: 1, y: 0.5 },
    { x: 0.75, y: 1 },
    { x: 0.25, y: 1 },
    { x: 0, y: 0.5 }
];

const pointyMidpoints: AdjacencyPoint[] = [
    { x: 1, y: 0.5 },
    { x: 0.75, y: 0.125 },
    { x: 0.25, y: 0.125 },
    { x: 0, y: 0.5 },
    { x: 0.25, y: 0.875 },
    { x: 0.75, y: 0.875 }
];

const flatMidpoints: AdjacencyPoint[] = [
    { x: 0.875, y: 0.75 },
    { x: 0.875, y: 0.25 },
    { x: 0.5, y: 0 },
    { x: 0.125, y: 0.25 },
    { x: 0.125, y: 0.75 },
    { x: 0.5, y: 1 }
];

export function currentHexAdjacency(
    currentCell: HexCoordinate,
    orientation: HexOrientation,
    selectedDirection: number | null,
    rotationDegrees = 0): CurrentCellAdjacency<HexCoordinate, number> {
    const baseMidpoints = orientation === "FlatTop" ? flatMidpoints : pointyMidpoints;
    const basePolygon = orientation === "FlatTop" ? flatPolygon : pointyPolygon;
    const geometry = transformGeometry(basePolygon, baseMidpoints, rotationDegrees);

    return createCurrentCellAdjacency(
        currentCell,
        geometry.polygon,
        axialSteps.map((step, direction) => {
            const targetCell = { q: currentCell.q + step.q, r: currentCell.r + step.r };
            const midpoint = geometry.midpoints[direction];
            return {
                id: `edge-${direction}`,
                order: direction,
                targetCell,
                directionValue: direction,
                label: screenRelativeEdgeLabel(midpoint),
                shortLabel: outwardArrow(midpoint),
                midpoint,
                traversable: true,
                disabledReason: null
            };
        }),
        selectedDirection);
}

export function sameHex(left: HexCoordinate, right: HexCoordinate): boolean {
    return left.q === right.q && left.r === right.r;
}

function transformGeometry(
    polygon: AdjacencyPoint[],
    midpoints: AdjacencyPoint[],
    rotationDegrees: number): { polygon: AdjacencyPoint[]; midpoints: AdjacencyPoint[] } {
    const rotatedPolygon = polygon.map(point => rotateAroundCenter(point, rotationDegrees));
    const rotatedMidpoints = midpoints.map(point => rotateAroundCenter(point, rotationDegrees));
    const minX = Math.min(...rotatedPolygon.map(point => point.x));
    const maxX = Math.max(...rotatedPolygon.map(point => point.x));
    const minY = Math.min(...rotatedPolygon.map(point => point.y));
    const maxY = Math.max(...rotatedPolygon.map(point => point.y));
    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;
    const width = Math.max(maxX - minX, Number.EPSILON);
    const height = Math.max(maxY - minY, Number.EPSILON);
    const fit = 0.68 / Math.max(width, height);

    const normalize = (point: AdjacencyPoint): AdjacencyPoint => ({
        x: 0.5 + (point.x - centerX) * fit,
        y: 0.5 + (point.y - centerY) * fit
    });

    return {
        polygon: rotatedPolygon.map(normalize),
        midpoints: rotatedMidpoints.map(normalize)
    };
}

function rotateAroundCenter(point: AdjacencyPoint, degrees: number): AdjacencyPoint {
    if (degrees === 0) return { ...point };
    const radians = degrees * Math.PI / 180;
    const cos = Math.cos(radians);
    const sin = Math.sin(radians);
    const x = point.x - 0.5;
    const y = point.y - 0.5;
    return {
        x: 0.5 + x * cos - y * sin,
        y: 0.5 + x * sin + y * cos
    };
}
