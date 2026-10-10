import assert from "node:assert/strict";
import test from "node:test";
import {
    buildRasterGridAlignmentProposal,
    physicalDistancePerDetectedHex,
    selectPhysicalDistancePerHex,
    transformPoint
} from "../.test-dist/raster-grid-alignment.js";

const grid = {
    id: "grid",
    orientation: "PointyTop",
    coordinateConvention: "AxialQr",
    origin: { x: 10, y: 20 },
    rotationDegrees: 0,
    hexRadiusWorldUnits: 2,
    neighborCenterDistance: {
        value: 12,
        unit: { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 }
    }
};

const fit = {
    orientation: "FlatTop",
    rotationDegrees: 4,
    centerSpacingPixels: 40,
    anchorPixel: { x: 100, y: 80 },
    confidence: 0.9,
    residualPixels: 0.5,
    supportCoverage: 0.7,
    orientationSupport: 0.8,
    translationScore: 0.7,
    competingTranslationScore: 0.2,
    phaseScore: 0.8
};

test("unplaced raster maps detected center to existing grid origin without inventing physical distance", () => {
    const proposal = buildRasterGridAlignmentProposal(
        fit,
        grid,
        null,
        { pixelWidth: 400, pixelHeight: 300 });

    assert.equal(proposal.mode, "PlaceAtGridOrigin");
    assert.equal(proposal.grid.orientation, "FlatTop");
    assert.equal(proposal.grid.rotationDegrees, 4);
    assert.deepEqual(transformPoint(proposal.alignment, fit.anchorPixel), grid.origin);
    assert.equal(proposal.grid.neighborCenterDistance.value, 12);
    assert.ok(Math.abs(proposal.grid.hexRadiusWorldUnits - 2) < 1e-12);
    assert.match(proposal.warnings[0], /Physical distance per hex was not detected/);
});

test("existing similarity registration stays fixed while grid adopts detected phase", () => {
    const angle = 10 * Math.PI / 180;
    const scale = 0.25;
    const alignment = {
        kind: "Affine",
        m11: scale * Math.cos(angle),
        m12: -scale * Math.sin(angle),
        m13: 7,
        m21: scale * Math.sin(angle),
        m22: scale * Math.cos(angle),
        m23: -4,
        m31: 0,
        m32: 0
    };

    const proposal = buildRasterGridAlignmentProposal(
        fit,
        grid,
        alignment,
        { pixelWidth: 400, pixelHeight: 300 });

    assert.equal(proposal.mode, "PreservePlacedRaster");
    assert.deepEqual(proposal.alignment, alignment);
    assert.ok(Math.abs(proposal.grid.rotationDegrees - 14) < 1e-9);
    assert.deepEqual(proposal.grid.origin, transformPoint(alignment, fit.anchorPixel));
    assert.ok(Math.abs(proposal.grid.hexRadiusWorldUnits - (40 * 0.25 / Math.sqrt(3))) < 1e-12);
});

test("sheared registration is repaired to similarity while preserving raster center", () => {
    const alignment = {
        kind: "Affine",
        m11: 0.3,
        m12: 0.06,
        m13: 5,
        m21: 0.02,
        m22: 0.22,
        m23: -3,
        m31: 0,
        m32: 0
    };
    const center = { x: 200, y: 150 };
    const before = transformPoint(alignment, center);

    const proposal = buildRasterGridAlignmentProposal(
        fit,
        grid,
        alignment,
        { pixelWidth: 400, pixelHeight: 300 });

    assert.equal(proposal.mode, "RepairPlacedRaster");
    const after = transformPoint(proposal.alignment, center);
    assert.ok(Math.hypot(before.x - after.x, before.y - after.y) < 1e-9);
    assert.ok(proposal.registrationDistortion > 0.015);
    assert.match(proposal.warnings[0], /non-similarity distortion/);
});

test("authoritative scale overrides saved scale while preserving raster center", () => {
    const alignment = {
        kind: "Affine",
        m11: 0.2,
        m12: 0,
        m13: 0,
        m21: 0,
        m22: 0.2,
        m23: 0,
        m31: 0,
        m32: 0
    };
    const center = { x: 200, y: 150 };
    const before = transformPoint(alignment, center);

    const proposal = buildRasterGridAlignmentProposal(
        fit,
        grid,
        alignment,
        {
            pixelWidth: 400,
            pixelHeight: 300,
            authoritativeWorldUnitsPerPixel: 0.3
        });

    assert.equal(proposal.mode, "RepairPlacedRaster");
    assert.equal(proposal.worldUnitsPerPixel, 0.3);
    assert.deepEqual(transformPoint(proposal.alignment, center), before);
    assert.ok(Math.abs(proposal.grid.hexRadiusWorldUnits - (40 * 0.3 / Math.sqrt(3))) < 1e-12);
});

test("verified project scale converts detected Humblewood spacing independently of lattice fit", () => {
    const distanceMiles = physicalDistancePerDetectedHex({
        centerSpacingPixels: 80,
        sourceUnitsPerProjectPixel: 30 / 220,
        rasterPixelsPerProjectPixel: 1,
        sourceMetersPerUnit: 1609.344,
        targetMetersPerUnit: 1609.344
    });

    assert.ok(Math.abs(distanceMiles - (80 * 30 / 220)) < 1e-12);
    assert.ok(Math.abs(distanceMiles - 10.909090909090908) < 1e-12);
});

test("physical-scale conversion accounts for proportional raster exports", () => {
    const distanceKilometers = physicalDistancePerDetectedHex({
        centerSpacingPixels: 160,
        sourceUnitsPerProjectPixel: 10 / 100,
        rasterPixelsPerProjectPixel: 2,
        sourceMetersPerUnit: 1000,
        targetMetersPerUnit: 1000
    });

    assert.equal(distanceKilometers, 8);
});

test("whole-mile candidate is selected only when it better reconciles independent scale evidence", () => {
    const selection = selectPhysicalDistancePerHex({
        directDistancePerHex: 10.9,
        crossCheckDistancePerHex: 11.07,
        considerWholeUnits: true
    });

    assert.equal(selection.distancePerHex, 11);
    assert.equal(selection.usedWholeUnitCandidate, true);
    assert.ok(selection.selectedWorstRelativeError < selection.directWorstRelativeError);
});

test("Humblewood scale stays continuous when a whole mile is not a better fit", () => {
    const direct = 80 * 30 / 220;
    const selection = selectPhysicalDistancePerHex({
        directDistancePerHex: direct,
        crossCheckDistancePerHex: direct,
        considerWholeUnits: true
    });

    assert.equal(selection.distancePerHex, direct);
    assert.equal(selection.usedWholeUnitCandidate, false);
});

test("a nearby whole mile is not selected when it worsens the Wonderdraft grid cross-check", () => {
    const selection = selectPhysicalDistancePerHex({
        directDistancePerHex: 10.967,
        crossCheckDistancePerHex: 10.909,
        considerWholeUnits: true
    });

    assert.equal(selection.distancePerHex, 10.967);
    assert.equal(selection.usedWholeUnitCandidate, false);
});

test("whole-unit candidates are not applied to non-mile distance units", () => {
    const selection = selectPhysicalDistancePerHex({
        directDistancePerHex: 10.9,
        crossCheckDistancePerHex: 11.07,
        considerWholeUnits: false
    });

    assert.equal(selection.distancePerHex, 10.9);
    assert.equal(selection.usedWholeUnitCandidate, false);
});
