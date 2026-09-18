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


test("mapless tracker only loads an Overworld in full-map mode", () => {
    const source = fs.readFileSync(path.join(sourceDir, "expedition-view.ts"), "utf8");
    assert.match(source, /showMap \? await api\.getOverworld\(runtime\.overworldId\) : null/);
});

test("focused assistants never load an Overworld or construct a map surface", () => {
    const source = fs.readFileSync(path.join(sourceDir, "expedition-assistant-view.ts"), "utf8");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("MapSurface"), false);
    assert.match(source, /recordTravelAssistant/);
    assert.match(source, /recordNavigationAssistant/);
    assert.match(source, /recordEncounterAssistant/);
});
