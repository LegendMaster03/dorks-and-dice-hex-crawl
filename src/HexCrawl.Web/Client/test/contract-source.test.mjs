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


test("mapless tracker only loads an Overworld after the full-map world guard", () => {
    const source = fs.readFileSync(path.join(sourceDir, "expedition-view.ts"), "utf8");
    assert.match(source, /showMap && runtime\.overworldId === null/);
    assert.match(source, /showMap \? await api\.getOverworld\(runtime\.overworldId!\) : null/);
});

test("focused assistants never load an Overworld or construct a map surface", () => {
    const source = fs.readFileSync(path.join(sourceDir, "expedition-assistant-view.ts"), "utf8");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("MapSurface"), false);
    assert.match(source, /recordTravelAssistant/);
    assert.match(source, /recordWatchAssistant/);
    assert.match(source, /recordNavigationAssistant/);
    assert.match(source, /recordEncounterAssistant/);
});


test("mapless session creation never creates a placeholder Overworld", () => {
    const source = fs.readFileSync(path.join(sourceDir, "tool-home-view.ts"), "utf8");
    assert.equal(source.includes("api.createOverworld("), false);
    assert.match(source, /api\.startStandaloneSession\(request\)/);
    assert.match(source, /kind: "AbstractHex"/);
    assert.match(source, /kind: "NonSpatial"/);
});

test("standalone crawl sessions post directly to the expedition collection", () => {
    const source = fs.readFileSync(path.join(sourceDir, "api.ts"), "utf8");
    assert.match(source, /startStandaloneSession/);
    assert.match(source, /sendJson\("POST", "\/api\/expeditions"/);
});


test("non-spatial watch bookkeeping uses a dedicated non-spatial API", () => {
    const api = fs.readFileSync(path.join(sourceDir, "api.ts"), "utf8");
    const view = fs.readFileSync(path.join(sourceDir, "expedition-assistant-view.ts"), "utf8");
    assert.match(api, /assistants\/watch/);
    assert.match(view, /recordWatchAssistant/);
    assert.match(view, /Watch \/ time bookkeeping/);
    assert.equal(/recordWatchAssistant\([^)]*resultingHex/.test(view), false);
});


test("direct assistant entry does not create or fetch an Overworld", () => {
    const source = fs.readFileSync(path.join(sourceDir, "assistant-entry-view.ts"), "utf8");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("createOverworld"), false);
    assert.match(source, /startStandaloneSession/);
    assert.match(source, /kind: "NonSpatial"/);
    assert.match(source, /kind: "AbstractHex"/);
});

test("dashboard prominently exposes all top-level assistant entry routes", () => {
    const source = fs.readFileSync(path.join(sourceDir, "tool-home-view.ts"), "utf8");
    assert.match(source, /\/assistants\/travel/);
    assert.match(source, /\/assistants\/navigation/);
    assert.match(source, /\/assistants\/encounters/);
});


test("hosted anonymous access renders a public shell before owner-scoped routes run", () => {
    const source = fs.readFileSync(path.join(sourceDir, "app.ts"), "utf8");
    assert.match(source, /context !== null && context\.user == null/);
    assert.match(source, /if \(hostedAnonymous\) \{\s*renderAnonymousAccess\(rootElement, route\.kind !== "home"\);\s*return;/s);
    assert.match(source, /Anonymous access does not create a shared or placeholder owner/);
});

test("Hex Crawl follows the Dorks & Dice theme and standalone system preference", () => {
    const source = fs.readFileSync(path.join(sourceDir, "styles.ts"), "utf8");
    assert.match(source, /color-scheme: light/);
    assert.match(source, /html\[data-bs-theme="dark"\] #tool-root\.hex-crawl-app/);
    assert.match(source, /@media \(prefers-color-scheme: dark\)/);
    assert.match(source, /html:not\(\[data-bs-theme\]\) #tool-root\.hex-crawl-app/);
    assert.equal(source.includes("MutationObserver"), false);
});
