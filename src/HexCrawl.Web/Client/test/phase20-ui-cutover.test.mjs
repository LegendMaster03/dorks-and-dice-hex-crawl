import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { nextCellExpeditionAction } from "../.test-dist/modules/expeditions/phase15-cell-action.js";
import { polygonContainsSubject, uniquelyAdjacentInterface } from "../.test-dist/modules/expeditions/phase20-cell-ui-policy.js";

const file = (...segments) => fs.readFileSync(path.join("src", ...segments), "utf8");
const id = (tilingId = "tiling-1") => ({
    tilingId,
    address: { motifCellId: "accepted-hex-motif", translation: { u: 7, v: -4 } }
});

test("map click selects a travel course only for a uniquely adjacent qualified cell", () => {
    const target = id();
    assert.equal(uniquelyAdjacentInterface(target, []), null);
    assert.equal(uniquelyAdjacentInterface(target, [{
        interfaceIndex: 5, neighborCell: target
    }]), 5);
    assert.equal(uniquelyAdjacentInterface(target, [
        { interfaceIndex: 0, neighborCell: target },
        { interfaceIndex: 4, neighborCell: target }
    ]), null);
    assert.equal(uniquelyAdjacentInterface(target, [{
        interfaceIndex: 4, neighborCell: id("foreign")
    }]), null);
});

test("authored subjects are listed only inside the selected server cell polygon", () => {
    const square = [{x:0,y:0},{x:2,y:0},{x:2,y:2},{x:0,y:2}];
    assert.equal(polygonContainsSubject(square,{x:1,y:1}),true);
    assert.equal(polygonContainsSubject(square,{x:0,y:1}),true);
    assert.equal(polygonContainsSubject(square,{x:3,y:1}),false);
    assert.equal(polygonContainsSubject(square,{x:0,y:3}),false);
    assert.equal(polygonContainsSubject([],{x:0,y:0}),false);
    const concave = [{x:0,y:0},{x:3,y:0},{x:3,y:1},{x:1,y:1},{x:1,y:3},{x:0,y:3}];
    assert.equal(polygonContainsSubject(concave,{x:.5,y:2}),true);
    assert.equal(polygonContainsSubject(concave,{x:2,y:2}),false);
});

test("world starts use qualified cell addresses without axial translation", () => {
    const setup = file("modules", "expeditions", "expedition-setup.ts");
    const worldContract = file("..", "..", "HexCrawl.Web", "Modules", "Worlds", "WorldApiContracts.cs");
    assert.match(setup, /const startCell = \\{/);
    assert.match(setup, /motifCellId: select\\(form, "motifCellId"\\)\\.value/);
    assert.match(setup, /u: integer\\(numberInput\\(form, "u"\\)\\)/);
    assert.match(setup, /v: integer\\(numberInput\\(form, "v"\\)\\)/);
    assert.doesNotMatch(setup, /resolveHexStartCell|acceptMapHex|world\\.grid|name="q"|name="r"/);
    assert.match(worldContract, /world\\.World\\.SpatialTiling/);
});

test("world authoring map selections feed the sole periodic cell-start form", () => {
    const editor = file("modules","worlds","world-editor-view.ts");
    const setup = file("modules","expeditions","expedition-setup.ts");
    assert.match(editor, /new CustomEvent\("hex-crawl:world-cell-selected"/);
    assert.match(setup, /root\.addEventListener\("hex-crawl:world-cell-selected", acceptMapCell\)/);
    assert.doesNotMatch(setup, /root\.addEventListener\("hex-crawl:world-hex-selected"/);
});

test("active expedition start entry points use cell APIs and no retired V1 starts", () => {
    const home = file("modules","home","tool-home-view.ts");
    const editor = file("modules","worlds","world-editor-view.ts");
    const setup = file("modules","expeditions","expedition-setup.ts");
    const api = file("api.ts");
    assert.match(home, /api\.startStandaloneCellSession\(/);
    assert.match(home, /api\.startCellExpedition\(/);
    assert.match(home, /api\.startStandaloneSession\(/);
    assert.match(setup, /api\.startCellExpedition\(/);
    assert.match(editor, /enhanceExpeditionSetup\(/);
    assert.doesNotMatch(home + editor + setup, /api\.(startConfiguredExpedition|startExpedition)\(/);
    assert.doesNotMatch(api, /public (?:startConfiguredExpedition|startExpedition)\(/);
    assert.doesNotMatch(file("modules","expeditions","procedure-start-selection.ts"),
        /applyWorldProcedureChoice/);
    assert.match(api, /"POST", "\/api\/expeditions\/cells"/);
    assert.match(api, /"POST", "\/api\/expeditions"/);
});

test("renderer shows actual cell polygon and party in mapless and legacy hex worlds", () => {
    const renderer = file("canvas-renderer.ts");
    const map = file("map-surface.ts");
    const periodic = file("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(renderer, /if \(!ctx\) return/);
    assert.doesNotMatch(renderer, /if \(!ctx \|\| !world\) return/);
    assert.match(renderer, /this\.drawExpedition\(ctx, world, width, height\)/);
    assert.match(renderer, /!world \|\| world\.tiling\?\.id === this\.currentWorldCell\.id\.tilingId/);
    assert.match(renderer, /if \(this\.selectedWorldCell && world\.tiling\) \{/);
    const surface = file("map-surface.ts");
    assert.match(renderer, /if \(!this\.gridPreview && world\.tiling\) \{/);
    assert.doesNotMatch(renderer, /if \(!this\.gridPreview && world\.tiling && !world\.grid\)/);
    assert.match(surface, /if \(!world\?\.tiling \|\| this\.renderer\.gridPreview \|\| !this\.api\) return/);
    assert.match(surface, /if \(world\.tiling && !this\.renderer\.gridPreview && this\.api\)/);
    assert.doesNotMatch(renderer, /if \(this\.selectedWorldCell && world\.tiling && !world\.grid\)/);
    assert.match(map, /public centerOn\(point: WorldPoint\)/);
    assert.match(periodic, /if \(!world\) map\.centerOn\(topology\.position\)/);
});

test("mapless encounter choices do not offer authored world discoveries", () => {
    const periodic = file("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(periodic, /keyedEncounterOption\.disabled = true/);
    assert.match(periodic, /keyedEncounterOption\.hidden = true/);
    assert.match(periodic, /!world \|\| choice\("encounter"\)\.value !== "KeyedLocationDiscovery"/);
});

test("focused spatial assistants use V2 expedition workbench; nonspatial cadence keeps live routes", () => {
    const entry = file("modules","assistants","assistant-entry-view.ts");
    const view = file("modules","assistants","expedition-assistant-view.ts");
    const api = file("api.ts");
    assert.match(entry, /api\.startStandaloneCellSession\(request\)/);
    assert.match(entry, /api\.startStandaloneSession\(request\)/);
    assert.match(entry, /session\.context\.kind === "NonSpatial"/);
    assert.match(view, /if \(runtime\.expedition\.isSpatial\)/);
    assert.match(view, /navigate\(`\/expeditions\/\$\{runtime\.id\}`, true\)/);
    assert.doesNotMatch(view, /recordTravelAssistant|recordNavigationAssistant|currentHex|hexProgress/);
    assert.match(api, /\/nonspatial\/encounters/);
    assert.doesNotMatch(entry, /api\.startConfiguredExpedition\(/);
});

test("V2 cell GM tools use server-qualified position and manual discovery, never q/r state", () => {
    const view = file("modules","expeditions","periodic-expedition-workbench.ts");
    const api = file("api.ts");
    assert.match(view, /api\.resolveWorldCell\(current\.id/);
    assert.match(view, /map\.setClickInterceptor\(point => \{/);
    assert.match(view, /api\.lookupWorldCell\(current\.id, point\)/);
    assert.doesNotMatch(view, /map\.setHexSelectionHandler\(/);
    assert.match(view, /api\.repositionCellExpedition\(runtime\.id/);
    assert.match(view, /state\.activeWatchNumber === null && state\.pendingEncounter === null/);
    assert.match(view, /api\.discover\(runtime\.id, runtime\.version/);
    assert.match(view, /polygonContainsSubject\(selected\.polygon/);
    assert.match(view, /uniquelyAdjacentInterface\(geometry\.id, topology\.interfaces\)/);
    assert.match(view, /selectHeading\(adjacent\)/);
    assert.match(view, /control\.dataset\.adjacencyInterfaceId === edge\.id\)\?\.focus\(\)/);
    assert.match(view, /map\.renderer\.discoveredSubjectIds = discoveredSubjectIds\(runtime\)/);
    assert.match(api, /\/cells\/reposition/);
});

test("V2 cell-workbench helpers generate owner/version-scoped automatic rolls", () => {
    const view = file("modules","expeditions","periodic-expedition-workbench.ts");
    const api = file("api.ts");
    assert.match(api, /\/cells\/resolution-helper/);
    assert.match(view, /api\.resolveCellProcedureInputs\(runtime\.id/);
    assert.match(view, /generatedProcedureResolutionId: generated\?\.generatedResolutionId/);
    assert.match(view, /generated\?\.travel \?\? travel/);
    assert.match(view, /generated\?\.navigation \?\? nav/);
    assert.match(view, /generated\?\.encounter \?\? encounter/);
    assert.match(view, /form\.addEventListener\("input"/);
    assert.match(view, /form\.addEventListener\("change"/);
    assert.doesNotMatch(view, /if \(\(event\.target as Element\)\.closest\("\[data-helper-section\]"\)\) return/);
    assert.match(view, /onToggle: edge => \{[\s\S]*if \(generated\) invalidateGenerated\(\)/);
    assert.match(view, /runtime = next;\s*generated = null/);
    assert.match(view, /next\.version !== result\.expeditionVersion/);
    assert.match(view, /displayedWatchNumber !== state\.activeWatchNumber/);
    assert.match(view, /state\.activeDeliberateDoubleBack/);
    assert.match(view, /state\.activeContinueAcrossBoundaries/);
});

test("Phase 15 next action respects V2 interrupt, decision and travel precedence", () => {
    const ready = {
        pendingEncounter: false, pauseReason: null, executable: true,
        hasSelectedCourse: false, activeWatchNumber: null
    };
    assert.equal(nextCellExpeditionAction(ready).label, "Choose travel direction");
    assert.equal(nextCellExpeditionAction({ ...ready, hasSelectedCourse: true }).label, "Continue travel");
    assert.equal(nextCellExpeditionAction({ ...ready, hasSelectedCourse: true, activeWatchNumber: 3 }).label, "Resume watch 3");
    assert.equal(nextCellExpeditionAction({ ...ready, executable: false }).kind, "procedure");
    assert.equal(nextCellExpeditionAction({ ...ready, pauseReason: "ConditionsReviewRequired" }).urgent, true);
    assert.equal(nextCellExpeditionAction({ ...ready, pauseReason: "CellCourseAdjudicationRequired" }).kind, "boundary");
    assert.equal(nextCellExpeditionAction({ ...ready, pauseReason: "LostRecognitionRequired" }).kind, "boundary");
    assert.equal(nextCellExpeditionAction({ ...ready, pendingEncounter: true, pauseReason: "CellCourseAdjudicationRequired" }).kind, "encounter");
});

test("spatial expedition route rejects snapshots without authoritative cells rather than invoking V1", () => {
    const view = file("modules","expeditions","expedition-view.ts");
    assert.match(view, /if \(runtime\.expedition\.isSpatial\) \{/);
    assert.match(view, /if \(!runtime\.expedition\.currentCell\)/);
    assert.match(view, /throw new Error\("Spatial expedition is missing its required authoritative cell\."\)/);
    assert.doesNotMatch(view, /if \(runtime\.expedition\.isSpatial && runtime\.expedition\.currentCell\)/);
});

test("Phase 15 is the shared action, summary, drawer and map-first UI for V2 cells", () => {
    const legacy = file("modules","expeditions","expedition-view.ts");
    const periodic = file("modules","expeditions","periodic-expedition-workbench.ts");
    const action = file("modules","expeditions","phase15-next-action.ts");
    assert.match(legacy, /renderPhase15NextAction/);
    assert.match(periodic, /renderPhase15NextAction\(/);
    assert.match(action, /hc-current-action/);
    assert.match(periodic, /hc-phase15-expedition/);
    assert.match(periodic, /hc-phase15-runtime-summary/);
    assert.match(periodic, /hc-stat-action-grid/);
    assert.match(periodic, /hc-panel hc-map-panel/);
    assert.match(periodic, /hc-panel hc-sidebar hc-table-rail/);
    assert.match(periodic, /openWorkspaceDrawer\(root, "Travel \/ watch"/);
    assert.match(periodic, /travelDock\.append\(travelContent\)/);
    assert.match(periodic, /data-gameplay-panels/);
    assert.match(periodic, /new MapSurface\(/);
    assert.match(periodic, /currentPeriodicCellAdjacency\(topology\)/);
    assert.doesNotMatch(periodic, /api\.advanceExpedition\(/);
});

test("V2 preserved Phase 15 tactical handoff remains separate from encounter resolution", () => {
    const periodic = file("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(periodic, /data-handoff-encounter/);
    assert.match(periodic, /api\.createEncounterHandoff\(runtime\.id/);
    assert.match(periodic, /runtimeEncounterSequence: pending\.triggerSequence/);
    assert.match(periodic, /blockInitiativeHandoffHref\(handoff\)/);
    assert.match(periodic, /api\.resolveCellEncounter\(/);
    assert.doesNotMatch(periodic, /api\.resolveEncounter\(/);
});

test("mapless cell workbench does not require a fabricated overworld", () => {
    const view = file("modules","expeditions","expedition-view.ts");
    const periodic = file("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(view, /runtime\.expedition\.currentCell/);
    assert.doesNotMatch(periodic, /if \(!initialWorld \|\|/);
    assert.match(periodic, /world \? api\.getOverworld\(world\.id\) : Promise\.resolve\(null\)/);
    assert.match(periodic, /world \? "Expedition map" : "Mapless exploration"/);
    assert.match(periodic, /world\?\.locations \?\? \[\]/);
});
