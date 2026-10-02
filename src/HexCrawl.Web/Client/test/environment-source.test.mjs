import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const source = fs.readFileSync(
    path.resolve("src/modules/expeditions/environment-panel.ts"),
    "utf8");

test("environment measurement form rejects a blank numeric value instead of coercing it to zero", () => {
    assert.match(source, /const numericText = value\("measurement"\);/);
    assert.match(source, /if \(!numericText \|\| !unit\) return null;/);
    assert.match(source, /const numeric = Number\(numericText\);/);
});
