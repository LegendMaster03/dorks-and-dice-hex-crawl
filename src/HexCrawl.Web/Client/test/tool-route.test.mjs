import assert from "node:assert/strict";
import test from "node:test";
import { deriveToolRoute } from "../.test-dist/tool-route.js";

test("nested hosted routes follow the current browser pathname", () => {
    assert.equal(deriveToolRoute("/tools/hex-crawl", "/tools/hex-crawl", "/old"), "/");
    assert.equal(deriveToolRoute("/tools/hex-crawl", "/tools/hex-crawl/worlds/demo", "/old"), "/worlds/demo");
    assert.equal(deriveToolRoute("/tools/hex-crawl", "/tools/hex-crawl/expeditions/7", "/old"), "/expeditions/7");
});

test("route derivation does not consume another Tool prefix", () => {
    assert.equal(deriveToolRoute("/tools/hex", "/tools/hex-crawl", "/initial"), "/initial");
});
