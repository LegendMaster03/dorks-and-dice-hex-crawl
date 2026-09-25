import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceDir = path.resolve("src");

test("application-owned DOM does not use MutationObserver", () => {
    const pending = [sourceDir];
    const sources = [];
    while (pending.length > 0) {
        const directory = pending.pop();
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            const location = path.join(directory, entry.name);
            if (entry.isDirectory()) {
                pending.push(location);
            } else if (entry.name.endsWith(".ts")) {
                sources.push(fs.readFileSync(location, "utf8"));
            }
        }
    }
    assert.equal(sources.join("\n").includes("MutationObserver"), false);
});


test("mapless tracker only loads an Overworld after the full-map world guard", () => {
    const source = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    assert.match(source, /showMap && runtime\.overworldId === null/);
    assert.match(source, /showMap \? await api\.getOverworld\(runtime\.overworldId!\) : null/);
});

test("focused assistants never load an Overworld or construct a map surface", () => {
    const source = fs.readFileSync(path.join(sourceDir, "modules/assistants/expedition-assistant-view.ts"), "utf8");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("MapSurface"), false);
    assert.match(source, /recordTravelAssistant/);
    assert.match(source, /recordWatchAssistant/);
    assert.match(source, /recordNavigationAssistant/);
    assert.match(source, /recordEncounterAssistant/);
});


test("mapless session creation never creates a placeholder Overworld", () => {
    const source = fs.readFileSync(path.join(sourceDir, "modules/home/tool-home-view.ts"), "utf8");
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
    const view = fs.readFileSync(path.join(sourceDir, "modules/assistants/expedition-assistant-view.ts"), "utf8");
    assert.match(api, /assistants\/watch/);
    assert.match(view, /recordWatchAssistant/);
    assert.match(view, /Watch \/ time bookkeeping/);
    assert.equal(/recordWatchAssistant\([^)]*resultingHex/.test(view), false);
});


test("direct assistant entry does not create or fetch an Overworld", () => {
    const source = fs.readFileSync(path.join(sourceDir, "modules/assistants/assistant-entry-view.ts"), "utf8");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("createOverworld"), false);
    assert.match(source, /startStandaloneSession/);
    assert.match(source, /kind: "NonSpatial"/);
    assert.match(source, /kind: "AbstractHex"/);
});

test("dashboard prominently exposes all top-level assistant entry routes", () => {
    const source = fs.readFileSync(path.join(sourceDir, "modules/home/tool-home-view.ts"), "utf8");
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


test("home dashboard sections share the same three-column grid", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/home/tool-home-view.ts"), "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "styles.ts"), "utf8");
    assert.equal((view.match(/hc-home-three-column-grid/g) ?? []).length, 3);
    assert.match(view, /hc-columns hc-home-three-column-grid hc-home-main-grid/);
    assert.match(styles, /\.hc-home-three-column-grid \{ grid-template-columns: repeat\(3, minmax\(0, 1fr\)\); gap: \.65rem; \}/);
    assert.match(styles, /\.hc-home-main-grid > :first-child \{ grid-column: span 2; \}/);
    assert.match(styles, /\.hc-home-main-grid > :first-child \{ grid-column: auto; \}/);
});


test("world editor removes nested control scrolling without changing narrow-layout flow", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/worlds/world-editor-view.ts"), "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "styles.ts"), "utf8");
    assert.match(view, /hc-world-editor/);
    assert.match(styles, /\.hc-world-editor \.hc-sidebar \{ max-height: none; overflow: visible;/);
    assert.match(styles, /\.hc-world-editor \.hc-map-panel \{ position: sticky; top: \.75rem; \}/);
    assert.match(styles, /@media \(max-width: 820px\)[\s\S]*\.hc-world-editor \.hc-map-panel \{ position: static; \}/);
});

test("interactive canvas exposes a named keyboard interaction surface and selected hex state", () => {
    const surface = fs.readFileSync(path.join(sourceDir, "map-surface.ts"), "utf8");
    assert.match(surface, /setAttribute\("role", "region"\)/);
    assert.match(surface, /aria-describedby/);
    assert.match(surface, /aria-keyshortcuts/);
    assert.match(surface, /ArrowUp/);
    assert.match(surface, /event\.key === "Enter" \|\| event\.key === " "/);
    assert.match(surface, /worldToHex/);
    assert.match(surface, /renderer\.selectedHex = selected/);
    assert.match(surface, /Current expedition hex q/);
    assert.match(surface, /Selected hex q/);
});

test("DM-facing world authoring copy explains empty states and keeps deeper map terminology secondary", () => {
    const world = fs.readFileSync(path.join(sourceDir, "modules/worlds/world-editor-view.ts"), "utf8");
    const maps = [
        fs.readFileSync(path.join(sourceDir, "modules/worlds/source-map-workspace.ts"), "utf8"),
        fs.readFileSync(path.join(sourceDir, "modules/worlds/source-map-registration-controller.ts"), "utf8"),
        fs.readFileSync(path.join(sourceDir, "modules/worlds/wonderdraft-import-controller.ts"), "utf8")
    ].join("\n");
    assert.match(world, /World map editor/);
    assert.match(world, /No locations yet/);
    assert.match(world, /No map features yet/);
    assert.match(world, /No expeditions yet/);
    assert.match(maps, /<summary>Reference maps<\/summary>/);
    assert.match(maps, /No reference maps yet/);
    assert.match(maps, /Internally, these are stored as source-map representations/);
    assert.doesNotMatch(maps, /Semantic locations and features remain independent world truth/);
});

test("direction controls retain numeric values but present axial step labels", () => {
    const runtime = fs.readFileSync(path.join(sourceDir, "runtime-view.ts"), "utf8");
    const expedition = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const watch = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"), "utf8");
    const assistant = fs.readFileSync(path.join(sourceDir, "modules/assistants/expedition-assistant-view.ts"), "utf8");
    assert.match(runtime, /"Toward \+q"/);
    assert.match(runtime, /"Toward -q"/);
    assert.match(expedition, /<option value="\$\{value\}">\$\{directionLabel\(value\)\}<\/option>/);
    assert.match(assistant, /\$\{directionLabel\(value\)\}<\/option>/);
    assert.match(watch, /persisted runtime values remain 0–5/);
});


test("expedition workbench keeps presentation separate from mutation orchestration", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const presentation = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-presentation.ts"), "utf8");
    assert.match(view, /renderExpeditionStatus/);
    assert.match(view, /renderExpeditionSnapshots/);
    assert.match(presentation, /renderExpeditionHistory/);
    assert.match(presentation, /renderPlayerKnowledgePreview/);
    assert.doesNotMatch(presentation, /advanceExpedition/);
    assert.doesNotMatch(presentation, /api\.discover/);
});


test("expedition watch controller owns watch form policy and mutation submission", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const controller = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"), "utf8");
    assert.match(view, /ExpeditionWatchController/);
    assert.doesNotMatch(view, /api\.advanceExpedition/);
    assert.match(controller, /api\.advanceExpedition/);
    assert.match(controller, /navigationResolutionDue/);
    assert.match(controller, /encounterCheckDue/);
    assert.match(controller, /persisted runtime values remain 0–5/);
});


test("running sheet exposes the persisted party register through a dedicated controller", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const party = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-party-sheet.ts"), "utf8");
    assert.match(view, /ExpeditionPartySheetController/);
    assert.match(view, /data-party-summary/);
    assert.match(view, /data-party-editor/);
    assert.match(party, /updateExpeditionParty/);
    assert.match(party, /Marching order/);
    assert.match(party, /Watch list/);
    assert.match(party, /Standing orders/);
    assert.match(party, /Per hour/);
    assert.match(party, /Per watch/);
    assert.match(party, /Per march/);
    assert.match(party, /runtime\.context\.hexCenterDistance\?\.unit/);
});

test("watch planning uses travel-duty terminology without changing persisted activity input", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    assert.match(view, /Travel duties \/ activities/);
    assert.match(view, /name="activities"/);
    assert.match(view, /navigate, forage, map, scout/);
});


test("party movement editor derives units from persisted session data without a mile fallback", () => {
    const party = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-party-sheet.ts"), "utf8");
    assert.match(party, /runtime\.context\.hexCenterDistance\?\.unit/);
    assert.match(party, /runtime\.expedition\.distanceTraveled\.unit/);
    assert.doesNotMatch(party, /\?\? \{ kind: "Mile", symbol: "mi", metersPerUnit: 1609\.344 \}/);
    assert.match(party, /custom movement unit conversion must be a finite positive number/i);
});


test("watch form does not invent unresolved travel results", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const controller = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"), "utf8");
    assert.doesNotMatch(view, /name="hexSteps"[^>]*value="1"/);
    assert.doesNotMatch(controller, /actualDistance"\)\.value = String\(scale\)/);
    assert.match(controller, /suggestedWatchDistance\(runtime\)/);
});


test("wide world-bound workbench places map beside current sheet while ledger remains full-width", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "styles.ts"), "utf8");
    assert.match(view, /hc-map-sheet-top/);
    assert.match(view, /\$\{ledgerMarkup\}[\s\S]*<\/section>/);
    assert.match(styles, /@media \(min-width: 1350px\)[\s\S]*\.hc-workspace-grid \.hc-map-sheet-top[\s\S]*grid-template-columns:/);
    assert.match(styles, /\.hc-map-panel > \.hc-running-ledger-panel/);
    assert.match(styles, /@media \(max-width: 820px\)[\s\S]*\.hc-workspace-grid[\s\S]*grid-template-columns: 1fr/);
});
