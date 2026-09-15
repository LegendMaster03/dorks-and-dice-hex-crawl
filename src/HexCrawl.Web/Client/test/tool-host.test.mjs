import assert from "node:assert/strict";
import test from "node:test";
import { backendBaseFromContext } from "../.test-dist/api.js";

test("hosted API uses the Tool Host upstream gateway", () => {
    assert.equal(
        backendBaseFromContext("/tool-host/hex-crawl/api/"),
        "/tool-host/hex-crawl/api/upstream");
    assert.equal(backendBaseFromContext(null), "");
});
