import test from "node:test";
import assert from "node:assert/strict";
import { registerHooks } from "node:module";
import { JSDOM } from "jsdom";

// Production is bundled by Vite. Resolve the emitted TS imports in Node as well.
registerHooks({ resolve(specifier, context, next) {
    try { return next(specifier, context); }
    catch (error) {
        if (specifier.startsWith(".") && !specifier.endsWith(".js"))
            return next(specifier + ".js", context);
        throw error;
    }
} });
const { RasterImageCache } = await import("../.test-dist/raster-image-cache.js");
const { CanvasMapRenderer } = await import("../.test-dist/canvas-renderer.js");
const { MapSurface } = await import("../.test-dist/map-surface.js");
const { Viewport } = await import("../.test-dist/viewport.js");
const { renderWorldEditor } = await import("../.test-dist/modules/worlds/world-editor-view.js");
const deferred = () => {
    let resolve, reject;
    const promise = new Promise((a, b) => { resolve = a; reject = b; });
    return { promise, resolve, reject };
};
const tick = () => new Promise(resolve => setTimeout(resolve, 0));
const world = () => ({ id: "world", version: 1, name: "Fixture", features: [], locations: [], sourceMaps: [],
    tiling: { id: "tiling", revision: 1, origin: { x: 0, y: 0 },
        playableBounds: { width: 5, height: 5 }, topology: { motifCells: [{ id: "square" }] } } });

test("raster waiters join existing loads and are rejected on failure or disposal", async t => {
    t.mock.method(globalThis, "fetch", () => request.promise);
    const request = deferred();
    const previousWindow = globalThis.window;
    const previousBitmap = globalThis.createImageBitmap;
    globalThis.window = { setTimeout: (fn, ms) => setTimeout(fn, ms < 15000 ? 0 : ms), clearTimeout };
    const image = {};
    globalThis.createImageBitmap = async () => image;
    const cache = new RasterImageCache();
    t.after(() => { cache.dispose(); globalThis.window = previousWindow; globalThis.createImageBitmap = previousBitmap; });
    let ready = 0;
    cache.get("/image", () => ready++);
    const first = cache.waitUntilReady("/image"), second = cache.waitUntilReady("/image");
    request.resolve({ ok: true, blob: async () => new Blob() });
    assert.equal(await first, image);
    assert.equal(await second, image);
    assert.equal(ready, 1);
    assert.equal(await cache.waitUntilReady("/image"), image);

    t.mock.method(globalThis, "fetch", async () => { throw new Error("offline"); });
    await assert.rejects(cache.waitUntilReady("/failed"), /offline/);
    // An explicit retry can recover an exhausted cache entry.
    t.mock.method(globalThis, "fetch", async () => ({ ok: true, blob: async () => new Blob() }));
    assert.equal(await cache.waitUntilReady("/failed"), image);

    let signal;
    t.mock.method(globalThis, "fetch", (_url, options) => { signal = options.signal; return new Promise(() => {}); });
    const pending = cache.waitUntilReady("/pending");
    cache.dispose();
    await assert.rejects(pending, /canceled/);
    assert.equal(signal.aborted, true);
    await assert.rejects(cache.waitUntilReady("/later"), /disposed/);
});

test("raster timeout detaches its waiter without preventing the renderer from recovering", async t => {
    const request = deferred();
    t.mock.method(globalThis, "fetch", () => request.promise);
    const previousWindow = globalThis.window, previousBitmap = globalThis.createImageBitmap;
    globalThis.window = { setTimeout, clearTimeout };
    globalThis.createImageBitmap = async () => ({});
    const cache = new RasterImageCache();
    t.after(() => { cache.dispose(); globalThis.window = previousWindow; globalThis.createImageBitmap = previousBitmap; });
    let ready = false;
    cache.get("/slow", () => { ready = true; });
    await assert.rejects(cache.waitUntilReady("/slow", 1), /timed out/);
    assert.equal(cache.entries.get("/slow").waiters.size, 0);
    request.resolve({ ok: true, blob: async () => new Blob() });
    await tick();
    assert.equal(ready, true);
    assert.ok(await cache.waitUntilReady("/slow"));
});

function surfaceFixture() {
    let current = world();
    const surface = Object.create(MapSurface.prototype);
    Object.assign(surface, { disposed: false, viewport: new Viewport(),
        canvas: { clientWidth: 800, clientHeight: 600, width: 800, height: 600 },
        getWorld: () => current, api: { listVisibleCells: async () => [] },
        cellRequestGeneration: 0, cellSelectionGeneration: 0,
        accessibilityStatus: { textContent: "", className: "" }, requestRender() {} });
    surface.renderer = new CanvasMapRenderer(surface.canvas, surface.viewport, surface.getWorld, () => {}, true);
    surface.downloads = [];
    surface.downloadPng = async (canvas, name) => surface.downloads.push({ canvas, name });
    return { surface, change: next => current = next };
}

for (const audience of ["gm", "player"]) {
    test(`${audience} viewport export captures pan/size before waiting and leaves live state untouched`, async t => {
        const { surface } = surfaceFixture();
        const cells = deferred();
        let query, rendered;
        surface.api.listVisibleCells = (_id, bounds) => { query = bounds; return cells.promise; };
        surface.renderer.authoringGeometry = { kind: "Point", position: { x: 1, y: 1 }, vertices: [] };
        surface.renderer.visibleWorldCells = ["live geometry"];
        const previousDocument = globalThis.document, previousWindow = globalThis.window;
        globalThis.document = { createElement: () => ({ width: 0, height: 0 }) };
        globalThis.window = { devicePixelRatio: 2 };
        t.after(() => { globalThis.document = previousDocument; globalThis.window = previousWindow; });
        t.mock.method(CanvasMapRenderer.prototype, "waitForSourceMaps", async () => {});
        t.mock.method(CanvasMapRenderer.prototype, "render", function () {
            rendered = { center: this.viewport.center, zoom: this.viewport.zoom, world: this.getWorld(), draft: this.authoringGeometry };
        });
        const preview = audience === "player" ? { world: surface.getWorld(), terrainAnnotations: [],
            featureLabelIds: [], locationLabelIds: [], showGrid: false } : undefined;
        const pending = surface.exportViewportPng("fixture", "light", preview);
        surface.viewport.center = { x: 100, y: 100 };
        surface.viewport.zoom = 12;
        surface.canvas.clientWidth = 1200;
        cells.resolve([]);
        await pending;
        assert.deepEqual(rendered.center, { x: 0, y: 0 });
        assert.equal(rendered.zoom, 112);
        assert.equal(rendered.draft, null);
        assert.equal(query.maxX, 400 / 56);
        assert.equal(surface.downloads[0].canvas.width, 1600);
        assert.equal(surface.downloads[0].canvas.height, 1200);
        assert.notEqual(surface.downloads[0].canvas, surface.canvas);
        assert.deepEqual(surface.renderer.visibleWorldCells, ["live geometry"]);
        assert.equal(surface.renderer.authoringGeometry.kind, "Point");
    });
}

for (const operation of ["world change", "dispose"]) {
    test(`viewport export rejects ${operation} during image loading`, async t => {
        const { surface, change } = surfaceFixture();
        const images = deferred();
        let exportRenderer;
        const previousDocument = globalThis.document, previousWindow = globalThis.window;
        globalThis.document = { createElement: () => ({ width: 0, height: 0 }) };
        globalThis.window = { devicePixelRatio: 1 };
        t.after(() => { globalThis.document = previousDocument; globalThis.window = previousWindow; });
        t.mock.method(CanvasMapRenderer.prototype, "waitForSourceMaps", function () { exportRenderer = this; return images.promise; });
        const pending = surface.exportViewportPng("fixture", "light");
        await tick();
        if (operation === "world change") change({ ...world(), version: 2 });
        else surface.disposed = true;
        images.resolve();
        await assert.rejects(pending, /changed during export/);
        assert.equal(surface.downloads.length, 0);
        assert.equal(exportRenderer.rasterCache.disposed, true);
    });
}

test("export copies established source-image placement without registration previews", () => {
    const { surface } = surfaceFixture();
    const target = new CanvasMapRenderer({}, new Viewport(), surface.getWorld, () => {}, true);
    const placement = { kind: "Affine", m11: 2, m12: 0, m13: 15, m21: 0, m22: 2, m23: 20, m31: 0, m32: 0 };
    surface.renderer.provisionalSourceMapTransforms.set("source", placement);
    surface.renderer.registrationPreview = { sourceMapId: "source", transform: { ...placement, m13: 100 } };
    surface.renderer.copySourceMapPlacementsTo(target);
    placement.m13 = 25;
    assert.equal(target.provisionalSourceMapTransforms.get("source").m13, 15);
    assert.equal(target.registrationPreview, null);
});

test("player full-map export rejects released unaligned images even when hidden in the GM editor", async () => {
    const { surface, change } = surfaceFixture();
    const released = { ...world(), sourceMaps: [{ id: "player-raster", name: "Unaligned", alignment: null }] };
    change(released);
    surface.renderer.hiddenSourceMapIds.add("player-raster");
    await assert.rejects(surface.exportFullMapPng("fixture", "light", 1600, true, true, {
        world: released, terrainAnnotations: [], featureLabelIds: [], locationLabelIds: [], showGrid: true
    }), /Align or hide/);
});

test("failed wider cell query retains geometry; returning to cached bounds ignores late responses", async () => {
    const { surface } = surfaceFixture();
    surface.loadedCells = { worldKey: "world:tiling:1", bounds: { minX: -10, minY: -10, maxX: 10, maxY: 10 } };
    const geometry = [{ id: "original" }];
    surface.renderer.visibleWorldCells = geometry;
    surface.api.listVisibleCells = async () => { throw new Error("enumeration limit"); };
    surface.viewport.zoom = 12;
    surface.loadVisibleCells();
    await tick();
    assert.equal(surface.renderer.visibleWorldCells, geometry);
    assert.equal(surface.accessibilityStatus.className, "hc-hint");
    surface.viewport.zoom = 56;
    surface.loadVisibleCells();
    assert.equal(surface.renderer.visibleWorldCells, geometry);
    const later = deferred();
    surface.api.listVisibleCells = () => later.promise;
    surface.viewport.zoom = 12;
    surface.loadVisibleCells();
    surface.viewport.zoom = 56;
    surface.loadVisibleCells();
    later.resolve([{ id: "wrong viewport" }]);
    await tick();
    assert.equal(surface.renderer.visibleWorldCells, geometry);
});

async function editorFixture(t, initialWorld = world()) {
    const dom = new JSDOM('<div id="root"></div>', { url: "https://fixture.invalid/" });
    const keys = ["window", "document", "HTMLElement", "HTMLCanvasElement", "HTMLFormElement", "HTMLDetailsElement",
        "HTMLInputElement", "HTMLSelectElement", "HTMLButtonElement", "CustomEvent", "localStorage", "ResizeObserver", "requestAnimationFrame"];
    const previous = new Map(keys.map(key => [key, globalThis[key]]));
    for (const key of keys) globalThis[key] = key === "window" ? dom.window : dom.window[key];
    globalThis.ResizeObserver = class { observe() {} disconnect() {} };
    globalThis.requestAnimationFrame = () => 1;
    let surface;
    t.mock.method(MapSurface.prototype, "requestRender", function () { surface = this; });
    t.mock.method(globalThis, "fetch", async () => ({ ok: true, json: async () => [] }));
    let dispose = () => {};
    let disposed = false;
    const cleanup = () => { if (!disposed) { disposed = true; dispose(); } };
    t.after(() => { cleanup(); for (const [key, value] of previous) globalThis[key] = value; dom.window.close(); });
    const state = { current: initialWorld, fail: false };
    const api = { getOverworld: async () => state.current, getWorldEnvironment: async () => ({ annotations: [], version: 1 }),
        listExpeditions: async () => [], listSourceMaps: async () => ({ sourceMaps: [] }), getProcedurePresets: async () => [],
        getPresentationProfiles: async () => [{ key: "exploration-map", name: "Exploration Map", automationMode: "Automatic",
            markEnteredCellKnown: true, playerGrid: "Visible", terrainMode: "HiddenUntilKnown" }],
        createFeature: async (_id, payload) => {
            if (state.fail) throw new Error("Save failed");
            state.current = { ...state.current, version: 2, features: [{ ...payload, id: "created" }] };
            return state.current;
        } };
    const root = dom.window.document.getElementById("root");
    dispose = await renderWorldEditor(root, api, "world", () => {});
    return { root, surface, api, state, dom, dispose: cleanup };
}

test("feature Save respects geometry readiness initially and after success/failure/reset", async t => {
    const { root, surface, state, dom } = await editorFixture(t);
    const form = root.querySelector("[data-feature-form]"), save = form.querySelector('button[type="submit"]');
    const set = (name, value) => { const field = form.elements.namedItem(name); field.value = value; field.dispatchEvent(new dom.window.Event("input")); };
    assert.equal(save.disabled, true);
    set("name", "Camp"); set("category", "town"); set("x", "0"); set("y", "0");
    assert.equal(save.disabled, false);
    form.dispatchEvent(new dom.window.Event("submit", { cancelable: true }));
    await tick();
    assert.equal(save.disabled, true);
    const kind = form.elements.namedItem("kind");
    kind.value = "Line"; kind.dispatchEvent(new dom.window.Event("change"));
    root.querySelector("[data-author-geometry]").click();
    surface.onWorldClick({ x: 0, y: 0 });
    assert.equal(save.disabled, true);
    surface.onWorldClick({ x: 1, y: 1 });
    set("name", "Road"); set("category", "road");
    state.fail = true;
    form.dispatchEvent(new dom.window.Event("submit", { cancelable: true }));
    assert.equal(save.disabled, true);
    await tick();
    assert.equal(save.disabled, false); // Valid geometry remains retryable after failure.
    assert.equal(state.current.features[0].name, "Camp");
    root.querySelector("[data-clear-geometry]").click();
    assert.equal(save.disabled, true);
});

test("map maker shares nonmodal tools and preserves drafts, geometry, file inputs, and viewport", async t => {
    const { root, surface, dom, dispose } = await editorFixture(t);
    const open = key => {
        const button = root.querySelector(`[data-world-tool-button="${key}"]`);
        button.focus(); button.click();
    };
    const form = root.querySelector("[data-feature-form]");
    const sourceFiles = root.querySelector('[data-source-map-upload] input[type="file"]');
    const registration = root.querySelector('[data-registration-panel]');
    open("features");
    form.elements.namedItem("name").value = "Unfinished road";
    form.elements.namedItem("category").value = "road";
    form.elements.namedItem("kind").value = "Line";
    form.elements.namedItem("kind").dispatchEvent(new dom.window.Event("change"));
    root.querySelector('[data-author-geometry]').click();
    surface.onWorldClick({x:0,y:0}); surface.onWorldClick({x:1,y:1});
    const draftText = root.querySelector('[data-draft]').textContent;
    open("appearance");
    root.querySelector('[data-palette-form] input[name="category"]').value = "Unfinished palette";
    const dialog = root.querySelector('[role="dialog"]');
    assert.equal(dialog.getAttribute('aria-modal'), 'false');
    assert.equal(dialog.classList.contains('hc-map-tool-panel'), true);
    assert.ok(root.querySelector('.hc-map-tool-frame').contains(dialog));
    assert.equal(dialog.contains(surface.canvas), false);
    const center = {...surface.viewport.center};
    surface.canvas.dispatchEvent(new dom.window.KeyboardEvent("keydown", {key:"ArrowRight", bubbles:true}));
    const panned = {...surface.viewport.center};
    assert.notDeepEqual(panned, center);
    open("sources");
    Object.defineProperty(sourceFiles, 'files', { configurable:true,
        value:[new dom.window.File(['fixture'], 'pending-map.png', {type:'image/png'})] });
    sourceFiles.dispatchEvent(new dom.window.Event('change'));
    open("features");
    assert.equal(root.querySelector('[data-feature-form]'), form);
    assert.equal(form.elements.namedItem('name').value, 'Unfinished road');
    assert.equal(root.querySelector('[data-draft]').textContent, draftText);
    assert.equal(form.querySelector('button[type="submit"]').disabled, false);
    open("appearance");
    assert.equal(root.querySelector('[data-palette-form] input[name="category"]').value, 'Unfinished palette');
    open("sources");
    assert.equal(root.querySelector('[data-source-map-upload] input[type="file"]'), sourceFiles);
    assert.equal(sourceFiles.files[0].name, 'pending-map.png');
    assert.equal(root.querySelector('[data-registration-panel]'), registration);
    assert.deepEqual(surface.viewport.center, panned);
    assert.equal(root.querySelectorAll('[role="dialog"]').length, 1);
    root.querySelector('[role="dialog"]').dispatchEvent(new dom.window.KeyboardEvent('keydown', {key:'Escape',bubbles:true}));
    assert.equal(root.querySelector('[role="dialog"]'), null);
    assert.equal(document.activeElement, root.querySelector('[data-world-tool-button="sources"]'));
    open("features");
    assert.equal(form.elements.namedItem('name').value, 'Unfinished road');
    dispose();
    assert.equal(root.querySelector('[role="dialog"]'), null);
    assert.ok(root.querySelector('[data-world-tool-dock]').contains(form));
});

test("selected-cell shortcuts open the existing editors and export keeps its settings", async t => {
    const initial = world();
    initial.features = [{id:'camp',name:'Camp',category:'camp',kind:'Point',position:{x:0,y:0}}];
    const { root, surface, dom } = await editorFixture(t, initial);
    const open = key => root.querySelector(`[data-world-tool-button="${key}"]`).click();
    const cell = {id:{tilingId:'tiling',address:{motifCellId:'square',translation:{u:0,v:0}}},
        center:{x:0,y:0},polygon:[{x:-1,y:-1},{x:1,y:-1},{x:1,y:1},{x:-1,y:1}]};
    surface.canvas.focus();
    surface.cellSelectionHandler(cell);
    assert.equal(root.querySelector('[role="dialog"] h2').textContent, 'Selected cell');
    assert.equal(document.activeElement, surface.canvas);
    const edit = [...root.querySelectorAll('[data-selected-cell-panel] button')].find(b=>b.textContent==='Edit feature');
    edit.click();
    assert.equal(root.querySelector('[role="dialog"] h2').textContent, 'Spatial features');
    assert.equal(root.querySelector('[data-feature-form] input[name="name"]').value, 'Camp');
    open('export');
    const form = root.querySelector('[data-export-form]');
    const set = (name,value) => {const field=form.elements.namedItem(name);field.value=value;field.dispatchEvent(new dom.window.Event('change'));};
    set('scope','full'); set('resolution','3200'); set('background','transparent');
    form.elements.namedItem('grid').checked=false;
    open('world'); open('export');
    assert.equal(form.elements.namedItem('scope').value, 'full');
    assert.equal(form.elements.namedItem('resolution').value, '3200');
    const exports=[];
    t.mock.method(surface, 'exportFullMapPng', async (...args)=>exports.push(args));
    form.dispatchEvent(new dom.window.Event('submit',{cancelable:true}));
    await tick();
    assert.deepEqual(exports[0].slice(0,5), ['Fixture','transparent',3200,false,true]);
    assert.equal(document.querySelectorAll('#hex-crawl-map-tool-styles').length, 1);
});
