import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { HexCrawlApi } from "../.test-dist/api.js";
import { solveAffine, transformPoint } from "../.test-dist/affine-registration.js";
import { Viewport } from "../.test-dist/viewport.js";

const sourceRoot = path.resolve("src");

function pair(sourcePixel, worldPoint) {
    return { sourcePixel, worldPoint };
}

test("frontend affine solver handles identity and translation", () => {
    const identity = solveAffine([
        pair({ x: 0, y: 0 }, { x: 0, y: 0 }),
        pair({ x: 10, y: 0 }, { x: 10, y: 0 }),
        pair({ x: 0, y: 5 }, { x: 0, y: 5 })
    ]);
    assert.deepEqual(transformPoint(identity, { x: 4, y: 3 }), { x: 4, y: 3 });

    const translated = solveAffine([
        pair({ x: 0, y: 0 }, { x: 7, y: -3 }),
        pair({ x: 2, y: 0 }, { x: 9, y: -3 }),
        pair({ x: 0, y: 2 }, { x: 7, y: -1 })
    ]);
    assert.deepEqual(transformPoint(translated, { x: 1, y: 1 }), { x: 8, y: -2 });
});

test("frontend affine solver rejects degenerate control points", () => {
    assert.throws(() => solveAffine([
        pair({ x: 0, y: 0 }, { x: 0, y: 0 }),
        pair({ x: 1, y: 1 }, { x: 2, y: 2 }),
        pair({ x: 2, y: 2 }, { x: 4, y: 4 })
    ]), /collinear/);
});

test("viewport resizing does not change the world point represented by a registered raster point", () => {
    const viewport = new Viewport();
    viewport.center = { x: 17.25, y: -9.5 };
    viewport.zoom = 73;

    const worldPoint = { x: 22.75, y: -2.125 };
    for (const [width, height] of [[320, 240], [1280, 720], [1920, 1080]]) {
        const screen = viewport.worldToScreen(worldPoint, width, height);
        const roundTrip = viewport.screenToWorld(screen.x, screen.y, width, height);
        assert.ok(Math.abs(roundTrip.x - worldPoint.x) < 1e-12);
        assert.ok(Math.abs(roundTrip.y - worldPoint.y) < 1e-12);
    }

    const renderer = fs.readFileSync(path.join(sourceRoot, "canvas-renderer.ts"), "utf8");
    assert.match(renderer, /const width = this\.canvas\.width \/ ratio/);
    assert.match(renderer, /const height = this\.canvas\.height \/ ratio/);
    assert.match(renderer, /ctx\.setTransform\(ratio, 0, 0, ratio, 0, 0\)/);
});

test("standalone source map assets use the local API path", async () => {
    const { api } = await HexCrawlApi.create({ dataset: {} });
    assert.equal(api.sourceMapAssetUrl("world 1", "map 1"), "/api/overworlds/world%201/source-maps/map%201/asset");
});

test("renderer source puts rasters before semantic regions and grid", () => {
    const renderer = fs.readFileSync(path.join(sourceRoot, "canvas-renderer.ts"), "utf8");
    const renderBody = renderer.slice(renderer.indexOf("public render(): void"), renderer.indexOf("public dispose(): void"));
    assert.ok(renderBody.indexOf("this.drawSourceMaps") < renderBody.indexOf("this.drawRegions"));
    assert.ok(renderBody.indexOf("this.drawRegions") < renderBody.indexOf("this.drawGrid"));
    assert.match(renderer, /hiddenSourceMapIds/);
    assert.match(renderer, /rasterCache\.get/);
});

test("registration consumes map clicks through an explicit interaction interceptor", () => {
    const surface = fs.readFileSync(path.join(sourceRoot, "map-surface.ts"), "utf8");
    const workspace = fs.readFileSync(path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"), "utf8");
    const registration = fs.readFileSync(path.join(sourceRoot, "modules/worlds/source-map-registration-controller.ts"), "utf8");
    assert.match(surface, /clickInterceptor\?\.\(point\)/);
    assert.match(workspace, /SourceMapRegistrationController/);
    assert.match(registration, /if \(!this\.registration\) return false/);
    assert.match(registration, /Registration mode is active/);
});

test("Wonderdraft source import is automatic, scalable, and spatially reviewable", () => {
    const controller = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/wonderdraft-import-controller.ts"),
        "utf8");
    const workspace = fs.readFileSync(
        path.join(sourceRoot, "modules/worlds/source-map-workspace.ts"),
        "utf8");
    const renderer = fs.readFileSync(path.join(sourceRoot, "canvas-renderer.ts"), "utf8");
    const surface = fs.readFileSync(path.join(sourceRoot, "map-surface.ts"), "utf8");

    assert.match(controller, /importWonderdraftSource/);
    assert.match(controller, /Suggested semantic review/);
    assert.match(controller, /Symbol group/);
    assert.match(controller, /candidate\.properties/);
    assert.match(controller, /summarizeSymbolGroups/);
    assert.match(controller, /const selectedGroup = groupApplies \? group\.value : ""/);
    assert.match(controller, /const pageSize = 50/);
    assert.doesNotMatch(controller, /maximumRendered\s*=\s*200/);
    assert.match(controller, /Highlight on map/);
    assert.match(workspace, /preserves Wonderdraft source content first/);
    assert.match(renderer, /reviewOverlay/);
    assert.match(renderer, /hitTestReview/);
    assert.match(surface, /setReviewSelectionHandler/);
    const activatePoint = surface.slice(
        surface.indexOf("private activatePoint"),
        surface.indexOf("private handleKeyDown"));
    assert.ok(activatePoint.indexOf("clickInterceptor") < activatePoint.indexOf("worldToHex"));
    assert.ok(activatePoint.indexOf("hitTestReview") < activatePoint.indexOf("worldToHex"));
});

test("route cleanup disposes raster resources and application-owned DOM does not use MutationObserver", () => {
    const surface = fs.readFileSync(path.join(sourceRoot, "map-surface.ts"), "utf8");
    const cache = fs.readFileSync(path.join(sourceRoot, "raster-image-cache.ts"), "utf8");
    assert.match(surface, /this\.renderer\.dispose\(\)/);
    assert.match(cache, /URL\.revokeObjectURL/);

    const pending = [sourceRoot];
    while (pending.length > 0) {
        const directory = pending.pop();
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            const location = path.join(directory, entry.name);
            if (entry.isDirectory()) {
                pending.push(location);
                continue;
            }
            if (!entry.name.endsWith(".ts")) continue;
            const content = fs.readFileSync(location, "utf8");
            const relative = path.relative(sourceRoot, location);
            assert.doesNotMatch(
                content,
                /MutationObserver/,
                `${relative} must not use MutationObserver for application-owned DOM`);
        }
    }
});
