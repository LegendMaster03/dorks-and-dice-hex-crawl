import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceDir = path.resolve("src");

test("application-owned DOM does not use MutationObserver", () => {
    const files = fs.readdirSync(sourceDir).filter(name => name.endsWith(".ts"));
    const source = files.map(name => fs.readFileSync(path.join(sourceDir, name), "utf8")).join("\n");
    assert.equal(source.includes("MutationObserver"), false);
});
