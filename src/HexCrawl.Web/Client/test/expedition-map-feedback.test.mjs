import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { registerHooks, stripTypeScriptTypes } from "node:module";
import { JSDOM } from "jsdom";

// Execute the actual workbench and DOM. Stub only the canvas and independently
// tested gameplay services; their persistent editor nodes still move normally.
registerHooks({
    resolve(specifier, context, next) {
        if (specifier.startsWith(".") && context.parentURL) {
            const candidate = new URL(specifier.endsWith(".js") ? specifier.slice(0, -3) + ".ts"
                : specifier.endsWith(".ts") ? specifier : specifier + ".ts", context.parentURL);
            if (existsSync(candidate)) return { url: candidate.href, shortCircuit: true };
        }
        return next(specifier, context);
    },
    load(url, context, next) {
        if (url.endsWith("/map-surface.ts")) return { format: "module", shortCircuit: true,
            source: "export const MapSurface = globalThis.mapFixtureClass;" };
        if (url.endsWith("/periodic-gameplay-panels.ts")) return { format: "module", shortCircuit: true,
            source: "export const mountPeriodicGameplayPanels = (...args) => globalThis.mountGameplayFixture(...args);" };
        if (url.endsWith(".ts")) return { format: "module", shortCircuit: true,
            source: stripTypeScriptTypes(readFileSync(new URL(url), "utf8"), { mode: "transform" }) };
        return next(url, context);
    }
});

let surface;
globalThis.mapFixtureClass = class {
    renderer = {};
    terrains = [];
    constructor(host, getWorld) {
        surface = this;
        this.host = host;
        this.getWorld = getWorld;
        const map = document.createElement("button");
        map.textContent = "Map interaction";
        map.onclick = () => { this.interactions = (this.interactions ?? 0) + 1; };
        host.append(map);
    }
    centerOn() {}
    setCellSelectionHandler(handler) { this.select = handler; }
    requestRender() {}
    setTerrainAnnotations(annotations) { this.terrains.push(annotations); }
    dispose() { this.disposed = true; }
};
let onWorldLoaded;
globalThis.mountGameplayFixture = (host, _api, _id, _context, _runtime, _apply, _mutate, _error, _attention, loaded) => {
    onWorldLoaded = loaded;
    for (const [attribute, name] of [
        ["data-periodic-party", "Party"], ["class", "hc-environment-panel"],
        ["data-survival-resources-panel", "Resources"], ["data-effects-panel", "Effects"],
        ["data-journey-panel", "Journey"]
    ]) {
        const panel = document.createElement("details");
        panel.setAttribute(attribute, attribute === "class" ? name : "");
        panel.innerHTML = `<summary>${name}</summary><input aria-label="Draft ${name}">`;
        host.append(panel);
    }
    return { sync() {}, dispose() { host.replaceChildren(); } };
};
const { renderPeriodicExpeditionWorkbench } = await import("../src/modules/expeditions/periodic-expedition-workbench.ts");
const { ExpeditionEnvironmentPanel } = await import("../src/modules/expeditions/environment-panel.ts");

function dom(t) {
    const document = new JSDOM('<div id="tool-root" class="hc-phase15"></div>', { url: "https://example.test/" });
    const names = ["window", "document", "HTMLElement", "HTMLDetailsElement", "HTMLButtonElement", "HTMLInputElement", "CustomEvent", "localStorage"];
    const previous = new Map(names.map(name => [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
    for (const name of names) Object.defineProperty(globalThis, name, { configurable: true, value: document.window[name] });
    t.after(() => {
        document.window.close();
        for (const [name, descriptor] of previous) {
            if (descriptor) Object.defineProperty(globalThis, name, descriptor);
            else delete globalThis[name];
        }
    });
    return document.window.document.querySelector("#tool-root");
}
function fixtures() {
    const id = { tilingId: "tiling", address: { motifCellId: "triangle-1", translation: { u: 21, v: -3 } } };
    const topology = { formatVersion: 1, currentCell: id, position: { x: 0, y: 0 }, center: { x: 0, y: 0 },
        polygon: [{ x: -1, y: -1 }, { x: 1, y: -1 }, { x: 0, y: 1 }], interfaces: [], selectedExitInterfaceIndex: null };
    const world = { id: "world", version: 1, name: "Fixture", features: [], locations: [], sourceMaps: [], tiling: { id: "tiling", revision: 1 } };
    const runtime = { id: "expedition", version: 1, name: "Fixture expedition", overworldId: "world", knowledge: [],
        context: { name: "Fixture context" }, procedure: { name: "Custom", modules: [], runtime: { travelResolution: "CellSteps" } },
        party: { members: [], activityAssignments: [] },
        movementComposition: { status: "Resolved", effectiveValue: null, contributors: [], missingInputs: [],
            provenance: [], diagnostics: [], policy: { travelModeKeys: [] } },
        history: [{ message: "DM repositioned to cell WorldCellId { TilingId = 0da1c371-d22f-4b5a-949b-697f412e0552, Address = triangle-1@21,-3 } without recording travel." }],
        expedition: { isSpatial: true, currentCell: id, position: topology.position, currentDay: 1,
            completedWatches: 0, activeWatchNumber: null, pendingEncounter: null, intendedHeading: null } };
    return { world, runtime, topology };
}

test("GM tools preserve editor drafts and map interaction across switches and close", async t => {
    const root = dom(t), { world, runtime, topology } = fixtures();
    const dispose = await renderPeriodicExpeditionWorkbench(root, { getCellTopology: async () => topology }, runtime, world, () => {});
    t.after(dispose);
    const click = selector => { const control = root.querySelector(selector); control.focus(); control.click(); };
    const host = root.querySelector("[data-gameplay-panels]");
    const input = host.querySelector("input");
    click("[data-gm-party]");
    assert.equal(root.querySelector('[role="dialog"]').getAttribute("aria-modal"), "false");
    assert.ok(root.querySelector('[role="dialog"]').contains(input));
    assert.ok(root.querySelector('.hc-map-frame').contains(root.querySelector('[role="dialog"]')));
    assert.equal(root.querySelector('[role="dialog"]').contains(root.querySelector('[aria-label="GM map tools"]')), false);
    input.value = "Unfinished party edit";
    click("[data-gm-environment]");
    assert.equal(root.querySelectorAll('[role="dialog"]').length, 1);
    assert.equal(input.closest("details").hidden, true);
    click("[data-map] button");
    assert.equal(surface.interactions, 1);
    click("[data-gm-party]");
    assert.equal(host.querySelector("input"), input);
    assert.equal(input.value, "Unfinished party edit");
    click("[data-gm-history]");
    assert.match(root.querySelector('[role="dialog"]').textContent, /cell triangle-1@21,-3/);
    assert.doesNotMatch(root.querySelector('[role="dialog"]').textContent, /WorldCellId|TilingId/);
    click("[data-gm-overview]");
    assert.ok(root.querySelector('[role="dialog"] [data-status]'));
    click("[data-gm-cell]");
    assert.ok(root.querySelector('[role="dialog"] [data-cell-details]'));
    surface.select({ id: topology.currentCell, center: topology.center, polygon: topology.polygon });
    assert.match(root.querySelector('[role="dialog"] [data-cell-details]').textContent, /Cell triangle-1 \(21, -3\)/);
    click("[data-gm-travel]");
    assert.ok(root.querySelector('[role="dialog"] [data-travel-form]'));
    root.querySelector('[role="dialog"]').dispatchEvent(new window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    assert.equal(root.querySelector('[role="dialog"]'), null);
    assert.ok(root.querySelector("[data-travel-staging]").contains(root.querySelector("[data-travel-content]")));
    assert.equal(document.activeElement, root.querySelector("[data-gm-travel]"));
    click("[data-gm-party]");
    dispose();
    assert.equal(root.querySelector('[role="dialog"]'), null);
    assert.equal(surface.disposed, true);
});

test("GM map consumes authoritative terrain updates, ignores stale worlds and does not recenter", async t => {
    const root = dom(t), { world, runtime, topology } = fixtures();
    let recentered = 0;
    const dispose = await renderPeriodicExpeditionWorkbench(root, { getCellTopology: async () => topology }, runtime, world, () => {});
    t.after(dispose);
    surface.centerOn = () => recentered++;
    const annotations = [{ id: "forest" }];
    const nextWorld = { ...world, version: 2, mapAppearance: { terrainColors: { forest: "#22AA44" } } };
    onWorldLoaded(nextWorld, { version: 2, annotations });
    assert.equal(surface.getWorld(), nextWorld);
    assert.deepEqual(surface.terrains, [annotations]);
    onWorldLoaded(world, { version: 1, annotations: [] });
    assert.equal(surface.getWorld(), nextWorld);
    assert.equal(surface.terrains.length, 1);
    onWorldLoaded({ ...world, version: 3 }, { version: 2, annotations: [] });
    assert.equal(surface.terrains.length, 1);
    assert.match(root.querySelector('[data-error]').textContent, /world changed/i);
    onWorldLoaded({ ...world, version: 3 }, { version: 3, annotations: [] });
    assert.deepEqual(surface.terrains.at(-1), []);
    assert.equal(recentered, 0);
    dispose();
    onWorldLoaded({ ...world, version: 4 }, { version: 4, annotations: [{}] });
    assert.equal(surface.terrains.length, 2);
});

test("real environment loader publishes initial and changed terrain, drops late responses", async t => {
    dom(t);
    let resolve;
    const pending = new Promise(yes => { resolve = yes; });
    const loaded = [], fixture = fixtures();
    const panel = Object.create(ExpeditionEnvironmentPanel.prototype);
    Object.assign(panel, { disposed: false, refreshToken: 0, body: document.createElement("div"),
        getRuntime: () => fixture.runtime, render() {}, onWorldLoaded: (...args) => loaded.push(args),
        api: { getExpeditionEnvironment: async () => ({}), getOverworld: async () => fixture.world,
            getWorldEnvironment: () => pending } });
    const old = panel.refresh();
    panel.api.getWorldEnvironment = async () => ({ version: 2, annotations: [] });
    fixture.world = { ...fixture.world, version: 2 };
    await panel.refresh();
    resolve({ version: 1, annotations: [{ id: "obsolete" }] });
    await old;
    assert.equal(loaded.length, 1);
    assert.equal(loaded[0][0].version, 2);
    assert.deepEqual(loaded[0][1].annotations, []);
    panel.disposed = true;
    await panel.refresh();
    assert.equal(loaded.length, 1);
});
