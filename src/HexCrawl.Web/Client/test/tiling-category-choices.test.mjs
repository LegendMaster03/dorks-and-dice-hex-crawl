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
    const summaries = [...choices.querySelectorAll("summary")].map(node => node.textContent);
    assert.deepEqual(summaries, ["Pentagons", "Convex", "Squares"]);
    const squareButton = [...choices.querySelectorAll("button")]
        .find(button => button.textContent.includes("Square"));
    assert.equal(squareButton.getAttribute("aria-pressed"), "true");
    assert.equal(choices.querySelectorAll("canvas[role='img']").length, 3);
    assert.equal(choices.querySelectorAll("details[open]").length, 1);
    const type2 = [...choices.querySelectorAll("button")]
        .find(button => button.textContent.includes("Type 2"));
    type2.click();
    assert.equal(picked, entries[0]);
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
