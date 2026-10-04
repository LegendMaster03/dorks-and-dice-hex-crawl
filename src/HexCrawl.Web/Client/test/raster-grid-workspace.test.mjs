import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceRoot = path.resolve("src");

test("reference-map workspace exposes server-backed automatic baked-grid detection before manual registration", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");
    const api = fs.readFileSync(path.join(sourceRoot, "api.ts"), "utf8");
    const renderer = fs.readFileSync(path.join(sourceRoot, "canvas-renderer.ts"), "utf8");

    assert.match(workspace, /SourceMapGridAlignmentController/);
    assert.match(workspace, /Detect \/ repair hex grid/);
    assert.match(workspace, /Detect and preview/);
    assert.match(workspace, /Advanced registration/);
    assert.match(controller, /analyzeSourceMapGrid/);
    assert.doesNotMatch(controller, /detectHexLattice/);
    assert.doesNotMatch(controller, /createImageBitmap/);
    assert.doesNotMatch(controller, /getImageData/);
    assert.match(api, /source-maps\/\$\{encodeURIComponent\(sourceMapId\)\}\/grid-analysis/);
    assert.match(controller, /buildRasterGridAlignmentProposal/);
    assert.match(controller, /detection\.status !== "detected"/);
    assert.match(controller, /gridPreview = proposal\.grid/);
    assert.match(controller, /registrationPreview =/);
    assert.match(controller, /grid-alignment/);
    assert.match(controller, /Automatic analysis unavailable/);
    assert.match(controller, /Advanced registration remains available/);
    assert.match(renderer, /public gridPreview: GridDefinition \| null = null/);
    assert.match(renderer, /const grid = this\.gridPreview \?\? world\.grid/);
});

test("unplaced baked-grid Wonderdraft import automatically starts a non-saving detection preview", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /public async beginAndPreview/);
    assert.match(controller, /this\.begin\(sourceMap\)/);
    assert.match(controller, /await this\.detectAndPreview\(\)/);
    assert.match(workspace, /importedSourceMap\?\.containsBakedGrid && !importedSourceMap\.alignment/);
    assert.match(workspace, /await this\.gridAlignmentController\.beginAndPreview\(importedSourceMap\)/);
    assert.doesNotMatch(workspace, /containsBakedGrid && importedSourceMap\.alignment[^\n]*beginAndPreview/);
});

test("grid detection requires Surveyor source-resolution verification before automatic Apply", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /analyzed\.analysis\.sourceResolutionVerified/);
    assert.match(controller, /source-resolution phase verified/);
    assert.match(controller, /downscaled phase only/);
    assert.match(controller, /Source-resolution phase verification is required before automatic Apply|source-resolution phase verification is required before automatic Apply/i);
    assert.match(controller, /isCanonicalSourceFit/);
    assert.match(controller, /rasterGridCanonicalResidualLimit/);
    assert.doesNotMatch(controller, /rasterGridAnalysisScale/);
    assert.doesNotMatch(controller, /mapDetectionToSourceImage/);
    assert.doesNotMatch(controller, /AnalysisMaximumDimension = 768/);
});

test("Wonderdraft grid metadata and scale-bar metadata remain independent cross-checks", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /gridMetadata: Record<string, string>/);
    assert.match(controller, /metadataValue\(metadata, "grid\.size"\)/);
    assert.match(controller, /grid\.size=/);
    assert.match(controller, /grid\.type not exposed/);
    assert.match(controller, /raster detector/);
    assert.match(controller, /difference/);
    assert.match(controller, /raster geometry is not overridden/);
    assert.match(controller, /scale-bar metadata/);
    assert.match(controller, /physicalScale\.unitsPerPixel \/ context\.uniformScale/);
    assert.match(controller, /selectPhysicalDistancePerHex/);
    assert.match(controller, /considerWholeUnits: grid\.neighborCenterDistance\.unit\.kind === "Mile"/);
    assert.match(controller, /usedWholeUnitCandidate/);
});

test("preview reports and gates final worst distant-region behavior", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /worst distant residual/);
    assert.match(controller, /fit\.residualPixels/);
    assert.match(controller, /final rigid overlay misses at least one distant region/);
    assert.match(controller, /automatic Apply requires at most/);
});

test("verified physical-scale conflicts require explicit confirmation before apply", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /PhysicalScaleConfirmationTolerance = 0\.01/);
    assert.match(controller, /detectPhysicalScaleChange\(world\.grid, proposal\.grid\)/);
    assert.match(controller, /Apply requires explicit confirmation/);
    assert.match(controller, /window\.confirm\(/);
    assert.match(controller, /physical-scale change was not confirmed/);
});
