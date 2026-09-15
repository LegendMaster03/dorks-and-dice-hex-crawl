import assert from "node:assert/strict";
import test from "node:test";
import { deriveToolRoute, parseToolRoute, toolRelativeHref } from "../.test-dist/tool-route.js";

test("derives nested hosted tool routes from the Tool Host base path", () => {
    assert.equal(deriveToolRoute("/tools/hex-crawl", "/tools/hex-crawl/worlds/abc/edit", "/"), "/worlds/abc/edit");
    assert.equal(deriveToolRoute("/tools/hex-crawl", "/tools/hex-crawl", "/worlds"), "/");
});

test("standalone routing retains the entire nested path", () => {
    assert.equal(deriveToolRoute("/", "/worlds/abc/expeditions/def", "/"), "/worlds/abc/expeditions/def");
});

test("parses persistent world editor and expedition routes", () => {
    assert.deepEqual(parseToolRoute("/worlds"), { kind: "worlds" });
    assert.deepEqual(parseToolRoute("/worlds/abc/edit"), { kind: "edit", worldId: "abc" });
    assert.deepEqual(parseToolRoute("/worlds/abc/expeditions/def"), { kind: "expedition", worldId: "abc", expeditionId: "def" });
});

test("builds hosted and standalone hrefs without teaching the site internal routes", () => {
    assert.equal(toolRelativeHref("/tools/hex-crawl", "/worlds/abc/edit"), "/tools/hex-crawl/worlds/abc/edit");
    assert.equal(toolRelativeHref("/", "/worlds/abc/edit"), "/worlds/abc/edit");
});
