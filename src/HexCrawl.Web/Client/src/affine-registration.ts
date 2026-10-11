import type { MapRegistrationTransform, RegistrationControlPoint, WorldPoint } from "./types";

const epsilon = 1e-10;

export type AffineRegistrationHoldout = {
    /** Predicted world location of the independent image landmark. */
    predictedWorldPoint: WorldPoint;
    /** Discrepancy measured in original image pixels, regardless of world scale. */
    errorSourcePixels: number;
};

export const maxAffineHoldoutErrorSourcePixels = 12;

/**
 * Checks a landmark NOT used by the exact three-point registration fit.
 * Reports discrepancy back in original source pixels by inverting only the
 * affine linear part; world-space distance alone changes with map calibration.
 * This is an independent spot check, not a certificate for the full raster.
 */
export function evaluateAffineRegistrationHoldout(
    transform: MapRegistrationTransform,
    holdout: RegistrationControlPoint,
    fittedPoints: readonly RegistrationControlPoint[]): AffineRegistrationHoldout {
    if (fittedPoints.length !== 3 || fittedPoints.some(point =>
        !Number.isFinite(point.sourcePixel.x) || !Number.isFinite(point.sourcePixel.y)
        || Math.hypot(point.sourcePixel.x - holdout.sourcePixel.x,
            point.sourcePixel.y - holdout.sourcePixel.y) < 1)) {
        throw new Error("Independent landmark must differ from the three fitted image landmarks.");
    }
    if (transform.kind !== "Affine" || transform.m31 !== 0 || transform.m32 !== 0) {
        throw new Error("Independent landmark validation requires an affine registration.");
    }
    const coefficients = [transform.m11, transform.m12, transform.m13,
        transform.m21, transform.m22, transform.m23,
        holdout.sourcePixel.x, holdout.sourcePixel.y,
        holdout.worldPoint.x, holdout.worldPoint.y];
    if (!coefficients.every(Number.isFinite)) {
        throw new Error("Independent landmark coordinates and registration must be finite.");
    }
    const determinant = transform.m11 * transform.m22 - transform.m12 * transform.m21;
    if (!Number.isFinite(determinant) || Math.abs(determinant) <= 1e-10) {
        throw new Error("Independent landmark validation requires a nondegenerate registration.");
    }
    const predictedWorldPoint = {
        x: transform.m11 * holdout.sourcePixel.x + transform.m12 * holdout.sourcePixel.y + transform.m13,
        y: transform.m21 * holdout.sourcePixel.x + transform.m22 * holdout.sourcePixel.y + transform.m23
    };
    const errorX = holdout.worldPoint.x - predictedWorldPoint.x;
    const errorY = holdout.worldPoint.y - predictedWorldPoint.y;
    const pixelErrorX = (transform.m22 * errorX - transform.m12 * errorY) / determinant;
    const pixelErrorY = (transform.m11 * errorY - transform.m21 * errorX) / determinant;
    const errorSourcePixels = Math.hypot(pixelErrorX, pixelErrorY);
    if (!Number.isFinite(errorSourcePixels)) {
        throw new Error("Independent landmark discrepancy is outside the finite range.");
    }
    return { predictedWorldPoint, errorSourcePixels };
}

export function affineRegistrationSaveAllowed(
    pairCount: number,
    hasPreview: boolean,
    pendingSource: boolean,
    holdoutErrorSourcePixels: number | null): boolean {
    if (!hasPreview || pendingSource) return false;
    if (pairCount === 3) return true;
    return pairCount === 4 && holdoutErrorSourcePixels !== null
        && Number.isFinite(holdoutErrorSourcePixels)
        && holdoutErrorSourcePixels >= 0
        && holdoutErrorSourcePixels <= maxAffineHoldoutErrorSourcePixels;
}

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
