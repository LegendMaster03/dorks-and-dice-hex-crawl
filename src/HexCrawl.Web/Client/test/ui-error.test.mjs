import assert from "node:assert/strict";
import test from "node:test";
import { HexCrawlApiError } from "../.test-dist/api.js";
import { describeUiError } from "../.test-dist/ui-error.js";

test("known overworld expedition dependency remains actionable while arbitrary conflicts stay masked", () => {
    const dependency = new HexCrawlApiError(
        409,
        "conflict",
        "Delete overworld failed: The overworld can not be deleted while it has saved expeditions. Remove those expeditions first.");
    const dependencyUi = describeUiError(dependency);
    assert.equal(dependencyUi.kind, "conflict");
    assert.match(dependencyUi.message, /Remove those expeditions first/);

    const arbitrary = new HexCrawlApiError(409, "conflict", "raw internal conflict details");
    const arbitraryUi = describeUiError(arbitrary);
    assert.equal(arbitraryUi.kind, "conflict");
    assert.doesNotMatch(arbitraryUi.message, /raw internal conflict details/);
    assert.match(arbitraryUi.message, /Reload the latest state/);
});
