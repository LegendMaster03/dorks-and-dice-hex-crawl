import type { HexLatticeFit } from "./grid-lattice-detector";
import type { GridDefinition, MapRegistrationTransform, WorldPoint } from "./types";

export type RasterGridAlignmentProposal = {
    grid: GridDefinition;
    alignment: MapRegistrationTransform;
    mode: "PlaceAtGridOrigin" | "PreservePlacedRaster" | "RepairPlacedRaster";
    worldUnitsPerPixel: number;
    registrationDistortion: number;
    warnings: string[];
};

export type RasterGridAlignmentOptions = {
    pixelWidth: number;
    pixelHeight: number;
    authoritativeWorldUnitsPerPixel?: number | null;
};

export type RasterPhysicalScaleInput = {
    centerSpacingPixels: number;
    sourceUnitsPerProjectPixel: number;
    rasterPixelsPerProjectPixel: number;
    sourceMetersPerUnit: number;
    targetMetersPerUnit: number;
};

export type PhysicalDistanceSelectionInput = {
    directDistancePerHex: number;
    crossCheckDistancePerHex?: number | null;
    considerWholeUnits?: boolean;
};

export type PhysicalDistanceSelection = {
    distancePerHex: number;
    directDistancePerHex: number;
    crossCheckDistancePerHex: number | null;
    usedWholeUnitCandidate: boolean;
    directWorstRelativeError: number;
    selectedWorstRelativeError: number;
};

type PhysicalDistanceCandidateScore = {
    worstRelativeError: number;
    rmsRelativeError: number;
};

const SQRT3 = Math.sqrt(3);
const EPSILON = 1e-10;

export function physicalDistancePerDetectedHex(input: RasterPhysicalScaleInput): number {
    const values = [
        input.centerSpacingPixels,
        input.sourceUnitsPerProjectPixel,
        input.rasterPixelsPerProjectPixel,
        input.sourceMetersPerUnit,
        input.targetMetersPerUnit
    ];
    if (values.some(value => !Number.isFinite(value) || value <= 0)) {
        throw new Error("Physical-scale inputs must all be finite and positive.");
    }

    const sourceUnitsPerRasterPixel =
        input.sourceUnitsPerProjectPixel / input.rasterPixelsPerProjectPixel;
    const metersPerHex = input.centerSpacingPixels
        * sourceUnitsPerRasterPixel
        * input.sourceMetersPerUnit;
    return metersPerHex / input.targetMetersPerUnit;
}

export function selectPhysicalDistancePerHex(
    input: PhysicalDistanceSelectionInput): PhysicalDistanceSelection {
    const direct = input.directDistancePerHex;
    if (!Number.isFinite(direct) || direct <= 0) {
        throw new Error("Direct physical distance per hex must be finite and positive.");
    }

    const crossCheck = input.crossCheckDistancePerHex ?? null;
    if (crossCheck != null && (!Number.isFinite(crossCheck) || crossCheck <= 0)) {
        throw new Error("Cross-check physical distance per hex must be finite and positive when supplied.");
    }

    const evidence = crossCheck == null ? [direct] : [direct, crossCheck];
    const directScore = scorePhysicalDistanceCandidate(direct, evidence);
    let bestDistance = direct;
    let bestScore = directScore;

    if (input.considerWholeUnits && crossCheck != null) {
        const lower = Math.max(1, Math.floor(Math.min(direct, crossCheck)) - 1);
        const upper = Math.ceil(Math.max(direct, crossCheck)) + 1;
        for (let candidate = lower; candidate <= upper; candidate++) {
            if (Math.abs(candidate - direct) <= 1e-12) continue;
            const score = scorePhysicalDistanceCandidate(candidate, evidence);
            if (isBetterPhysicalDistanceScore(score, bestScore)) {
                bestDistance = candidate;
                bestScore = score;
            }
        }
    }

    return {
        distancePerHex: bestDistance,
        directDistancePerHex: direct,
        crossCheckDistancePerHex: crossCheck,
        usedWholeUnitCandidate: Math.abs(bestDistance - direct) > 1e-12,
        directWorstRelativeError: directScore.worstRelativeError,
        selectedWorstRelativeError: bestScore.worstRelativeError
    };
}

function scorePhysicalDistanceCandidate(
    candidate: number,
    evidence: readonly number[]): PhysicalDistanceCandidateScore {
    const errors = evidence.map(value => Math.abs(candidate - value) / value);
    const worstRelativeError = Math.max(...errors);
    const rmsRelativeError = Math.sqrt(
        errors.reduce((sum, value) => sum + value * value, 0) / errors.length);
    return { worstRelativeError, rmsRelativeError };
}

function isBetterPhysicalDistanceScore(
    candidate: PhysicalDistanceCandidateScore,
    current: PhysicalDistanceCandidateScore): boolean {
    if (candidate.worstRelativeError < current.worstRelativeError - 1e-12) return true;
    if (candidate.worstRelativeError > current.worstRelativeError + 1e-12) return false;
    return candidate.rmsRelativeError < current.rmsRelativeError - 1e-12;
}

export function buildRasterGridAlignmentProposal(
    fit: HexLatticeFit,
    currentGrid: GridDefinition,
    currentAlignment: MapRegistrationTransform | null,
    options: RasterGridAlignmentOptions): RasterGridAlignmentProposal {
    validateFit(fit);
    validateGrid(currentGrid);
    validateDimensions(options.pixelWidth, options.pixelHeight);

    const authoritativeScale = options.authoritativeWorldUnitsPerPixel;
    if (authoritativeScale != null
        && (!Number.isFinite(authoritativeScale) || authoritativeScale <= 0)) {
        throw new Error("Authoritative world-units-per-pixel scale must be finite and positive.");
    }

    if (!currentAlignment) {
        const scale = authoritativeScale
            ?? ((currentGrid.hexRadiusWorldUnits * SQRT3) / fit.centerSpacingPixels);
        const alignment = similarityTransform(
            scale,
            0,
            fit.anchorPixel,
            currentGrid.origin);
        return {
            grid: {
                ...currentGrid,
                orientation: fit.orientation,
                rotationDegrees: normalizeSignedDegrees(fit.rotationDegrees),
                origin: { ...currentGrid.origin },
                hexRadiusWorldUnits: (fit.centerSpacingPixels * scale) / SQRT3
            },
            alignment,
            mode: "PlaceAtGridOrigin",
            worldUnitsPerPixel: scale,
            registrationDistortion: 0,
            warnings: authoritativeScale == null
                ? ["Physical distance per hex was not detected; the existing world distance is retained."]
                : []
        };
    }

    if (currentAlignment.kind !== "Affine") {
        throw new Error("Automatic baked-grid repair currently requires an affine raster registration.");
    }

    const similarity = decomposeAffine(currentAlignment);
    const mapRotation = similarity.rotationDegrees;
    const scale = authoritativeScale ?? similarity.scale;
    const centerPixel = { x: options.pixelWidth / 2, y: options.pixelHeight / 2 };
    const centerWorld = transformPoint(currentAlignment, centerPixel);
    const repairedAlignment = similarityTransform(scale, mapRotation, centerPixel, centerWorld);
    const anchorWorld = transformPoint(repairedAlignment, fit.anchorPixel);
    const exactSimilarity = similarity.distortion <= 0.015 && authoritativeScale == null;
    const alignment = exactSimilarity ? currentAlignment : repairedAlignment;
    const effectiveAnchor = exactSimilarity
        ? transformPoint(currentAlignment, fit.anchorPixel)
        : anchorWorld;
    const effectiveScale = exactSimilarity ? similarity.scale : scale;
    const warnings: string[] = [];
    if (similarity.distortion > 0.015) {
        warnings.push(
            `The saved raster registration has ${(similarity.distortion * 100).toFixed(1)}% non-similarity distortion. `
            + "The preview removes shear/nonuniform scale while preserving the raster center position.");
    }
    if (authoritativeScale == null) {
        warnings.push("Physical distance per hex was not detected; the existing world distance is retained.");
    } else if (Math.abs(authoritativeScale - similarity.scale) / similarity.scale > 0.01) {
        warnings.push("The supplied physical scale changes the raster scale while preserving its center position.");
    }

    return {
        grid: {
            ...currentGrid,
            orientation: fit.orientation,
            rotationDegrees: normalizeSignedDegrees(fit.rotationDegrees + mapRotation),
            origin: effectiveAnchor,
            hexRadiusWorldUnits: (fit.centerSpacingPixels * effectiveScale) / SQRT3
        },
        alignment,
        mode: exactSimilarity ? "PreservePlacedRaster" : "RepairPlacedRaster",
        worldUnitsPerPixel: effectiveScale,
        registrationDistortion: similarity.distortion,
        warnings
    };
}

export function transformPoint(transform: MapRegistrationTransform, point: WorldPoint): WorldPoint {
    const denominator = (transform.m31 * point.x) + (transform.m32 * point.y) + 1;
    if (!Number.isFinite(denominator) || Math.abs(denominator) <= EPSILON) {
        throw new Error("Raster registration is undefined at the requested source point.");
    }
    return {
        x: ((transform.m11 * point.x) + (transform.m12 * point.y) + transform.m13) / denominator,
        y: ((transform.m21 * point.x) + (transform.m22 * point.y) + transform.m23) / denominator
    };
}

function decomposeAffine(transform: MapRegistrationTransform): {
    scale: number;
    rotationDegrees: number;
    distortion: number;
} {
    const values = [
        transform.m11, transform.m12, transform.m13,
        transform.m21, transform.m22, transform.m23
    ];
    if (values.some(value => !Number.isFinite(value))) {
        throw new Error("Saved raster registration contains a non-finite value.");
    }
    const determinant = (transform.m11 * transform.m22) - (transform.m12 * transform.m21);
    if (!Number.isFinite(determinant) || determinant <= EPSILON) {
        throw new Error("Saved raster registration is degenerate or reflected and can not seed automatic grid repair.");
    }

    const scale = Math.sqrt(determinant);
    const rotation = Math.atan2(
        transform.m21 - transform.m12,
        transform.m11 + transform.m22);
    const cos = Math.cos(rotation);
    const sin = Math.sin(rotation);
    const ideal = [scale * cos, -scale * sin, scale * sin, scale * cos];
    const actual = [transform.m11, transform.m12, transform.m21, transform.m22];
    const difference = Math.hypot(
        actual[0] - ideal[0], actual[1] - ideal[1],
        actual[2] - ideal[2], actual[3] - ideal[3]);
    const magnitude = Math.max(EPSILON, Math.hypot(...actual));
    return {
        scale,
        rotationDegrees: rotation * 180 / Math.PI,
        distortion: difference / magnitude
    };
}

function similarityTransform(
    scale: number,
    rotationDegrees: number,
    sourceAnchor: WorldPoint,
    worldAnchor: WorldPoint): MapRegistrationTransform {
    if (!Number.isFinite(scale) || scale <= 0) throw new Error("Raster scale must be finite and positive.");
    const radians = rotationDegrees * Math.PI / 180;
    const cos = Math.cos(radians);
    const sin = Math.sin(radians);
    const m11 = scale * cos;
    const m12 = -scale * sin;
    const m21 = scale * sin;
    const m22 = scale * cos;
    return {
        kind: "Affine",
        m11,
        m12,
        m13: worldAnchor.x - ((m11 * sourceAnchor.x) + (m12 * sourceAnchor.y)),
        m21,
        m22,
        m23: worldAnchor.y - ((m21 * sourceAnchor.x) + (m22 * sourceAnchor.y)),
        m31: 0,
        m32: 0
    };
}

function validateFit(fit: HexLatticeFit): void {
    if (!Number.isFinite(fit.centerSpacingPixels) || fit.centerSpacingPixels <= 0
        || !Number.isFinite(fit.rotationDegrees)
        || !Number.isFinite(fit.anchorPixel.x) || !Number.isFinite(fit.anchorPixel.y)) {
        throw new Error("Detected raster lattice contains invalid geometry.");
    }
}

function validateGrid(grid: GridDefinition): void {
    if (!Number.isFinite(grid.hexRadiusWorldUnits) || grid.hexRadiusWorldUnits <= 0
        || !Number.isFinite(grid.origin.x) || !Number.isFinite(grid.origin.y)) {
        throw new Error("Current world grid contains invalid geometry.");
    }
}

function validateDimensions(width: number, height: number): void {
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
        throw new Error("Raster dimensions must be positive.");
    }
}

function normalizeSignedDegrees(value: number): number {
    let result = ((value + 180) % 360 + 360) % 360 - 180;
    if (Object.is(result, -0)) result = 0;
    return result;
}
