import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { HexCrawlApi } from "../.test-dist/api.js";
import { solveAffine, transformPoint } from "../.test-dist/affine-registration.js";

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
