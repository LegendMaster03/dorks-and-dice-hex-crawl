export type AdjacencyPoint = {
    x: number;
    y: number;
};

export type SpatialAdjacencyInterface<TCell, TIntent> = {
    id: string;
    order: number;
    targetCell: TCell;
    targetLabel: string;
    intentValue: TIntent;
    label: string;
    boundarySegment: AdjacencyPoint[];
    anchor: AdjacencyPoint;
    traversable: boolean;
    disabledReason: string | null;
};

export type CurrentCellAdjacency<TCell, TIntent> = {
    currentCell: TCell;
    boundary: AdjacencyPoint[];
    center: AdjacencyPoint;
    adjacencies: SpatialAdjacencyInterface<TCell, TIntent>[];
    selectedAdjacencyId: string | null;
};

export function createCurrentCellAdjacency<TCell, TIntent>(
    currentCell: TCell,
    boundary: AdjacencyPoint[],
    adjacencies: SpatialAdjacencyInterface<TCell, TIntent>[],
    selectedIntent: TIntent | null = null): CurrentCellAdjacency<TCell, TIntent> {
    const selected = selectedIntent === null
        ? null
        : adjacencies.find(adjacency => Object.is(adjacency.intentValue, selectedIntent)) ?? null;
    return {
        currentCell,
        boundary: boundary.map(point => ({ ...point })),
        center: polygonCenter(boundary),
        adjacencies: [...adjacencies].sort((left, right) => left.order - right.order),
        selectedAdjacencyId: selected?.id ?? null
    };
}

export function adjacencyForCell<TCell, TIntent>(
    adjacency: CurrentCellAdjacency<TCell, TIntent>,
    target: TCell,
    sameCell: (left: TCell, right: TCell) => boolean): SpatialAdjacencyInterface<TCell, TIntent> | null {
    return adjacency.adjacencies.find(candidate => sameCell(candidate.targetCell, target)) ?? null;
}

export function adjacencyForIntent<TCell, TIntent>(
    adjacency: CurrentCellAdjacency<TCell, TIntent>,
    intent: TIntent): SpatialAdjacencyInterface<TCell, TIntent> | null {
    return adjacency.adjacencies.find(candidate => Object.is(candidate.intentValue, intent)) ?? null;
}

export function adjacencyFeedbackVector(
    center: AdjacencyPoint,
    anchor: AdjacencyPoint): AdjacencyPoint {
    const dx = anchor.x - center.x;
    const dy = anchor.y - center.y;
    const length = Math.hypot(dx, dy);
    if (length <= Number.EPSILON) return { x: 0, y: 0 };
    return { x: dx / length, y: dy / length };
}

export function screenRelativeAdjacencyLabel(anchor: AdjacencyPoint): string {
    const horizontal = anchor.x < 0.38 ? "left" : anchor.x > 0.62 ? "right" : "";
    const vertical = anchor.y < 0.38 ? "upper" : anchor.y > 0.62 ? "lower" : "";
    const position = [vertical, horizontal].filter(Boolean).join("-");
    return `${position || "center"} boundary`;
}

export function fitAdjacencyGeometry<TCell, TIntent>(
    boundary: readonly AdjacencyPoint[],
    adjacencies: readonly SpatialAdjacencyInterface<TCell, TIntent>[],
    extent = 0.68): {
        boundary: AdjacencyPoint[];
        adjacencies: SpatialAdjacencyInterface<TCell, TIntent>[];
    } {
    if (boundary.length === 0) return { boundary: [], adjacencies: [...adjacencies] };

    const minX = Math.min(...boundary.map(point => point.x));
    const maxX = Math.max(...boundary.map(point => point.x));
    const minY = Math.min(...boundary.map(point => point.y));
    const maxY = Math.max(...boundary.map(point => point.y));
    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;
    const width = Math.max(maxX - minX, Number.EPSILON);
    const height = Math.max(maxY - minY, Number.EPSILON);
    const scale = extent / Math.max(width, height);

    const normalize = (point: AdjacencyPoint): AdjacencyPoint => ({
        x: 0.5 + (point.x - centerX) * scale,
        y: 0.5 + (point.y - centerY) * scale
    });

    return {
        boundary: boundary.map(normalize),
        adjacencies: adjacencies.map(adjacency => {
            const boundarySegment = adjacency.boundarySegment.map(normalize);
            return {
                ...adjacency,
                boundarySegment,
                anchor: pointAverage(boundarySegment)
            };
        })
    };
}

function polygonCenter(polygon: readonly AdjacencyPoint[]): AdjacencyPoint {
    if (polygon.length === 0) return { x: 0.5, y: 0.5 };

    let twiceArea = 0;
    let x = 0;
    let y = 0;
    for (let index = 0; index < polygon.length; index += 1) {
        const current = polygon[index];
        const next = polygon[(index + 1) % polygon.length];
        const cross = current.x * next.y - next.x * current.y;
        twiceArea += cross;
        x += (current.x + next.x) * cross;
        y += (current.y + next.y) * cross;
    }
    if (Math.abs(twiceArea) > Number.EPSILON) {
        return { x: x / (3 * twiceArea), y: y / (3 * twiceArea) };
    }
    return pointAverage(polygon);
}

function pointAverage(points: readonly AdjacencyPoint[]): AdjacencyPoint {
    if (points.length === 0) return { x: 0.5, y: 0.5 };
    const sum = points.reduce(
        (current, point) => ({ x: current.x + point.x, y: current.y + point.y }),
        { x: 0, y: 0 });
    return { x: sum.x / points.length, y: sum.y / points.length };
}
