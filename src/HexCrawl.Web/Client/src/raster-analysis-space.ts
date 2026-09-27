import type { HexLatticeDetection } from "./grid-lattice-detector";

export const RasterGridAnalysisMaximumDimension = 2048;

export function rasterGridAnalysisScale(width: number, height: number): number {
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
        throw new Error("Raster analysis dimensions must be finite and positive.");
    }
    return Math.min(1, RasterGridAnalysisMaximumDimension / Math.max(width, height));
}

export function rasterGridCanonicalResidualLimit(centerSpacingPixels: number): number {
    if (!Number.isFinite(centerSpacingPixels) || centerSpacingPixels <= 0) {
        throw new Error("Raster grid spacing must be finite and positive.");
    }
    // Keep small lattices strict while allowing larger baked lines a modest absolute
    // tolerance. The cap prevents periodicity confidence from masking visibly doubled
    // edges on large maps.
    return Math.min(2.5, Math.max(1.5, centerSpacingPixels * 0.025));
}

export function mapDetectionToSourceImage(
    detection: HexLatticeDetection,
    analysisScale: number): HexLatticeDetection {
    if (!Number.isFinite(analysisScale) || analysisScale <= 0) {
        throw new Error("Raster analysis scale must be finite and positive.");
    }
    if (!detection.fit) return detection;

    const fit = detection.fit;
    return {
        ...detection,
        fit: {
            ...fit,
            centerSpacingPixels: fit.centerSpacingPixels / analysisScale,
            // The detector works in sample-center coordinates: array index (0,0)
            // denotes the center of the first analysis pixel. Canvas drawImage and
            // persisted raster transforms use image-edge coordinates. Move from the
            // sample center to image geometry before scaling back to the source raster.
            anchorPixel: {
                x: (fit.anchorPixel.x + 0.5) / analysisScale,
                y: (fit.anchorPixel.y + 0.5) / analysisScale
            },
            residualPixels: fit.residualPixels / analysisScale
        }
    };
}

export function isCanonicalSourceFit(
    detection: HexLatticeDetection,
    analysisScale: number): boolean {
    if (analysisScale !== 1 || detection.status !== "detected" || !detection.fit) return false;
    return detection.fit.residualPixels
        <= rasterGridCanonicalResidualLimit(detection.fit.centerSpacingPixels);
}
