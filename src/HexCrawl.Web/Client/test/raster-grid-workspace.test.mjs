import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceRoot = path.resolve("src");

test("reference-map workspace exposes automatic baked-grid detection before manual registration", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");
    const renderer = fs.readFileSync(path.join(sourceRoot, "canvas-renderer.ts"), "utf8");

    assert.match(workspace, /SourceMapGridAlignmentController/);
    assert.match(workspace, /Detect \/ repair hex grid/);
    assert.match(workspace, /Detect and preview/);
    assert.match(workspace, /Advanced registration/);
    assert.match(controller, /detectHexLattice/);
    assert.match(controller, /buildRasterGridAlignmentProposal/);
    assert.match(controller, /detection\.status !== "detected"/);
    assert.match(controller, /gridPreview = proposal\.grid/);
    assert.match(controller, /registrationPreview =/);
    assert.match(controller, /grid-alignment/);
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

test("grid detection analyzes a bounded-resolution copy and rescales measurements to source pixels", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /AnalysisMaximumDimension = 768/);
    assert.match(controller, /createImageBitmap/);
    assert.match(controller, /centerSpacingPixels: fit\.centerSpacingPixels \/ scale/);
    assert.match(controller, /anchorPixel:/);
    assert.match(controller, /residualPixels: fit\.residualPixels \/ scale/);
});

test("Wonderdraft grid metadata and scale-bar metadata remain independent cross-checks", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /gridMetadata: Record<string, string>/);
    assert.match(controller, /metadata\["grid\.size"\]/);
    assert.match(controller, /grid\.size=/);
    assert.match(controller, /raster detector/);
    assert.match(controller, /difference/);
    assert.match(controller, /raster geometry is not overridden/);
    assert.match(controller, /scale-bar metadata/);
    assert.match(controller, /physicalScale\.unitsPerPixel \/ context\.uniformScale/);
});

test("preview reports final worst distant-region behavior", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /worst distant residual/);
    assert.match(controller, /fit\.residualPixels/);
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
