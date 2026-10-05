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

test("one-click alignment applies a returned usable fit without exposing detector thresholds", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /if \(!analyzed\.fit \|\| analyzed\.status === "gridless"\)/);
    assert.match(controller, /A usable hex grid could not be detected/);
    assert.doesNotMatch(controller, /isCanonicalSourceFit/);
    assert.doesNotMatch(controller, /sourceResolutionVerified/);
    assert.doesNotMatch(controller, /confidence.*threshold|residual.*limit/i);
    assert.match(controller, /Promise<boolean>/);
    assert.match(controller, /return false/);
    assert.match(controller, /return true/);
    assert.match(workspace, /const applied = await this\.gridAlignmentController\.detectAndApply/);
    assert.match(workspace, /if \(applied\) this\.mapHint\.textContent = `Grid alignment updated/);
});

test("automatic detection failures leave the source map available for manual placement", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");

    assert.match(controller, /Automatic grid detection is unavailable/);
    assert.match(controller, /The map remains visible; try again or use Manual placement/);
    assert.match(controller, /A usable hex grid could not be detected/);
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
    assert.match(controller, /if \(!this\.isCurrent[\s\S]*return false/);
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
    assert.match(controller, /catch \(error\) \{\s*if \(signal\.aborted\) throw error;\s*return null;/);
    assert.doesNotMatch(controller, /window\.confirm/);
});

test("unregistered map images receive temporary centered placement instead of disappearing", () => {
    const renderer = fs.readFileSync(path.join(sourceRoot, "canvas-renderer.ts"), "utf8");

    assert.match(renderer, /private readonly provisionalSourceMapTransforms/);
    assert.match(renderer, /private provisionalSourceMapTransform\(/);
    assert.match(renderer, /previewTransform[\s\S]*savedTransform[\s\S]*provisionalSourceMapTransform/);
    assert.match(renderer, /this\.viewport\.center\.x - \(\(dimensions\.width \* scale\) \/ 2\)/);
    assert.match(renderer, /this\.viewport\.center\.y - \(\(dimensions\.height \* scale\) \/ 2\)/);
    assert.match(renderer, /this\.provisionalSourceMapTransforms\.set\(sourceMapId, transform\)/);
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

test("coordinated map views preserve manual GM/player visibility and leave auxiliary references independent", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");

    assert.match(workspace, /type CoordinatedRasterView = "manual" \| "gm-grid" \| "gm-gridless" \| "player-grid" \| "player-gridless"/);
    assert.match(workspace, /private coordinatedRasterView: CoordinatedRasterView = "manual"/);
    assert.match(workspace, /private manualHiddenSourceMapIds: Set<string> \| null = null/);
    assert.match(workspace, /this\.manualHiddenSourceMapIds = new Set/);
    assert.match(workspace, /if \(this\.manualHiddenSourceMapIds\.has\(sourceMap\.id\)\)/);
    assert.match(workspace, /sourceMap\.role === target\.role/);
    assert.match(workspace, /sourceMap\.containsBakedGrid === target\.containsBakedGrid/);
    assert.match(workspace, /Shared and auxiliary references keep their manual visibility/);
    assert.match(workspace, /if \(isCoordinatedViewSource\(sourceMap\)\) this\.switchToManualVisibility\(\)/);
    assert.match(workspace, /visible\.dataset\.sourceMapVisibleId = sourceMap\.id/);
    assert.match(workspace, /private syncVisibilityControls\(\): void/);
});

test("grid alignment and manual placement can not run as overlapping map workflows", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const registration = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-registration-controller.ts"),
        "utf8");

    assert.match(workspace, /private gridAlignmentBusy = false/);
    assert.match(workspace, /Another grid alignment is already in progress/);
    assert.match(workspace, /this\.registrationController\.cancelActive\(\)/);
    assert.match(workspace, /private setGridAlignmentBusy\(busy: boolean\): void/);
    assert.match(workspace, /\[data-align-grid\], \[data-register\]/);
    assert.match(registration, /public cancelActive\(\): void/);
});

test("normal map-management UI uses plain-language image and placement terminology", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-grid-alignment-controller.ts"),
        "utf8");
    const registration = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-registration-controller.ts"),
        "utf8");

    assert.match(workspace, />Map view </);
    assert.match(workspace, />Map images </);
    assert.match(workspace, />Selected reference map</);
    assert.match(workspace, />Delete reference map</);
    assert.match(workspace, />Manual placement</);
    assert.match(workspace, />Manual map placement</);
    assert.match(registration, /Drag to pan; wheel zooms/);
    assert.doesNotMatch(registration, /Shift-drag/);
    assert.doesNotMatch(workspace, />Raster view |\bRaster files\b|>Selected raster map|>Delete raster map|>Advanced registration/);
    assert.doesNotMatch(controller, /usable raster dimensions|Advanced registration/);
});
