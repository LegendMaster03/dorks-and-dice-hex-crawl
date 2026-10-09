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

export type SourceMapGridAnalysis = HexLatticeDetection & {
    tilingDsSymbol: string | null;
    apiVersion: "v1";
    capability: "map.periodic-tiling.detect";
    source: { width: number; height: number; mediaType: string };
    analysis: {
        width: number;
        height: number;
        scale: number;
        sourceResolutionVerified: boolean;
    };
};

export function rasterGridCanonicalResidualLimit(centerSpacingPixels: number): number {
    if (!Number.isFinite(centerSpacingPixels) || centerSpacingPixels <= 0) {
        throw new Error("Raster grid spacing must be finite and positive.");
    }
    return Math.min(2.5, Math.max(1.5, centerSpacingPixels * 0.025));
}

export function isCanonicalSourceFit(
    detection: HexLatticeDetection,
    sourceResolutionVerified: boolean): boolean {
    if (!sourceResolutionVerified || detection.status !== "detected" || !detection.fit) return false;
    return detection.fit.residualPixels <= rasterGridCanonicalResidualLimit(detection.fit.centerSpacingPixels);
}
