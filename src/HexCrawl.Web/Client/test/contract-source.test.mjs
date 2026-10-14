import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceDir = path.resolve("src");
const read = relative => fs.readFileSync(path.join(sourceDir, relative), "utf8");

test("application-owned DOM does not use MutationObserver", () => {
    const pending = [sourceDir];
    const sources = [];
    while (pending.length > 0) {
        const directory = pending.pop();
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            const location = path.join(directory, entry.name);
            if (entry.isDirectory()) pending.push(location);
            else if (entry.name.endsWith(".ts")) sources.push(fs.readFileSync(location, "utf8"));
        }
    }
    assert.equal(sources.join("\n").includes("MutationObserver"), false);
});

test("unified expedition workspace only loads an Overworld when the session has one", () => {
    const source = read("modules/expeditions/expedition-view.ts");
    assert.match(source, /let world: Overworld \| null = runtime\.overworldId \? await api\.getOverworld\(runtime\.overworldId\) : null/);
    assert.match(source, /return renderPeriodicExpeditionWorkbench/);
    assert.doesNotMatch(source, /new MapSurface|setHexSelectionHandler/);
    assert.doesNotMatch(source, /showMap/);
});

test("focused assistants never load an Overworld or construct a map surface", () => {
    const source = read("modules/assistants/expedition-assistant-view.ts");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("MapSurface"), false);
    assert.match(source, /recordWatchAssistant/);
    assert.match(source, /recordEncounterAssistant/);
    assert.doesNotMatch(source, /recordTravelAssistant|recordNavigationAssistant|hexSteps|currentHex|veerSteps/);
});

test("focused assistants require explicit resolved outcomes and provenance", () => {
    const source = read("modules/assistants/expedition-assistant-view.ts");
    assert.match(source, /<option value="">Select resolved outcome<\/option>/);
    assert.match(source, /<select name="outcome" required>/);
    assert.match(source, /<select name="source" data-source required>/);
    assert.match(source, /source\.append\(option\("", "Select result source"\)\)/);
    assert.match(source, /source\.value = mode === "travel" \? "ProcedureDefault" : ""/);
    assert.doesNotMatch(source, /source\.value = "ManualRoll"/);
    assert.match(source, /source\.value = mode === "travel" \? "ProcedureDefault" : ""/);
});

test("mapless session creation never creates a placeholder Overworld", () => {
    const source = read("modules/home/tool-home-view.ts");
    assert.equal(source.includes("api.createOverworld("), false);
    assert.match(source, /api\.startStandaloneSession/);
    assert.match(source, /applyStandaloneProcedureChoice/);
    assert.match(source, /kind: "MaplessSpatial"/);
    assert.match(source, /kind: "NonSpatial"/);
});

test("crawl creation surfaces do not assume a 12-mile physical scale", () => {
    const world = read("modules/worlds/world-list-view.ts");
    const home = read("modules/home/tool-home-view.ts");
    const assistant = read("modules/assistants/assistant-entry-view.ts");
    const editor = read("modules/worlds/world-editor-view.ts");
    for (const source of [home, assistant]) {
        assert.doesNotMatch(source, /name="scale"[^>]*value="12"/);
        assert.match(source, /Select unit/);
    }
    assert.match(world, /name="physicalScale"[^>]*placeholder="Optional for cell steps"/);
    assert.match(world, /api\.createPeriodicOverworld/);
    assert.doesNotMatch(world, /api\.createOverworld\(|<option value="square"|<option value="triangle"/);
    for (const source of [world, home, assistant, editor]) {
        assert.doesNotMatch(source, /name="(?:symbol|unitSymbol)"[^>]*value="u"/);
        assert.doesNotMatch(source, /name="(?:meters|metersPerUnit)"[^>]*value="1"/);
    }
    assert.match(world, /Physical map scale/);
    assert.match(home, /unit\.required = false/);
    assert.match(assistant, /unit\.required = false/);
    assert.match(home, /Boolean\(scaleText\) !== Boolean\(unitKind\)/);
    assert.match(assistant, /Boolean\(scaleText\) !== Boolean\(unitKind\)/);
    assert.match(world, /Boolean\(scaleText\) !== Boolean\(unit\)/);
});

test("standalone crawl sessions post directly to the expedition collection", () => {
    const source = read("api.ts");
    assert.match(source, /startStandaloneSession/);
    assert.match(source, /sendJson\("POST", "\/api\/expeditions"/);
});

test("non-spatial watch bookkeeping uses a dedicated non-spatial API", () => {
    const api = read("api.ts");
    const view = read("modules/assistants/expedition-assistant-view.ts");
    assert.match(api, /assistants\/watch/);
    assert.match(view, /recordWatchAssistant/);
    assert.match(view, /Time \/ interval bookkeeping/);
    assert.equal(/recordWatchAssistant\([^)]*resultingHex/.test(view), false);
});

test("direct assistant entry does not create or fetch an Overworld", () => {
    const source = read("modules/assistants/assistant-entry-view.ts");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("createOverworld"), false);
    assert.match(source, /startStandaloneSession/);
    assert.match(source, /kind: "NonSpatial"/);
    assert.match(source, /kind: "MaplessSpatial"/);
});

test("dashboard prominently exposes all top-level assistant entry routes", () => {
    const source = read("modules/home/tool-home-view.ts");
    assert.match(source, /\/assistants\/travel/);
    assert.match(source, /\/assistants\/navigation/);
    assert.match(source, /\/assistants\/encounters/);
});

test("hosted anonymous access renders a public shell before owner-scoped routes run", () => {
    const source = read("app.ts");
    assert.match(source, /context !== null && context\.user == null/);
    assert.match(source, /if \(hostedAnonymous\) \{\s*renderAnonymousAccess\(rootElement, route\.kind !== "home"\);\s*return;/s);
    assert.match(source, /Anonymous access does not create a shared or placeholder owner/);
});

test("Hex Crawl follows the Dorks & Dice theme and standalone system preference", () => {
    const source = read("styles.ts");
    assert.match(source, /color-scheme: light/);
    assert.match(source, /html\[data-bs-theme="dark"\] #tool-root\.hex-crawl-app/);
    assert.match(source, /@media \(prefers-color-scheme: dark\)/);
    assert.match(source, /html:not\(\[data-bs-theme\]\) #tool-root\.hex-crawl-app/);
    assert.equal(source.includes("MutationObserver"), false);
});

test("home dashboard sections share the same three-column grid", () => {
    const view = read("modules/home/tool-home-view.ts");
    const styles = read("styles.ts");
    assert.equal((view.match(/hc-home-three-column-grid/g) ?? []).length, 3);
    assert.match(view, /hc-columns hc-home-three-column-grid hc-home-main-grid/);
    assert.match(styles, /\.hc-home-three-column-grid \{ grid-template-columns: repeat\(3, minmax\(0, 1fr\)\); gap: \.65rem; \}/);
    assert.match(styles, /\.hc-home-main-grid > :first-child \{ grid-column: span 2; \}/);
    assert.match(styles, /\.hc-home-main-grid > :first-child \{ grid-column: auto; \}/);
});

test("world editor removes nested control scrolling without changing narrow-layout flow", () => {
    const view = read("modules/worlds/world-editor-view.ts");
    const styles = read("styles.ts");
    assert.match(view, /hc-world-editor/);
    assert.match(styles, /\.hc-sidebar \{ display: grid; gap: \.6rem; max-height: none; overflow: visible;/);
    assert.match(styles, /\.hc-world-editor \.hc-map-panel \{ position: sticky; top: \.75rem; \}/);
    assert.match(styles, /@media \(max-width: 820px\)[\s\S]*\.hc-world-editor \.hc-map-panel \{ position: static; \}/);
});

test("interactive canvas exposes accessible keyboard controls and authoritative cell position", () => {
    const surface = read("map-surface.ts");
    assert.match(surface, /setAttribute\("role", "region"\)/);
    assert.match(surface, /aria-describedby/);
    assert.match(surface, /aria-keyshortcuts/);
    assert.match(surface, /ArrowUp/);
    assert.match(surface, /event\.key === "Enter" \|\| event\.key === " "/);
    assert.match(surface, /lookupWorldCell/);
    assert.match(surface, /renderer\.selectedWorldCell = geometry/);
    assert.doesNotMatch(surface, /worldToHex|selectedHex|setHexSelectionHandler/);
    assert.match(surface, /Current expedition cell is outlined/);
    assert.match(surface, /Selected a world cell/);
});

test("DM-facing world authoring copy explains empty states and keeps deeper map terminology secondary", () => {
    const world = read("modules/worlds/world-editor-view.ts");
    const maps = [
        read("modules/worlds/source-map-workspace.ts"),
        read("modules/worlds/source-map-registration-controller.ts"),
        read("modules/worlds/wonderdraft-import-controller.ts")
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

test("non-spatial primary workspace does not fabricate route, navigation, course, or pace state", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const start = view.indexOf("const renderNonSpatialWorkspace");
    const end = view.indexOf("const journeyFact", start);
    const nonspatial = view.slice(start, end);
    assert.match(nonspatial, /Current stage/);
    assert.match(nonspatial, /Progress/);
    assert.match(nonspatial, /Roles/);
    assert.match(nonspatial, /Pending/);
    assert.doesNotMatch(nonspatial, /currentHex|intendedDirection|selectedHex|Current travel|Pace \/ travel mode/);
});

test("unified non-spatial workspace keeps procedure and expedition history available as focused state", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    assert.match(view, /railAction\("Expedition history", presentation\.timeLabel, openHistory\)/);
    assert.match(view, /journey\?\.processPolicy\.support === "Supported"/);
    assert.match(view, /No journey process is currently active/);
});

test("unified expedition workspace keeps presentation derivation separate from mutation orchestration", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const model = read("modules/expeditions/expedition-workspace-model.ts");
    assert.match(view, /expeditionWorkspacePresentation/);
    assert.match(model, /export function expeditionWorkspacePresentation/);
    assert.doesNotMatch(model, /api\./);
    assert.doesNotMatch(model, /advanceExpedition/);
    assert.doesNotMatch(view, /api\.advanceExpedition/);
});

test("focused party workspace exposes the persisted party register through a dedicated controller", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const party = read("modules/expeditions/expedition-party-sheet.ts");
    assert.match(view, /ExpeditionPartySheetController/);
    assert.match(view, /summary\.dataset\.partySummary = ""/);
    assert.match(view, /editor\.dataset\.partyEditor = ""/);
    assert.match(view, /openDrawer\("Party & activities"/);
    assert.match(party, /updateExpeditionParty/);
    assert.match(party, /Marching order/);
    assert.match(party, /Watch list/);
    assert.match(party, /Standing orders/);
    assert.match(party, /Per hour/);
    assert.match(party, /Per watch/);
    assert.match(party, /Per march/);
    const unitResolver = read("modules/expeditions/party-movement-rendering.ts");
    assert.match(party, /preferredPartyMovementUnit/);
    assert.ok(unitResolver.includes("runtime.context.tiling?.physicalDistancePerWorldUnit?.unit"));
});

test("cell watches use persisted participant assignments instead of free-form activity input", () => {
    const periodic = read("modules/expeditions/periodic-expedition-workbench.ts");
    const party = read("modules/expeditions/expedition-party-sheet.ts");
    assert.match(periodic, /runtime\.party\.activityAssignments/);
    assert.match(periodic, /activeActivityAssignments/);
    assert.doesNotMatch(periodic, /name="activities"/);
    assert.match(party, /activityAssignments/);
    assert.match(party, /participantActivityPolicy/);
    assert.match(party, /The current procedure does not define participant activity assignments/);
});

test("party movement editor derives units from persisted session data without a mile fallback", () => {
    const party = read("modules/expeditions/expedition-party-sheet.ts");
    const unitResolver = read("modules/expeditions/party-movement-rendering.ts");
    assert.match(party, /preferredPartyMovementUnit/);
    assert.ok(unitResolver.includes("runtime.context.tiling?.physicalDistancePerWorldUnit?.unit"));
    assert.match(unitResolver, /runtime\.expedition\.distanceTraveled\?\.unit/);
    assert.match(unitResolver, /\?\? null/);
    assert.doesNotMatch(party, /\?\? \{ kind: "Mile", symbol: "mi", metersPerUnit: 1609\.344 \}/);
    assert.match(party, /custom movement unit conversion must be a finite positive number/i);
});

test("running surfaces use document scrolling and focused assistants keep a readable content width", () => {
    const styles = read("styles.ts");
    assert.match(styles, /\.hc-sidebar \{ display: grid; gap: \.6rem; max-height: none; overflow: visible;/);
    assert.match(styles, /\.hc-history \{ margin-bottom: 0;/);
    assert.doesNotMatch(styles, /\.hc-history \{[^}]*max-height:/);
    assert.doesNotMatch(styles, /\.hc-event-audit-list \{[^}]*overflow: auto/);
    assert.match(styles, /\.hc-assistant-grid \{ display: grid; grid-template-columns: minmax\(0, 44rem\) minmax\(18rem, 30rem\);/);
    assert.match(styles, /justify-content: start/);
});

test("triggered encounters expose a Block Initiative handoff without loading another tool inside Hex Crawl", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const assistant = read("modules/assistants/expedition-assistant-view.ts");
    const handoff = read("encounter-handoff.ts");
    assert.match(view, /Open in Block Initiative/);
    assert.match(view, /runtime\.expedition\.pendingEncounter/);
    assert.match(view, /api\.createEncounterHandoff/);
    assert.match(view, /blockInitiativeHandoffHref/);
    assert.match(assistant, /Open in Block Initiative/);
    assert.match(handoff, /sourceTool: "hex-crawl"/);
    assert.match(handoff, /\/tools\/block-initiative/);
    assert.match(handoff, /combatants: EncounterHandoffCombatant\[\]/);
});

test("spatial sessions use only the periodic-cell workbench while nonspatial sessions keep their own page", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const periodic = read("modules/expeditions/periodic-expedition-workbench.ts");
    assert.match(view, /return renderPeriodicExpeditionWorkbench/);
    assert.match(view, /page\.append\(status, renderNonSpatialWorkspace\(\), renderGmTools\(\)\)/);
    assert.match(periodic, /hc-workspace-grid/);
    assert.match(periodic, /hc-panel hc-map-panel/);
    assert.doesNotMatch(view, /renderSpatialWorkspace|renderCurrentTravel|renderAdjacencyNavigator/);
});

test("expedition setup accepts saved revisions and materializes preset selections through one start contract", () => {
    const setup = read("modules/expeditions/expedition-setup.ts");
    const selection = read("modules/expeditions/procedure-start-selection.ts");
    assert.match(setup, /api\.getProcedurePresets\(\)/);
    assert.match(setup, /ProcedureComposerApi\.create/);
    assert.match(setup, /populateProcedureStartChoices/);
    assert.match(setup, /applyCellProcedureChoice/);
    assert.match(setup, /Saved rulesets use the selected revision/);
    assert.match(setup, /applyCellProcedureChoice/);
    assert.match(setup, /choice\.revision/);
    assert.match(setup, /api\.startCellExpedition/);
    assert.match(selection, /presetKey: choice\.presetKey/);
    assert.match(selection, /procedureId: choice\.procedureId/);
    assert.match(selection, /procedureRevision: choice\.revision/);
    assert.match(selection, /for \(const value of saved\)/);
    assert.doesNotMatch(selection, /saved\.filter\(value => value\.isExecutable\)/);
    assert.match(setup, /renderProcedureMechanicList/);
    assert.doesNotMatch(setup, /procedureSnapshot/);
    assert.doesNotMatch(setup, /name="customize"/);
    assert.doesNotMatch(setup, /compatibleResolutionHelpers/);
});

test("focused encounter assistant does not imply it can discard automatic helper timing", () => {
    const assistant = read("modules/assistants/expedition-assistant-view.ts");
    assert.match(assistant, /resolutionHelpers\?\.encounter/);
    assert.match(assistant, /Automatic encounter helpers also resolve timing/);
    assert.match(assistant, /independently resolved manual, external, or DM-override outcomes/);
});

test("procedure selectors expose materialized procedure mechanics before a session is created", () => {
    const setup = read("modules/expeditions/expedition-setup.ts");
    const assistant = read("modules/assistants/assistant-entry-view.ts");
    const home = read("modules/home/tool-home-view.ts");
    for (const source of [setup, assistant, home]) {
        assert.match(source, /data-procedure-mechanics/);
        assert.match(source, /renderProcedureMechanicList/);
        assert.match(source, /getProcedurePresets/);
    }
    assert.match(setup, /<summary>Ruleset details<\/summary>/);
    assert.match(setup, /populateProcedureStartChoices/);
    assert.match(assistant, /<summary>Ruleset details<\/summary>/);
    assert.match(assistant, /populateProcedureStartChoices/);
    assert.match(assistant, /applyStandaloneProcedureChoice/);
    assert.match(assistant, /ProcedureComposerApi\.create/);
    assert.doesNotMatch(assistant, /Procedure preset/);
});

test("procedure mechanics use shared presentation policy and the unified expedition links to the canonical reference", () => {
    const campaignProcedureView = read("campaign-procedure-view.ts");
    const setup = read("modules/expeditions/expedition-setup.ts");
    const assistant = read("modules/assistants/assistant-entry-view.ts");
    const home = read("modules/home/tool-home-view.ts");
    const expedition = read("modules/expeditions/expedition-view.ts");
    assert.match(setup, /campaignProcedureSummary/);
    assert.match(assistant, /campaignProcedureSummary/);
    assert.match(home, /campaignProcedureSummary/);
    assert.match(expedition, /Ruleset reference/);
    assert.match(expedition, /\/procedures\/\$\{encodeURIComponent\(runtime\.procedure\.procedureId\)\}\/revisions\/\$\{runtime\.procedure\.revision\}\/reference/);
    assert.match(campaignProcedureView, /discrete cell-boundary transitions/);
    assert.match(campaignProcedureView, /actual distance = expected distance/);
    assert.match(campaignProcedureView, /situational modifier vs\. the DM-confirmed DC/);
    assert.match(campaignProcedureView, /encounter time uses 1d/);
    assert.match(campaignProcedureView, /configured components are not applicable to the active procedure mechanics/);
});


test("spatial mutation and discovery use only qualified cell interfaces", () => {
    const route = read("modules/expeditions/expedition-view.ts");
    const periodic = read("modules/expeditions/periodic-expedition-workbench.ts");
    const surface = read("map-surface.ts");
    assert.match(route, /if \(!runtime\.expedition\.currentCell\)/);
    assert.match(route, /return renderPeriodicExpeditionWorkbench/);
    assert.match(periodic, /api\.repositionCellExpedition\(runtime\.id/);
    assert.match(periodic, /map\.setCellSelectionHandler\(cell =>/);
    assert.match(surface, /lookupWorldCell\(worldId, point\)/);
    assert.match(periodic, /api\.advanceCellWatch/);
    assert.doesNotMatch(route, /ExpeditionWatchController|advanceExpedition|repositionExpedition|selectedHex|currentHex/);
    assert.doesNotMatch(periodic, /api\.advanceExpedition|setHexSelectionHandler/);
});

test("native cell resolution retains authoritative provenance and expires stale results", () => {
    const periodic = read("modules/expeditions/periodic-expedition-workbench.ts");
    assert.match(periodic, /api\.resolveCellProcedureInputs\(runtime\.id/);
    assert.match(periodic, /generatedProcedureResolutionId: generated\?\.generatedResolutionId/);
    assert.match(periodic, /generated\?\.travel \?\? travel/);
    assert.match(periodic, /generated\?\.navigation \?\? nav/);
    assert.match(periodic, /runtime = next;\s*generated = null/);
    assert.match(periodic, /next\.version !== result\.expeditionVersion/);
    assert.match(periodic, /displayedWatchNumber !== state\.activeWatchNumber/);
});

test("independent nonspatial page retains focused bookkeeping without axial watch controls", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    assert.match(view, /recordWatchAssistant/);
    assert.match(view, /renderNonSpatialWorkspace/);
    assert.match(view, /ExpeditionPartySheetController/);
    assert.match(view, /ExpeditionJourneyPanel/);
    assert.match(view, /ExpeditionSurvivalResourcesPanel/);
    assert.doesNotMatch(view, /HexCoordinate|HexDirection|hexSteps|WatchTravelPlan|ExpeditionWatchController/);
});
