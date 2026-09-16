import type { MapRegistrationTransform, RegistrationControlPoint, WorldPoint } from "./types";

const epsilon = 1e-10;

export function solveAffine(controlPoints: RegistrationControlPoint[]): MapRegistrationTransform {
    if (controlPoints.length !== 3) throw new Error("Affine registration requires exactly three control-point pairs.");
    for (const pair of controlPoints) {
        finite(pair.sourcePixel, "source");
        finite(pair.worldPoint, "world");
    }
    distinct(controlPoints.map(pair => pair.sourcePixel), "source");
    distinct(controlPoints.map(pair => pair.worldPoint), "world");

    const source = controlPoints.map(pair => pair.sourcePixel);
    const determinant = det(source[0], source[1], source[2]);
    if (Math.abs(determinant) <= epsilon) throw new Error("Source control points are collinear.");

    const x = solveAxis(source, controlPoints.map(pair => pair.worldPoint.x), determinant);
    const y = solveAxis(source, controlPoints.map(pair => pair.worldPoint.y), determinant);
    const transform: MapRegistrationTransform = {
        kind: "Affine",
        m11: x.a,
        m12: x.b,
        m13: x.c,
        m21: y.a,
        m22: y.b,
        m23: y.c,
        m31: 0,
        m32: 0
    };
    const linearDeterminant = transform.m11 * transform.m22 - transform.m12 * transform.m21;
    if (!Number.isFinite(linearDeterminant) || Math.abs(linearDeterminant) <= epsilon) {
        throw new Error("Control points produce a degenerate affine transform.");
    }
    if (![transform.m11, transform.m12, transform.m13, transform.m21, transform.m22, transform.m23].every(Number.isFinite)) {
        throw new Error("Control points produce a non-finite affine transform.");
    }
    return transform;
}

export function transformPoint(transform: MapRegistrationTransform, source: WorldPoint): WorldPoint {
    const denominator = transform.m31 * source.x + transform.m32 * source.y + 1;
    if (Math.abs(denominator) <= 1e-12) throw new Error("Registration transform is undefined at this source point.");
    return {
        x: (transform.m11 * source.x + transform.m12 * source.y + transform.m13) / denominator,
        y: (transform.m21 * source.x + transform.m22 * source.y + transform.m23) / denominator
    };
}

function solveAxis(source: WorldPoint[], target: number[], determinant: number): { a: number; b: number; c: number } {
    const a = det(
        { x: target[0], y: source[0].y },
        { x: target[1], y: source[1].y },
        { x: target[2], y: source[2].y }) / determinant;
    const b = det(
        { x: source[0].x, y: target[0] },
        { x: source[1].x, y: target[1] },
        { x: source[2].x, y: target[2] }) / determinant;
    const c = (
        source[0].x * (source[1].y * target[2] - target[1] * source[2].y)
        - source[0].y * (source[1].x * target[2] - target[1] * source[2].x)
        + target[0] * (source[1].x * source[2].y - source[1].y * source[2].x)) / determinant;
    return { a, b, c };
}

function det(first: WorldPoint, second: WorldPoint, third: WorldPoint): number {
    return first.x * (second.y - third.y)
        - first.y * (second.x - third.x)
        + second.x * third.y - second.y * third.x;
}

function distinct(points: WorldPoint[], label: string): void {
    for (let i = 0; i < points.length; i++) {
        for (let j = i + 1; j < points.length; j++) {
            if (Math.abs(points[i].x - points[j].x) <= epsilon && Math.abs(points[i].y - points[j].y) <= epsilon) {
                throw new Error(`Duplicate ${label} control points are not valid for registration.`);
            }
        }
    }
}

function finite(point: WorldPoint, label: string): void {
    if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) throw new Error(`The ${label} control point must be finite.`);
}
