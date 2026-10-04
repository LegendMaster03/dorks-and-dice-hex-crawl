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
    assert.match(workspace, /Re-run detection/);
    assert.match(workspace, /Advanced registration/);
    assert.match(controller, /analyzeSourceMapGrid/);
    assert.doesNotMatch(controller, /detectHexLattice/);
    assert.doesNotMatch(controller, /createImageBitmap/);
    assert.doesNotMatch(controller, /getImageData/);
    assert.match(api, /source-maps\/\$\{encodeURIComponent\(sourceMapId\)\}\/grid-analysis/);
    assert.match(controller, /buildRasterGridAlignmentProposal/);
    assert.match(controller, /this\.detection\.status !== "detected"/);
    assert.match(controller, /gridPreview = proposal\.grid/);
    assert.match(controller, /registrationPreview =/);
    assert.match(controller, /grid-alignment/);
    assert.match(controller, /Automatic analysis unavailable/);
    assert.match(controller, /Advanced registration remains available/);
    assert.match(renderer, /public gridPreview: GridDefinition \| null = null/);
    assert.match(renderer, /const grid = this\.gridPreview \?\? world\.grid/);
});

test("explicit detect controls run detection immediately instead of only opening the panel", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, /await this\.gridAlignmentController\.beginAndPreview\(this\.selected\)/);
    assert.match(workspace, /this\.gridAlignmentController\.beginAndPreview\(map\)/);
    assert.doesNotMatch(workspace, /this\.gridAlignmentController\.begin\(this\.selected\)/);
});

test("standalone baked-grid upload automatically selects the new raster and starts preview", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, /const existingSourceMapIds = new Set\(world\.sourceMaps\.map\(map => map\.id\)\)/);
    assert.match(workspace, /const uploadedSourceMap = this\.details\.find\(map => !existingSourceMapIds\.has\(map\.id\)\) \?\? null/);
    assert.match(workspace, /this\.selected = uploadedSourceMap/);
    assert.match(workspace, /uploadedSourceMap\.containsBakedGrid && !uploadedSourceMap\.alignment/);
    assert.match(workspace, /await this\.gridAlignmentController\.beginAndPreview\(uploadedSourceMap\)/);
});

test("grid detection scrolls its workflow into view and exposes analysis progress", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /scrollIntoView\(\{ behavior: "smooth", block: "nearest" \}\)/);
    assert.match(controller, /setAnalysisBusy\(true\)/);
    assert.match(controller, /aria-busy/);
    assert.match(controller, /Analyzing…/);
    assert.match(controller, /Re-run detection/);
});

test("switching maps or starting a newer analysis invalidates stale asynchronous previews", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /private analysisGeneration = 0/);
    assert.match(controller, /this\.analysisAbortController\?\.abort\(\);\s*this\.analysisAbortController = null;\s*this\.analysisGeneration \+= 1;\s*this\.selectedMap = sourceMap/);
    assert.match(controller, /const generation = \+\+this\.analysisGeneration/);
    assert.match(controller, /generation !== this\.analysisGeneration/);
    assert.match(controller, /this\.selectedMap\?\.id !== sourceMap\.id/);
    assert.match(controller, /const scaleContext = await loadPhysicalScaleContext[\s\S]*generation !== this\.analysisGeneration/);
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

test("lower-confidence or downscaled previews remain explicitly applicable after confirmation", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /analyzed\.analysis\.sourceResolutionVerified/);
    assert.match(controller, /source-resolution phase verified/);
    assert.match(controller, /downscaled phase only/);
    assert.match(controller, /Source-resolution phase verification is unavailable/);
    assert.match(controller, /this\.applyButton\.disabled = false/);
    assert.match(controller, /Apply anyway/);
    assert.match(controller, /window\.confirm\(/);
    assert.match(controller, /Apply this preview anyway/);
    assert.doesNotMatch(controller, /isCanonicalSourceFit/);
    assert.doesNotMatch(controller, /high-confidence, source-verified grid detection must be previewed/);
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

test("preview reports distant-region residual and turns threshold failures into review warnings", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /worst distant residual/);
    assert.match(controller, /fit\.residualPixels/);
    assert.match(controller, /final rigid overlay misses at least one distant region/);
    assert.match(controller, /recommended limit is/);
    assert.match(controller, /explicitly applied after confirming these warnings/);
});

test("verified physical-scale conflicts remain part of explicit apply confirmation", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /PhysicalScaleConfirmationTolerance = 0\.01/);
    assert.match(controller, /detectPhysicalScaleChange\(world\.grid, proposal\.grid\)/);
    assert.match(controller, /Physical distance per hex changes from/);
    assert.match(controller, /window\.confirm\(/);
    assert.match(controller, /Alignment was not applied/);
});
