import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceRoot = path.resolve("src");

test("reference-map grid detection is one operation that analyzes and persists", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");
    const api = fs.readFileSync(path.join(sourceRoot, "api.ts"), "utf8");

    assert.match(workspace, /Detect \/ repair hex grid/);
    assert.match(workspace, /detectAndApply\(sourceMap/);
    assert.match(workspace, /Analyzing…/);
    assert.match(workspace, /Applying…/);
    assert.doesNotMatch(workspace, /Apply detected alignment|Re-run detection|data-grid-alignment-panel/);

    assert.match(controller, /public async detectAndApply/);
    assert.match(controller, /analyzeSourceMapGrid/);
    assert.match(controller, /buildRasterGridAlignmentProposal/);
    assert.match(controller, /applyGridAlignment/);
    assert.match(controller, /registrationPreview =/);
    assert.match(controller, /gridPreview = proposal\.grid/);
    assert.match(controller, /onProgress\("detecting"\)/);
    assert.match(controller, /onProgress\("applying"\)/);
    assert.doesNotMatch(controller, /window\.confirm/);
    assert.doesNotMatch(controller, /Apply anyway/);
    assert.match(api, /source-maps\/\$\{encodeURIComponent\(sourceMapId\)\}\/grid-analysis/);
});

test("automatic detection failures leave the source map available for manual registration", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /Automatic grid detection is unavailable/);
    assert.match(controller, /The map remains visible; try again or use Advanced registration/);
    assert.match(controller, /A usable hex grid could not be detected/);
    assert.match(controller, /use Advanced registration if needed/);
});

test("switching maps or starting a newer analysis invalidates stale asynchronous work", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /private analysisGeneration = 0/);
    assert.match(controller, /this\.analysisAbortController\?\.abort\(\)/);
    assert.match(controller, /const generation = \+\+this\.analysisGeneration/);
    assert.match(controller, /generation === this\.analysisGeneration/);
    assert.match(controller, /this\.selectedMapId === sourceMapId/);
    assert.match(controller, /abortController\.signal\.aborted/);
});

test("Wonderdraft physical scale remains an independent optional cross-check", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /gridMetadata: Record<string, string>/);
    assert.match(controller, /metadataValue\(metadata, "grid\.size"\)/);
    assert.match(controller, /selectPhysicalDistancePerHex/);
    assert.match(controller, /physicalScale\.unitsPerPixel \/ context\.uniformScale/);
    assert.match(controller, /considerWholeUnits: grid\.neighborCenterDistance\.unit\.kind === "Mile"/);
    assert.doesNotMatch(controller, /window\.confirm/);
});

test("map sets collect alternate source versions without becoming the future layer model", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, /A map set is a collection of alternate source versions of the same map and geographic extent/);
    assert.match(workspace, /neighboring regional map belongs in a different set/);
    assert.match(workspace, /future image comparison can derive a common base and true visual-difference layers/);
    assert.match(workspace, /without treating whole map images as the final layer model/);
    assert.match(workspace, /Shared \/ neutral reference/);
    assert.match(workspace, /Auxiliary \/ reference only/);
    assert.doesNotMatch(workspace, /Bellowing Wilds|Kylandria/);
});

test("map-set import accepts several image versions and reuses an existing set by selection", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, /type="file"[^>]*multiple/);
    assert.match(workspace, /Choose an existing set to avoid spelling variants/);
    assert.match(workspace, /Create new map set/);
    assert.match(workspace, /renderUploadFiles\(\)/);
    assert.match(workspace, /data-source-map-upload-index/);
    assert.match(workspace, /for \(const \[index, file\] of files\.entries\(\)\)/);
    assert.match(workspace, /role: select\(row, "mapRole"\)\.value as SourceMapRole/);
    assert.match(workspace, /containsBakedGrid: input\(row, "mapBakedGrid"\)\.checked/);
});

test("partial multi-image imports recover without encouraging duplicate retries", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, /let completed = 0/);
    assert.match(workspace, /completed \+= 1/);
    assert.match(workspace, /if \(completed === 0\) throw error/);
    assert.match(workspace, /Those maps are already in \$\{geographyKey\}; re-select only the images that are still missing/);
    assert.match(workspace, /this\.uploadForm\.reset\(\)/);
    assert.match(workspace, /await this\.refresh\(\)/);
});

test("coordinated map views switch GM and player sources across sets but leave auxiliary references manual", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, /type CoordinatedRasterView = "manual" \| "gm-grid" \| "gm-gridless" \| "player-grid" \| "player-gridless"/);
    assert.match(workspace, /if \(sourceMap\.role !== "Gm" && sourceMap\.role !== "Player"\) continue/);
    assert.match(workspace, /sourceMap\.role === target\.role/);
    assert.match(workspace, /sourceMap\.containsBakedGrid === target\.containsBakedGrid/);
    assert.match(workspace, /Shared and auxiliary references keep their manual visibility/);
    assert.match(workspace, /data\.sourceMapVisibleId = sourceMap\.id/);
    assert.match(workspace, /private syncVisibilityControls\(\): void/);
    assert.match(workspace, /this\.syncVisibilityControls\(\)/);
});

test("normal map-management UI does not require raster terminology", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, />Map view </);
    assert.match(workspace, />Map images </);
    assert.match(workspace, />Selected reference map</);
    assert.match(workspace, />Delete reference map</);
    assert.doesNotMatch(workspace, />Raster view |\bRaster files\b|>Selected raster map|>Delete raster map/);
});
