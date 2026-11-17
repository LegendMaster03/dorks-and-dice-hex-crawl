import assert from "node:assert/strict";
import test from "node:test";
import { registerHooks } from "node:module";
import { JSDOM } from "jsdom";

registerHooks({ resolve(specifier, context, next) {
    try { return next(specifier, context); }
    catch (error) {
        if (specifier.startsWith(".") && !specifier.endsWith(".js"))
            return next(specifier + ".js", context);
        throw error;
    }
} });

const { tilingPreview } = await import("../.test-dist/modules/procedures/tiling-preview.js");

test("small previews trace only visible translations of the shared polygon realization", t => {
    const dom = new JSDOM("<!doctype html><body></body>");
    const before = { document: globalThis.document, window: globalThis.window };
    globalThis.document = dom.window.document;
    globalThis.window = dom.window;
    let fills = 0;
    let strokes = 0;
    let vertices = 0;
    const ctx = {
        setTransform() {}, fillRect() {}, save() {}, beginPath() {},
        rect() {}, clip() {}, closePath() {}, restore() {},
        moveTo() { vertices++; }, lineTo() { vertices++; },
        fill() { fills++; }, stroke() { strokes++; }
    };
    dom.window.HTMLCanvasElement.prototype.getContext = () => ctx;
    t.after(() => {
        globalThis.document = before.document;
        globalThis.window = before.window;
        dom.window.close();
    });

    const square = {
        displayName: "Square", dsSymbol: "square",
        realization: {
            translationU: { x: 1, y: 0 },
            translationV: { x: 0, y: 1 },
            polygons: { square: [
                { x: 0, y: 0 }, { x: 1, y: 0 },
                { x: 1, y: 1 }, { x: 0, y: 1 }
            ] }
        }
    };
    const canvas = tilingPreview(square);
    assert.equal(canvas.getAttribute("role"), "img");
    assert.equal(canvas.getAttribute("aria-label"), "Square");
    assert.ok(fills > 0, "the visible central tiling must still be drawn");
    assert.equal(strokes, fills);
    assert.equal(vertices, fills * 4);
    // The legacy renderer traced 17×17 = 289 copies, most far outside 74×52px.
    assert.ok(fills < 60, `visible-area culling must avoid the 289 off-screen copies: ${fills}`);
});

test("an unavailable metric leaves a safe blank preview", t => {
    const dom = new JSDOM("<!doctype html><body></body>");
    const before = { document: globalThis.document, window: globalThis.window };
    globalThis.document = dom.window.document;
    globalThis.window = dom.window;
    dom.window.HTMLCanvasElement.prototype.getContext = () => null;
    t.after(() => {
        globalThis.document = before.document;
        globalThis.window = before.window;
        dom.window.close();
    });
    assert.equal(tilingPreview(undefined).getAttribute("aria-label"), "Unlisted tiling");
});
