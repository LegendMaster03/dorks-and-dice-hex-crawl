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

test("a short atomic edge does not hide the complete representative polygon", t => {
    const dom = new JSDOM("<!doctype html><body></body>");
    const before = { document: globalThis.document, window: globalThis.window };
    globalThis.document = dom.window.document;
    globalThis.window = dom.window;
    const visiblePaths = [];
    let path = [];
    dom.window.HTMLCanvasElement.prototype.getContext = () => ({
        setTransform() {}, fillRect() {}, save() {}, beginPath() { path = []; },
        rect() {}, clip() {}, closePath() {}, restore() {},
        moveTo(x, y) { path.push({ x, y }); },
        lineTo(x, y) { path.push({ x, y }); },
        fill() { visiblePaths.push([...path]); }, stroke() {}
    });
    t.after(() => {
        globalThis.document = before.document;
        globalThis.window = before.window;
        dom.window.close();
    });
    const representative = {
        displayName: "Subdivided pentagon",
        realization: {
            translationU: { x: 100, y: 0 },
            translationV: { x: 0, y: 100 },
            polygons: { tile: [
                { x: 0, y: 0 }, { x: 2, y: 0 },
                { x: 2, y: 0.01 }, { x: 1.5, y: 1 },
                { x: 0, y: 1 }
            ] }
        }
    };
    tilingPreview(representative);
    assert.equal(visiblePaths.length, 1, "the isolated exemplar must be drawn once");
    const xs = visiblePaths[0].map(p => p.x);
    const ys = visiblePaths[0].map(p => p.y);
    assert.ok(Math.min(...xs) > 8 && Math.max(...xs) < 66,
        "complete tile must fit horizontally");
    assert.ok(Math.min(...ys) > 5 && Math.max(...ys) < 47,
        "complete tile must fit vertically");
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
