import assert from "node:assert/strict";
import test from "node:test";
import { RenderLifecycle } from "../.test-dist/render-lifecycle.js";

test("render requests coalesce into one explicit pass", () => {
    const scheduled = [];
    const lifecycle = new RenderLifecycle(callback => { scheduled.push(callback); return 1; });
    let runs = 0;
    lifecycle.register("map", () => { runs++; });
    lifecycle.requestRender();
    lifecycle.requestRender();
    assert.equal(scheduled.length, 1);
    scheduled[0](0);
    assert.equal(runs, 1);
});
