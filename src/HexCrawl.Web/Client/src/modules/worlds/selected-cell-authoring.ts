import type { EnvironmentAnnotation, EnvironmentFact, WorldEnvironment } from "../../environment-types";
import type { SpatialFeature, WorldPoint } from "../../types";

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

// Presentation-only intersection against a polygon returned by authoritative
// world geometry. This must never choose a unique cell or execute traversal.
export function featureIntersectsPolygon(feature: SpatialFeature, polygon: readonly WorldPoint[]): boolean {
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

export function pointInPolygon(point: WorldPoint, polygon: readonly WorldPoint[]): boolean {
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
