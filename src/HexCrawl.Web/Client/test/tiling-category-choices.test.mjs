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

const { tilingCategoryChoices } = await import(
    "../.test-dist/modules/procedures/tiling-category-choices.js");

function fixture(name, categoryPath, id, symbol = id) {
    return {
        dsSymbol: symbol, presetId: id, categoryPath, displayName: name,
        showInRulesetSelector: true, supportsVertexOnlyAdjacency: false,
        realization: null
    };
}

test("nested tiling choices reuse preview controls and invoke the selected catalog entry", t => {
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

    const entries = [
        fixture("Type 2", ["Pentagons", "Convex"], "pentagon-2"),
        fixture("Square", ["Squares"], "square"),
        fixture("Type 1", ["Pentagons", "Convex"], "pentagon-1")
    ];
    let picked = null;
    const choices = tilingCategoryChoices(entries, "square", entry => { picked = entry; });
    dom.window.document.body.append(choices);
    assert.deepEqual([...choices.querySelectorAll("summary")].map(node => node.textContent),
        ["Pentagons", "Squares"]);
    // Do not draw hidden polygon patches until the category is expanded.
    assert.equal(choices.querySelectorAll("canvas[role='img']").length, 1);
    const squareButton = [...choices.querySelectorAll("button")]
        .find(button => button.textContent.includes("Square"));
    assert.equal(squareButton.getAttribute("aria-pressed"), "true");
    assert.equal(choices.querySelectorAll("details[open]").length, 1);
    const pentagons = choices.querySelector("details");
    pentagons.open = true;
    pentagons.dispatchEvent(new dom.window.Event("toggle"));
    const convex = pentagons.querySelector("details");
    convex.open = true;
    convex.dispatchEvent(new dom.window.Event("toggle"));
    assert.equal(choices.querySelectorAll("canvas[role='img']").length, 3);
    const type2 = [...choices.querySelectorAll("button")]
        .find(button => button.textContent.includes("Type 2"));
    type2.focus();
    type2.click();
    assert.equal(picked, entries[0]);
    assert.equal(dom.window.document.activeElement, type2,
        "switching a preview must not discard keyboard focus");
    assert.equal(type2.getAttribute("aria-pressed"), "true");
    assert.equal(squareButton.getAttribute("aria-pressed"), "false");
    assert.equal(pentagons.open, true);
    assert.equal(convex.open, true);
});

test("catalog preset IDs distinguish entries sharing a D-symbol", t => {
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

    const entries = [
        fixture("Type 1", ["Pentagons"], "pentagon-1", "shared"),
        fixture("Type 2", ["Pentagons"], "pentagon-2", "shared")
    ];
    const choices = tilingCategoryChoices(entries, "shared", () => {}, true, "pentagon-2");
    const buttons = [...choices.querySelectorAll("button")];
    assert.deepEqual(buttons.map(button => button.getAttribute("aria-pressed")), ["false", "true"]);
    assert.ok(buttons.every(button => button.disabled));
});

test("ambiguous stored topologies expand their category without selecting a metric", t => {
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

    const entries = [
        fixture("Equilateral Type 2", ["Pentagons", "Convex"], "pentagon-2", "shared"),
        fixture("Cairo Type 4", ["Pentagons", "Convex"], "pentagon-4", "shared"),
        fixture("Square", ["Squares"], "square", "square")
    ];
    const choices = tilingCategoryChoices(entries, "shared", () => {});
    const pentagons = choices.querySelector("details");
    const convex = pentagons.querySelector("details");
    assert.equal(pentagons.open, true);
    assert.equal(convex.open, true);
    const buttons = [...choices.querySelectorAll("button")];
    assert.equal(buttons.length, 2);
    assert.ok(buttons.every(button => button.getAttribute("aria-pressed") === "false"));

    const exact = tilingCategoryChoices(entries, "shared", () => {}, false, "pentagon-4");
    const selected = [...exact.querySelectorAll("button")];
    assert.deepEqual(selected.map(button => button.getAttribute("aria-pressed")), ["false", "true"]);
});
