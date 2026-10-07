import type {
    EnvironmentAnnotation,
    EnvironmentFact,
    WorldEnvironment
} from "../../environment-types";
import { hexCorners, worldToHex } from "../../hex-math";
import type {
    HexCoordinate,
    Location,
    Overworld,
    SpatialFeature,
    WorldPoint
} from "../../types";

export const commonEnvironmentDimensions = [
    "terrain",
    "route",
    "weather",
    "visibility",
    "water",
    "elevation",
    "hazard"
] as const;

export type CellEnvironmentFact = {
    annotation: EnvironmentAnnotation;
    fact: EnvironmentFact;
    sourceLabel: string;
};

export function sameHex(left: HexCoordinate | null, right: HexCoordinate | null): boolean {
    return left !== null
        && right !== null
        && left.q === right.q
        && left.r === right.r;
}

export function locationsInCell(world: Overworld, cell: HexCoordinate): Location[] {
    return world.locations.filter(location => sameHex(worldToHex(world.grid, location.position), cell));
}

export function featuresIntersectingCell(world: Overworld, cell: HexCoordinate): SpatialFeature[] {
    const hex = hexCorners(world.grid, cell);
    return world.features.filter(feature => featureIntersectsPolygon(feature, hex));
}

export function hexEnvironmentFacts(
    environment: WorldEnvironment,
    cell: HexCoordinate): CellEnvironmentFact[] {
    return environment.annotations
        .filter(annotation => annotation.scope.kind === "Hex" && sameHex(annotation.scope.hex, cell))
        .flatMap(annotation => annotation.facts.map(fact => ({
            annotation,
            fact,
            sourceLabel: `Hex ${cell.q},${cell.r}`
        })));
}

export function applicableFeatureEnvironmentFacts(
    environment: WorldEnvironment,
    features: readonly SpatialFeature[]): CellEnvironmentFact[] {
    const byId = new Map(features.map(feature => [feature.id, feature]));
    return environment.annotations
        .filter(annotation => annotation.scope.kind === "SpatialFeature"
            && annotation.scope.featureId !== null
            && byId.has(annotation.scope.featureId))
        .flatMap(annotation => annotation.facts.map(fact => ({
            annotation,
            fact,
            sourceLabel: byId.get(annotation.scope.featureId!)!.name
        })));
}

export function terrainFactsForCell(
    environment: WorldEnvironment,
    cell: HexCoordinate): CellEnvironmentFact[] {
    return hexEnvironmentFacts(environment, cell)
        .filter(item => normalizeDimension(item.fact.dimension) === "terrain");
}

export function replaceHexTerrain(
    annotations: readonly EnvironmentAnnotation[],
    cell: HexCoordinate,
    terrain: string | null,
    createId: () => string = () => crypto.randomUUID()): EnvironmentAnnotation[] {
    const next = annotations
        .map(annotation => {
            if (annotation.scope.kind !== "Hex" || !sameHex(annotation.scope.hex, cell)) return annotation;
            return {
                ...annotation,
                facts: annotation.facts.filter(fact => normalizeDimension(fact.dimension) !== "terrain")
            };
        })
        .filter(annotation => annotation.facts.length > 0);

    const value = terrain?.trim() ?? "";
    if (!value) return next;

    const fact = tagFact("terrain", value, "world-editor:selected-cell", null, createId);
    const existingIndex = next.findIndex(annotation =>
        annotation.scope.kind === "Hex" && sameHex(annotation.scope.hex, cell));
    if (existingIndex >= 0) {
        const existing = next[existingIndex];
        next[existingIndex] = { ...existing, facts: [...existing.facts, fact] };
        return next;
    }

    return [...next, {
        id: createId(),
        scope: { kind: "Hex", hex: cell, featureId: null },
        facts: [fact]
    }];
}

export function addHexTagFact(
    annotations: readonly EnvironmentAnnotation[],
    cell: HexCoordinate,
    dimension: string,
    value: string,
    provenance: string | null = "world-editor:selected-cell",
    note: string | null = null,
    createId: () => string = () => crypto.randomUUID()): EnvironmentAnnotation[] {
    const fact = tagFact(dimension, value, provenance, note, createId);
    return [...annotations, {
        id: createId(),
        scope: { kind: "Hex", hex: cell, featureId: null },
        facts: [fact]
    }];
}

export function addFeatureTagFact(
    annotations: readonly EnvironmentAnnotation[],
    featureId: string,
    dimension: string,
    value: string,
    provenance: string | null = "world-editor:selected-feature",
    note: string | null = null,
    createId: () => string = () => crypto.randomUUID()): EnvironmentAnnotation[] {
    const fact = tagFact(dimension, value, provenance, note, createId);
    return [...annotations, {
        id: createId(),
        scope: { kind: "SpatialFeature", hex: null, featureId },
        facts: [fact]
    }];
}

export function addEnvironmentFact(
    annotations: readonly EnvironmentAnnotation[],
    scope: EnvironmentAnnotation["scope"],
    fact: EnvironmentFact,
    createId: () => string = () => crypto.randomUUID()): EnvironmentAnnotation[] {
    return [...annotations, {
        id: createId(),
        scope,
        facts: [fact]
    }];
}

export function removeEnvironmentFact(
    annotations: readonly EnvironmentAnnotation[],
    annotationId: string,
    factId: string): EnvironmentAnnotation[] {
    return annotations
        .map(annotation => annotation.id !== annotationId
            ? annotation
            : { ...annotation, facts: annotation.facts.filter(fact => fact.id !== factId) })
        .filter(annotation => annotation.facts.length > 0);
}

export function featureHasEnvironmentRules(
    environment: WorldEnvironment,
    featureId: string): boolean {
    return environment.annotations.some(annotation =>
        annotation.scope.kind === "SpatialFeature"
        && annotation.scope.featureId === featureId
        && annotation.facts.length > 0);
}

export function tagFact(
    dimension: string,
    value: string,
    provenance: string | null,
    note: string | null,
    createId: () => string = () => crypto.randomUUID()): EnvironmentFact {
    return {
        id: createId(),
        dimension: dimension.trim(),
        valueKind: "Tag",
        tag: value.trim(),
        measurement: null,
        provenance,
        note
    };
}

export function measurementFact(
    dimension: string,
    value: number,
    unit: string,
    provenance: string | null,
    note: string | null,
    createId: () => string = () => crypto.randomUUID()): EnvironmentFact {
    return {
        id: createId(),
        dimension: dimension.trim(),
        valueKind: "Measurement",
        tag: null,
        measurement: { value, unit: unit.trim() },
        provenance,
        note
    };
}

function normalizeDimension(value: string): string {
    return value.trim().toLowerCase();
}

function featureIntersectsPolygon(feature: SpatialFeature, polygon: readonly WorldPoint[]): boolean {
    if (feature.kind === "Point") {
        return feature.position !== null && pointInPolygon(feature.position, polygon);
    }
    if (feature.kind === "Line") {
        return polylineIntersectsPolygon(feature.path ?? [], polygon);
    }
    return polygonsIntersect(feature.boundary ?? [], polygon);
}

function polylineIntersectsPolygon(path: readonly WorldPoint[], polygon: readonly WorldPoint[]): boolean {
    if (path.length === 0) return false;
    if (path.some(point => pointInPolygon(point, polygon))) return true;
    for (let index = 1; index < path.length; index++) {
        if (segmentIntersectsPolygon(path[index - 1], path[index], polygon)) return true;
    }
    return false;
}

function polygonsIntersect(left: readonly WorldPoint[], right: readonly WorldPoint[]): boolean {
    if (left.length < 3 || right.length < 3) return false;
    if (left.some(point => pointInPolygon(point, right))
        || right.some(point => pointInPolygon(point, left))) return true;

    for (let leftIndex = 0; leftIndex < left.length; leftIndex++) {
        const a1 = left[leftIndex];
        const a2 = left[(leftIndex + 1) % left.length];
        for (let rightIndex = 0; rightIndex < right.length; rightIndex++) {
            const b1 = right[rightIndex];
            const b2 = right[(rightIndex + 1) % right.length];
            if (segmentsIntersect(a1, a2, b1, b2)) return true;
        }
    }
    return false;
}

function segmentIntersectsPolygon(
    start: WorldPoint,
    end: WorldPoint,
    polygon: readonly WorldPoint[]): boolean {
    for (let index = 0; index < polygon.length; index++) {
        if (segmentsIntersect(start, end, polygon[index], polygon[(index + 1) % polygon.length])) {
            return true;
        }
    }
    return false;
}

function pointInPolygon(point: WorldPoint, polygon: readonly WorldPoint[]): boolean {
    if (polygon.length < 3) return false;
    for (let index = 0; index < polygon.length; index++) {
        if (pointOnSegment(polygon[index], polygon[(index + 1) % polygon.length], point)) return true;
    }

    let inside = false;
    for (let index = 0; index < polygon.length; index++) {
        const previous = index === 0 ? polygon.length - 1 : index - 1;
        const currentPoint = polygon[index];
        const previousPoint = polygon[previous];
        const crosses = ((currentPoint.y > point.y) !== (previousPoint.y > point.y))
            && point.x < ((previousPoint.x - currentPoint.x)
                * (point.y - currentPoint.y)
                / (previousPoint.y - currentPoint.y))
                + currentPoint.x;
        if (crosses) inside = !inside;
    }
    return inside;
}

function pointOnSegment(start: WorldPoint, end: WorldPoint, point: WorldPoint): boolean {
    const epsilon = 1e-9;
    const cross = ((end.x - start.x) * (point.y - start.y))
        - ((end.y - start.y) * (point.x - start.x));
    return Math.abs(cross) < epsilon
        && point.x >= Math.min(start.x, end.x) - epsilon
        && point.x <= Math.max(start.x, end.x) + epsilon
        && point.y >= Math.min(start.y, end.y) - epsilon
        && point.y <= Math.max(start.y, end.y) + epsilon;
}

function segmentsIntersect(a: WorldPoint, b: WorldPoint, c: WorldPoint, d: WorldPoint): boolean {
    const cross = (p1: WorldPoint, p2: WorldPoint, p3: WorldPoint): number =>
        ((p2.x - p1.x) * (p3.y - p1.y)) - ((p2.y - p1.y) * (p3.x - p1.x));

    const abC = cross(a, b, c);
    const abD = cross(a, b, d);
    const cdA = cross(c, d, a);
    const cdB = cross(c, d, b);

    if (((abC > 0 && abD < 0) || (abC < 0 && abD > 0))
        && ((cdA > 0 && cdB < 0) || (cdA < 0 && cdB > 0))) {
        return true;
    }

    const epsilon = 1e-9;
    return (Math.abs(abC) < epsilon && pointOnSegment(a, b, c))
        || (Math.abs(abD) < epsilon && pointOnSegment(a, b, d))
        || (Math.abs(cdA) < epsilon && pointOnSegment(c, d, a))
        || (Math.abs(cdB) < epsilon && pointOnSegment(c, d, b));
}
