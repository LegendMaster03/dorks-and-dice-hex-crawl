import assert from "node:assert/strict";
import test from "node:test";
import {
    isCanonicalSourceFit,
    mapDetectionToSourceImage,
    rasterGridAnalysisScale,
    rasterGridCanonicalResidualLimit
} from "../.test-dist/raster-analysis-space.js";

const fit = {
    orientation: "FlatTop",
    rotationDegrees: 0,
    centerSpacingPixels: 30,
    anchorPixel: { x: 2.75, y: 18.25 },
    confidence: 0.8,
    residualPixels: 1.75,
    supportCoverage: 0.8,
    orientationSupport: 0.8,
    translationScore: 0.8,
    competingTranslationScore: 0.2,
    linePeriodicityScore: 0.8,
    phaseScore: 0.8
};

test("common acceptance rasters are analyzed at source resolution", () => {
    assert.equal(rasterGridAnalysisScale(2048, 1536), 1);
    assert.equal(rasterGridAnalysisScale(2048, 1325), 1);
    assert.equal(rasterGridAnalysisScale(1403, 1026), 1);
    assert.equal(rasterGridAnalysisScale(4096, 2048), 0.5);
});

test("detector sample centers map to Canvas source-image coordinates", () => {
    const mapped = mapDetectionToSourceImage(
        { status: "detected", fit, reason: "ok" },
        0.375);

    assert.ok(mapped.fit);
    assert.equal(mapped.fit.centerSpacingPixels, 80);
    assert.equal(mapped.fit.anchorPixel.x, (2.75 + 0.5) / 0.375);
    assert.equal(mapped.fit.anchorPixel.y, (18.25 + 0.5) / 0.375);
    assert.equal(mapped.fit.residualPixels, 1.75 / 0.375);
});

test("canonical residual limits stay strict without rejecting larger known-good grids", () => {
    assert.equal(rasterGridCanonicalResidualLimit(42.76), 1.5);
    assert.equal(rasterGridCanonicalResidualLimit(80), 2);
    assert.equal(rasterGridCanonicalResidualLimit(132.67), 2.5);
});

test("automatic Apply requires source-resolution phase and a canonical distant residual", () => {
    const good = {
        status: "detected",
        fit: { ...fit, centerSpacingPixels: 80, residualPixels: 1.68 },
        reason: "ok"
    };
    const deployedHumblewoodFailure = {
        status: "detected",
        fit: { ...fit, centerSpacingPixels: 80, residualPixels: 4.66, confidence: 0.781 },
        reason: "periodic but visibly separated"
    };

    assert.equal(isCanonicalSourceFit(good, 1), true);
    assert.equal(isCanonicalSourceFit(good, 0.375), false);
    assert.equal(isCanonicalSourceFit(deployedHumblewoodFailure, 1), false);
});
