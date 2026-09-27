export type GrayscaleRaster = {
    width: number;
    height: number;
    pixels: Uint8Array;
};

export type HexLatticeOrientation = "PointyTop" | "FlatTop";

export type HexLatticeFit = {
    orientation: HexLatticeOrientation;
    rotationDegrees: number;
    centerSpacingPixels: number;
    anchorPixel: { x: number; y: number };
    confidence: number;
    residualPixels: number;
    supportCoverage: number;
    orientationSupport: number;
    translationScore: number;
    competingTranslationScore: number;
    linePeriodicityScore: number;
    phaseScore: number;
};

export type HexLatticeDetection = {
    status: "detected" | "inconclusive" | "gridless";
    fit: HexLatticeFit | null;
    reason: string;
};

export type HexLatticeDetectionOptions = {
    minimumSpacingPixels?: number;
    maximumSpacingPixels?: number;
    maximumEdgeSamples?: number;
    minimumConfidence?: number;
};

type EdgeSample = { x: number; y: number; normal: number };
type EdgeField = {
    width: number;
    height: number;
    strength: Float32Array;
    samples: EdgeSample[];
};
type HoughProfile = Float64Array;
type HoughCandidate = {
    baseNormalDegrees: number;
    carrierPitchPixels: number;
    score: number;
    combinedCorrelation: Float64Array;
};
type DistantPhaseResidual = {
    residualPixels: number;
    supportedRegions: number;
    totalRegions: number;
};

const PI = Math.PI;
const DEG = PI / 180;
const SQRT3 = Math.sqrt(3);
const HOUGH_ORIENTATION_TOLERANCE = 7 * DEG;
const HOUGH_MAX_LAG = 120;
const HOUGH_HARMONIC_WEIGHTS = [1, 0.8, 0.6, 0.4] as const;

export function detectHexLattice(
    raster: GrayscaleRaster,
    options: HexLatticeDetectionOptions = {}): HexLatticeDetection {
    validateRaster(raster);
    const minimumCenterSpacing = Math.max(8, Math.floor(options.minimumSpacingPixels ?? 12));
    const maximumCenterSpacing = Math.min(
        Math.floor(Math.min(raster.width, raster.height) / 2),
        Math.floor(options.maximumSpacingPixels ?? Number.POSITIVE_INFINITY));
    if (maximumCenterSpacing <= minimumCenterSpacing + 2) {
        return inconclusive("The raster is too small to establish repeated hex-grid spacing.");
    }

    const field = buildEdgeField(raster, options.maximumEdgeSamples ?? 90_000);
    if (field.samples.length < 500) {
        return gridless("The raster does not contain enough edge evidence for a hex lattice.");
    }

    const hough = fitHoughLattice(field, minimumCenterSpacing, maximumCenterSpacing);
    if (!hough.best || hough.best.score < 0.34) {
        return gridless(
            "Raster edges do not form three repeated line families with a stable hex-lattice period.");
    }

    const refined = refineHoughCandidate(
        field,
        hough.best,
        minimumCenterSpacing,
        maximumCenterSpacing);
    const model = classifyOrientation(refined.baseNormalDegrees * DEG);
    const refinedCarrierPitch = refinePeak(
        refined.combinedCorrelation,
        refined.carrierPitchPixels);
    const centerSpacing = refinedCarrierPitch * 2;
    const phase = fitPhase(
        field.strength,
        raster.width,
        raster.height,
        model.orientation,
        model.rotationDegrees,
        centerSpacing);
    const distantResidual = measureDistantPhaseResidual(
        field,
        refined.baseNormalDegrees,
        refinedCarrierPitch);

    const periodicityConfidence = clamp01((refined.score - 0.30) / 0.52);
    const uniqueness = clamp01(
        (refined.score - hough.competitorScore) / 0.18);
    const phaseConfidence = clamp01((phase.score - 0.10) / 0.32);
    const coverageConfidence = clamp01((phase.coverage - 0.10) / 0.60);
    const distantCoverageConfidence = clamp01(
        (distantResidual.supportedRegions - 6) / 15);
    const residualConfidence = clamp01(
        1 - distantResidual.residualPixels / Math.max(4, centerSpacing * 0.12));
    const confidence = clamp01(
        periodicityConfidence * 0.38
        + uniqueness * 0.14
        + phaseConfidence * 0.16
        + coverageConfidence * 0.12
        + distantCoverageConfidence * 0.10
        + residualConfidence * 0.10);

    const fit: HexLatticeFit = {
        orientation: model.orientation,
        rotationDegrees: model.rotationDegrees,
        centerSpacingPixels: centerSpacing,
        anchorPixel: phase.anchor,
        confidence,
        residualPixels: distantResidual.residualPixels,
        supportCoverage: phase.coverage,
        orientationSupport: refined.score,
        translationScore: refined.score,
        competingTranslationScore: hough.competitorScore,
        linePeriodicityScore: refined.score,
        phaseScore: phase.score
    };

    const threshold = options.minimumConfidence ?? 0.52;
    if (confidence < threshold || phase.coverage < 0.10 || distantResidual.supportedRegions < 6) {
        return {
            status: "inconclusive",
            fit,
            reason: confidence < threshold
                ? `A repeated hex lattice was found, but confidence ${confidence.toFixed(2)} is below the ${threshold.toFixed(2)} automatic-apply threshold.`
                : distantResidual.supportedRegions < 6
                    ? "A repeated local pattern was found, but too few distant image regions support one stable lattice."
                    : "A repeated hex lattice was found, but too little of the raster supports the fitted phase."
        };
    }

    return {
        status: "detected",
        fit,
        reason: `Detected a ${model.orientation === "PointyTop" ? "pointy-top" : "flat-top"} hex lattice with ${centerSpacing.toFixed(2)} px center spacing.`
    };
}

function validateRaster(raster: GrayscaleRaster): void {
    if (!Number.isInteger(raster.width) || !Number.isInteger(raster.height)
        || raster.width < 8 || raster.height < 8) {
        throw new Error("Raster dimensions must be integers of at least 8×8 pixels.");
    }
    if (raster.pixels.length !== raster.width * raster.height) {
        throw new Error("Grayscale raster pixel count does not match its dimensions.");
    }
}

function buildEdgeField(raster: GrayscaleRaster, maxSamples: number): EdgeField {
    const { width, height, pixels } = raster;
    const rawMagnitude = new Float32Array(width * height);
    const rawNormal = new Float32Array(width * height);
    const sampledMagnitudes: number[] = [];

    for (let y = 1; y < height - 1; y++) {
        for (let x = 1; x < width - 1; x++) {
            const i = y * width + x;
            const top = i - width;
            const bottom = i + width;
            const gx = -pixels[top - 1] + pixels[top + 1]
                - 2 * pixels[i - 1] + 2 * pixels[i + 1]
                - pixels[bottom - 1] + pixels[bottom + 1];
            const gy = -pixels[top - 1] - 2 * pixels[top] - pixels[top + 1]
                + pixels[bottom - 1] + 2 * pixels[bottom] + pixels[bottom + 1];
            const magnitude = Math.hypot(gx, gy);
            rawMagnitude[i] = magnitude;
            rawNormal[i] = normalizeHalfTurn(Math.atan2(gy, gx));
            if ((x & 1) === 0 && (y & 1) === 0 && magnitude > 0) {
                sampledMagnitudes.push(magnitude);
            }
        }
    }

    if (sampledMagnitudes.length === 0) {
        return { width, height, strength: new Float32Array(width * height), samples: [] };
    }

    sampledMagnitudes.sort((a, b) => a - b);
    // Baked map grids are often deliberately faint. A high edge threshold drops the
    // grid and leaves roads, labels, coastlines, and borders as the dominant signal.
    // Use broad gradient evidence, then let the global three-family Hough model reject
    // unrelated edges by geometry and periodicity.
    const threshold = Math.max(6, quantile(sampledMagnitudes, 0.55));
    const normalizer = Math.max(threshold, quantile(sampledMagnitudes, 0.90));
    const strength = new Float32Array(width * height);
    const candidates: EdgeSample[] = [];

    for (let y = 1; y < height - 1; y++) {
        for (let x = 1; x < width - 1; x++) {
            const i = y * width + x;
            const magnitude = rawMagnitude[i];
            strength[i] = Math.min(1, magnitude / normalizer);
            if (magnitude >= threshold) {
                candidates.push({ x, y, normal: rawNormal[i] });
            }
        }
    }

    if (candidates.length <= maxSamples) {
        return { width, height, strength, samples: candidates };
    }

    // Keep a deterministic, spatially distributed sample. Sorting by magnitude would
    // recreate the original failure mode by preferentially retaining map artwork.
    const samples: EdgeSample[] = [];
    const step = candidates.length / maxSamples;
    for (let index = 0; index < maxSamples; index++) {
        samples.push(candidates[Math.floor(index * step)]);
    }
    return { width, height, strength, samples };
}

function fitHoughLattice(
    field: EdgeField,
    minimumCenterSpacing: number,
    maximumCenterSpacing: number): {
    best: HoughCandidate | null;
    competitorScore: number;
} {
    const maxLag = Math.min(
        HOUGH_MAX_LAG,
        Math.floor(Math.min(field.width, field.height) / 3));
    const minimumPitch = Math.max(3, Math.floor(minimumCenterSpacing / 2));
    const maximumPitch = Math.min(
        Math.floor(maximumCenterSpacing / 2),
        Math.floor(maxLag / 2));
    if (maximumPitch <= minimumPitch + 1) {
        return { best: null, competitorScore: 0 };
    }

    const candidates: HoughCandidate[] = [];
    for (let degrees = 0; degrees < 60; degrees += 1) {
        const candidate = evaluateHoughOrientation(
            field,
            degrees,
            minimumPitch,
            maximumPitch,
            maxLag);
        if (candidate) candidates.push(candidate);
    }
    candidates.sort((a, b) => b.score - a.score);
    const best = candidates[0] ?? null;
    const competitor = best
        ? candidates.find(candidate => isDistinctHoughFit(best, candidate)) ?? null
        : null;
    return {
        best,
        competitorScore: competitor?.score ?? 0
    };
}

function isDistinctHoughFit(best: HoughCandidate, candidate: HoughCandidate): boolean {
    if (candidate === best) return false;
    const rawAngleDifference = Math.abs(best.baseNormalDegrees - candidate.baseNormalDegrees);
    const angleDifference = Math.min(rawAngleDifference, 60 - rawAngleDifference);
    const pitchDifference = Math.abs(best.carrierPitchPixels - candidate.carrierPitchPixels);
    return angleDifference >= 4
        || pitchDifference >= Math.max(2, best.carrierPitchPixels * 0.12);
}

function refineHoughCandidate(
    field: EdgeField,
    coarse: HoughCandidate,
    minimumCenterSpacing: number,
    maximumCenterSpacing: number): HoughCandidate {
    const maxLag = Math.min(
        HOUGH_MAX_LAG,
        Math.floor(Math.min(field.width, field.height) / 3));
    const minimumPitch = Math.max(3, Math.floor(minimumCenterSpacing / 2));
    const maximumPitch = Math.min(
        Math.floor(maximumCenterSpacing / 2),
        Math.floor(maxLag / 2));
    let best = coarse;
    for (let delta = -1; delta <= 1.0001; delta += 0.25) {
        const degrees = normalizePeriod(coarse.baseNormalDegrees + delta, 60);
        const candidate = evaluateHoughOrientation(
            field,
            degrees,
            minimumPitch,
            maximumPitch,
            maxLag);
        if (candidate && candidate.score > best.score) best = candidate;
    }
    return best;
}

function evaluateHoughOrientation(
    field: EdgeField,
    baseNormalDegrees: number,
    minimumPitch: number,
    maximumPitch: number,
    maxLag: number): HoughCandidate | null {
    const familyCurves: Float64Array[] = [];
    for (let family = 0; family < 3; family++) {
        const angle = (baseNormalDegrees * DEG) + (family * PI / 3);
        const profile = buildHoughProfile(field, angle);
        if (!profile) return null;
        familyCurves.push(normalizedAutocorrelation(profile, maxLag));
    }

    const combined = new Float64Array(maxLag + 1);
    for (let lag = 0; lag <= maxLag; lag++) {
        const familyScores = familyCurves.map(curve => curve[lag]).sort((a, b) => a - b);
        combined[lag] =
            (familyScores[1] * 0.55)
            + (familyScores[2] * 0.30)
            + (familyScores[0] * 0.15);
    }

    let bestPitch = 0;
    let bestScore = Number.NEGATIVE_INFINITY;
    for (let pitch = minimumPitch; pitch <= maximumPitch; pitch++) {
        const score = harmonicTrainScore(combined, pitch);
        if (score > bestScore) {
            bestScore = score;
            bestPitch = pitch;
        }
    }
    if (!Number.isFinite(bestScore) || bestPitch <= 0) return null;
    return {
        baseNormalDegrees,
        carrierPitchPixels: bestPitch,
        score: bestScore,
        combinedCorrelation: combined
    };
}

function buildHoughProfile(field: EdgeField, normal: number): HoughProfile | null {
    const diagonal = Math.ceil(Math.hypot(field.width, field.height));
    const profile = new Float64Array((diagonal * 2) + 5);
    const offset = diagonal + 2;
    const cos = Math.cos(normal);
    const sin = Math.sin(normal);
    let votes = 0;

    for (const sample of field.samples) {
        if (halfTurnDistance(sample.normal, normal) > HOUGH_ORIENTATION_TOLERANCE) continue;
        const rho = (sample.x * cos) + (sample.y * sin);
        const index = Math.round(rho) + offset;
        if (index < 0 || index >= profile.length) continue;
        profile[index] += 1;
        votes++;
    }
    if (votes < 120) return null;
    return smoothProfile(smoothProfile(profile));
}

function normalizedAutocorrelation(profile: HoughProfile, maxLag: number): Float64Array {
    const baseline = movingAverage(profile, 8);
    const residual = new Float64Array(profile.length);
    let sumSquares = 0;
    for (let index = 0; index < profile.length; index++) {
        const value = profile[index] - baseline[index];
        residual[index] = value;
        sumSquares += value * value;
    }
    const standardDeviation = Math.sqrt(sumSquares / Math.max(1, profile.length));
    const clip = Math.max(1e-9, standardDeviation * 3);
    for (let index = 0; index < residual.length; index++) {
        residual[index] = Math.max(-clip, Math.min(clip, residual[index]));
    }

    const result = new Float64Array(maxLag + 1);
    for (let lag = 3; lag <= maxLag; lag++) {
        let numerator = 0;
        let leftSquares = 0;
        let rightSquares = 0;
        for (let index = 0; index < residual.length - lag; index++) {
            const left = residual[index];
            const right = residual[index + lag];
            numerator += left * right;
            leftSquares += left * left;
            rightSquares += right * right;
        }
        const denominator = Math.sqrt(leftSquares * rightSquares);
        result[lag] = denominator > 1e-12 ? numerator / denominator : 0;
    }
    return result;
}

function harmonicTrainScore(curve: Float64Array, pitch: number): number {
    let weighted = 0;
    let weightTotal = 0;
    let first = 0;
    let maximum = 0;

    for (let harmonic = 1; harmonic <= HOUGH_HARMONIC_WEIGHTS.length; harmonic++) {
        const center = pitch * harmonic;
        const weight = HOUGH_HARMONIC_WEIGHTS[harmonic - 1];
        let value = 0;
        if (center < curve.length) {
            for (let offset = -1; offset <= 1; offset++) {
                const index = center + offset;
                if (index >= 3 && index < curve.length) {
                    value = Math.max(value, curve[index]);
                }
            }
        }
        value = Math.max(0, value);
        if (harmonic === 1) first = value;
        maximum = Math.max(maximum, value);
        weighted += value * weight;
        weightTotal += weight;
    }

    if (weightTotal <= 0 || maximum <= 0) return 0;
    const fundamentalSupport = clamp01((first + 0.10) / (maximum + 0.10));
    return (weighted / weightTotal) * (0.65 + (0.35 * fundamentalSupport));
}

function smoothProfile(values: Float64Array): Float64Array {
    const result = new Float64Array(values.length);
    for (let index = 0; index < values.length; index++) {
        const previous = values[Math.max(0, index - 1)];
        const current = values[index];
        const next = values[Math.min(values.length - 1, index + 1)];
        result[index] = (previous + (2 * current) + next) / 4;
    }
    return result;
}

function movingAverage(values: Float64Array, radius: number): Float64Array {
    const result = new Float64Array(values.length);
    const prefix = new Float64Array(values.length + 1);
    for (let index = 0; index < values.length; index++) {
        prefix[index + 1] = prefix[index] + values[index];
    }
    for (let index = 0; index < values.length; index++) {
        const start = Math.max(0, index - radius);
        const end = Math.min(values.length, index + radius + 1);
        result[index] = (prefix[end] - prefix[start]) / Math.max(1, end - start);
    }
    return result;
}

function measureDistantPhaseResidual(
    field: EdgeField,
    baseNormalDegrees: number,
    carrierPitchPixels: number): DistantPhaseResidual {
    const tilesPerAxis = 3;
    const totalRegions = 3 * tilesPerAxis * tilesPerAxis;
    let weightedSquaredResidual = 0;
    let totalWeight = 0;
    let supportedRegions = 0;

    for (let family = 0; family < 3; family++) {
        const normal = (baseNormalDegrees * DEG) + (family * PI / 3);
        const cos = Math.cos(normal);
        const sin = Math.sin(normal);
        const samples = field.samples.filter(
            sample => halfTurnDistance(sample.normal, normal) <= HOUGH_ORIENTATION_TOLERANCE);
        if (samples.length < 120) continue;

        let globalX = 0;
        let globalY = 0;
        for (const sample of samples) {
            const rho = (sample.x * cos) + (sample.y * sin);
            const phase = (2 * PI * rho) / carrierPitchPixels;
            globalX += Math.cos(phase);
            globalY += Math.sin(phase);
        }
        if (Math.hypot(globalX, globalY) < 1) continue;
        const globalPhase = Math.atan2(globalY, globalX);

        for (let tileY = 0; tileY < tilesPerAxis; tileY++) {
            for (let tileX = 0; tileX < tilesPerAxis; tileX++) {
                const minX = tileX * field.width / tilesPerAxis;
                const maxX = (tileX + 1) * field.width / tilesPerAxis;
                const minY = tileY * field.height / tilesPerAxis;
                const maxY = (tileY + 1) * field.height / tilesPerAxis;
                let localX = 0;
                let localY = 0;
                let count = 0;
                for (const sample of samples) {
                    if (sample.x < minX || sample.x >= maxX
                        || sample.y < minY || sample.y >= maxY) continue;
                    const rho = (sample.x * cos) + (sample.y * sin);
                    const phase = (2 * PI * rho) / carrierPitchPixels;
                    localX += Math.cos(phase);
                    localY += Math.sin(phase);
                    count++;
                }
                if (count < 20) continue;
                const coherence = Math.hypot(localX, localY) / count;
                if (coherence < 0.08) continue;

                const localPhase = Math.atan2(localY, localX);
                const delta = Math.atan2(
                    Math.sin(localPhase - globalPhase),
                    Math.cos(localPhase - globalPhase));
                const residual = Math.abs(delta) / (2 * PI) * carrierPitchPixels;
                const weight = Math.max(0.05, coherence) * Math.sqrt(count);
                weightedSquaredResidual += weight * residual * residual;
                totalWeight += weight;
                supportedRegions++;
            }
        }
    }

    return {
        residualPixels: totalWeight > 0
            ? Math.sqrt(weightedSquaredResidual / totalWeight)
            : carrierPitchPixels / 2,
        supportedRegions,
        totalRegions
    };
}

function classifyOrientation(baseNormal: number): { orientation: HexLatticeOrientation; rotationDegrees: number } {
    const degrees = normalizePeriod(baseNormal / DEG, 60);
    if (Math.min(degrees, 60 - degrees) <= 15) {
        return {
            orientation: "PointyTop",
            rotationDegrees: normalizeSigned(degrees <= 30 ? degrees : degrees - 60)
        };
    }
    return {
        orientation: "FlatTop",
        rotationDegrees: normalizeSigned(degrees - 30)
    };
}

function fitPhase(
    strength: Float32Array,
    width: number,
    height: number,
    orientation: HexLatticeOrientation,
    rotationDegrees: number,
    spacing: number): { anchor: { x: number; y: number }; score: number; coverage: number; residual: number } {
    const basisAngle = ((orientation === "PointyTop" ? 0 : 30) + rotationDegrees) * DEG;
    const u = { x: spacing * Math.cos(basisAngle), y: spacing * Math.sin(basisAngle) };
    const v = { x: spacing * Math.cos(basisAngle + PI / 3), y: spacing * Math.sin(basisAngle + PI / 3) };
    let best = { anchor: { x: 0, y: 0 }, score: -1, coverage: 0, residual: 4 };

    for (let ai = 0; ai < 12; ai++) {
        for (let bi = 0; bi < 12; bi++) {
            const anchor = {
                x: (ai / 12) * u.x + (bi / 12) * v.x,
                y: (ai / 12) * u.y + (bi / 12) * v.y
            };
            const result = scorePhase(
                strength,
                width,
                height,
                orientation,
                rotationDegrees,
                spacing,
                anchor,
                u,
                v);
            if (result.score > best.score) best = { anchor, ...result };
        }
    }
    return best;
}

function scorePhase(
    strength: Float32Array,
    width: number,
    height: number,
    orientation: HexLatticeOrientation,
    rotationDegrees: number,
    spacing: number,
    anchor: { x: number; y: number },
    u: { x: number; y: number },
    v: { x: number; y: number }): { score: number; coverage: number; residual: number } {
    const radius = spacing / SQRT3;
    const cornerStart = ((orientation === "PointyTop" ? -30 : 0) + rotationDegrees) * DEG;
    const reach = Math.ceil(Math.hypot(width, height) / spacing) + 4;
    let total = 0;
    let count = 0;
    let supported = 0;
    let residual = 0;
    let residualCount = 0;

    for (let i = -reach; i <= reach; i++) {
        for (let j = -reach; j <= reach; j++) {
            const cx = anchor.x + i * u.x + j * v.x;
            const cy = anchor.y + i * u.y + j * v.y;
            if (cx < -spacing || cy < -spacing || cx > width + spacing || cy > height + spacing) continue;
            const corners = Array.from({ length: 6 }, (_, index) => {
                const angle = cornerStart + index * PI / 3;
                return {
                    x: cx + radius * Math.cos(angle),
                    y: cy + radius * Math.sin(angle)
                };
            });
            for (let edge = 0; edge < 6; edge++) {
                const a = corners[edge];
                const b = corners[(edge + 1) % 6];
                for (const t of [0.25, 0.5, 0.75]) {
                    const x = a.x + (b.x - a.x) * t;
                    const y = a.y + (b.y - a.y) * t;
                    if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2) continue;
                    const value = bilinear(strength, width, height, x, y);
                    total += value;
                    count++;
                    if (value >= 0.22) supported++;
                    if ((edge & 1) === 0 && residualCount < 1200) {
                        const nx = -(b.y - a.y);
                        const ny = b.x - a.x;
                        const nlen = Math.hypot(nx, ny) || 1;
                        let bestOffset = 4;
                        let bestValue = value;
                        for (let offset = -4; offset <= 4; offset++) {
                            const shifted = bilinear(
                                strength,
                                width,
                                height,
                                x + (nx / nlen * offset),
                                y + (ny / nlen * offset));
                            if (shifted > bestValue) {
                                bestValue = shifted;
                                bestOffset = Math.abs(offset);
                            }
                        }
                        residual += bestValue >= 0.18 ? bestOffset : 4;
                        residualCount++;
                    }
                }
            }
        }
    }
    return {
        score: count ? total / count : 0,
        coverage: count ? supported / count : 0,
        residual: residualCount ? residual / residualCount : 4
    };
}

function refinePeak(scores: Float64Array, spacing: number): number {
    const rounded = Math.round(spacing);
    if (rounded <= 0 || rounded >= scores.length - 1) return spacing;
    const left = scores[rounded - 1];
    const center = scores[rounded];
    const right = scores[rounded + 1];
    const denominator = left - (2 * center) + right;
    if (Math.abs(denominator) < 1e-9) return spacing;
    return rounded + Math.max(-0.5, Math.min(0.5, 0.5 * (left - right) / denominator));
}

function bilinear(values: Float32Array, width: number, height: number, x: number, y: number): number {
    if (x < 0 || y < 0 || x >= width - 1 || y >= height - 1) return 0;
    const x0 = Math.floor(x);
    const y0 = Math.floor(y);
    const tx = x - x0;
    const ty = y - y0;
    const i = y0 * width + x0;
    const top = values[i] * (1 - tx) + values[i + 1] * tx;
    const bottom = values[i + width] * (1 - tx) + values[i + width + 1] * tx;
    return top * (1 - ty) + bottom * ty;
}

function halfTurnDistance(a: number, b: number): number {
    const delta = Math.abs(normalizeHalfTurn(a) - normalizeHalfTurn(b));
    return Math.min(delta, PI - delta);
}

function normalizeHalfTurn(value: number): number {
    let result = value % PI;
    if (result < 0) result += PI;
    return result;
}

function normalizePeriod(value: number, period: number): number {
    let result = value % period;
    if (result < 0) result += period;
    return result;
}

function normalizeSigned(value: number): number {
    let result = ((value + 180) % 360 + 360) % 360 - 180;
    if (Object.is(result, -0)) result = 0;
    return result;
}

function quantile(sorted: readonly number[], fraction: number): number {
    if (sorted.length === 0) return 0;
    const index = Math.min(
        sorted.length - 1,
        Math.max(0, Math.floor((sorted.length - 1) * fraction)));
    return sorted[index];
}

function clamp01(value: number): number {
    return Math.max(0, Math.min(1, value));
}

function inconclusive(reason: string): HexLatticeDetection {
    return { status: "inconclusive", fit: null, reason };
}

function gridless(reason: string): HexLatticeDetection {
    return { status: "gridless", fit: null, reason };
}
