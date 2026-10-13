import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceRoot = path.resolve("src");

test("generalized motif assessment provides guarded automatic source-map placement", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"), "utf8");
    const registration = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-registration-controller.ts"), "utf8");

    assert.match(workspace, /assessSourceMapCompatibility/);
    assert.match(workspace, /assessment\.registration\?\.status === "candidates"/);
    assert.match(workspace, /this\.api\.applyMotifRegistration/);
    assert.match(workspace, /this\.api\.applyMotifCorrection/);
    assert.match(workspace, /this\.map\.renderer\.registrationPreview =/);
    assert.match(workspace, /this\.getWorld\(\)\.version !== assessment\.worldVersion/);
    assert.match(workspace, /generation !== this\.compatibilityGeneration/);
    assert.match(workspace, /this\.registrationController\.cancelActive\(\)/);
    assert.match(registration, /this\.map\.setClickInterceptor/);
    assert.doesNotMatch(workspace, /gridAlignmentController|data-align-grid|Detect \/ repair hex grid/);
});

test("generalized motif alignment never silently changes a world or retained map", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"), "utf8");

    assert.match(workspace, /Preview placement/);
    assert.match(workspace, /Acceptance changes this image.s placement only/);
    assert.match(workspace, /Existing world geometry was not changed/);
    assert.match(workspace, /applyMotifCorrection/);
    assert.match(workspace, /this\.getWorld\(\)\.version !== assessment\.worldVersion/);
    assert.match(workspace, /this\.api\.applyMotifRegistration/);
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

test("manual map placement remains independent of detected motif registration", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"), "utf8");
    const registration = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-registration-controller.ts"), "utf8");

    assert.match(workspace, /this\.registrationController\.begin\(this\.selected\)/);
    assert.match(workspace, /this\.registrationController\.cancelActive\(\)/);
    assert.match(registration, /public cancelActive\(\): void/);
    assert.match(registration, /this\.map\.renderer\.registrationPreview = null/);
});

test("normal map-management UI uses plain-language image and placement terminology", () => {
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
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
});
