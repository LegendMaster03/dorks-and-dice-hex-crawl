import assert from "node:assert/strict";
import test from "node:test";
import { isCanonicalSourceFit, rasterGridCanonicalResidualLimit } from "../.test-dist/hex-grid-analysis.js";

test("canonical residual policy remains a Hex Crawl apply policy", () => {
    assert.equal(rasterGridCanonicalResidualLimit(40), 1.5);
    assert.equal(rasterGridCanonicalResidualLimit(100), 2.5);
    assert.equal(rasterGridCanonicalResidualLimit(200), 2.5);
});

test("automatic apply requires a detected source-resolution fit", () => {
    const detection = {
        status: "detected",
        reason: "fixture",
        fit: {
            orientation: "FlatTop",
            rotationDegrees: 0,
            centerSpacingPixels: 80,
            anchorPixel: { x: 10, y: 20 },
            confidence: 0.98,
            residualPixels: 1.2,
            supportCoverage: 0.8,
            orientationSupport: 0.9,
            translationScore: 0.9,
            competingTranslationScore: 0.1,
            linePeriodicityScore: 0.9,
            phaseScore: 0.8
        }
    };
    assert.equal(isCanonicalSourceFit(detection, true), true);
    assert.equal(isCanonicalSourceFit(detection, false), false);
    assert.equal(isCanonicalSourceFit({ ...detection, status: "inconclusive" }, true), false);
});
