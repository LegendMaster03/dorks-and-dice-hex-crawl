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
    assert.match(source, /if \(!host \|\| !world \|\| !runtime\.expedition\.isSpatial\) \{/);
    assert.match(source, /map\?\.dispose\(\);\s*map = null;\s*return;/);
    assert.doesNotMatch(source, /showMap/);
});

test("focused assistants never load an Overworld or construct a map surface", () => {
    const source = read("modules/assistants/expedition-assistant-view.ts");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("MapSurface"), false);
    assert.match(source, /recordTravelAssistant/);
    assert.match(source, /recordWatchAssistant/);
    assert.match(source, /recordNavigationAssistant/);
    assert.match(source, /recordEncounterAssistant/);
});

test("focused assistants require explicit resolved outcomes and provenance", () => {
    const source = read("modules/assistants/expedition-assistant-view.ts");
    assert.match(source, /<option value="">Select resolved outcome<\/option>/);
    assert.match(source, /<select name="outcome" required>/);
    assert.match(source, /<select name="source" data-source required>/);
    assert.match(source, /source\.append\(option\("", "Select result source"\)\)/);
    assert.match(source, /source\.value = ""/);
    assert.doesNotMatch(source, /source\.value = "ManualRoll"/);
    assert.match(source, /select\(form, "source"\)\.value = "ProcedureDefault"/);
});

test("mapless session creation never creates a placeholder Overworld", () => {
    const source = read("modules/home/tool-home-view.ts");
    assert.equal(source.includes("api.createOverworld("), false);
    assert.match(source, /api\.startStandaloneSession/);
    assert.match(source, /applyStandaloneProcedureChoice/);
    assert.match(source, /kind: "AbstractHex"/);
    assert.match(source, /kind: "NonSpatial"/);
});

test("crawl creation surfaces do not assume a 12-mile physical scale", () => {
    const world = read("modules/worlds/world-list-view.ts");
    const home = read("modules/home/tool-home-view.ts");
    const assistant = read("modules/assistants/assistant-entry-view.ts");
    const editor = read("modules/worlds/world-editor-view.ts");
    for (const source of [world, home, assistant]) {
        assert.doesNotMatch(source, /name="scale"[^>]*value="12"/);
        assert.match(source, /Select distance unit/);
    }
    for (const source of [world, home, assistant, editor]) {
        assert.doesNotMatch(source, /name="(?:symbol|unitSymbol)"[^>]*value="u"/);
        assert.doesNotMatch(source, /name="(?:meters|metersPerUnit)"[^>]*value="1"/);
    }
    assert.match(world, /No default physical scale is assumed/);
    assert.match(home, /unit\.required = abstract/);
    assert.match(assistant, /unit\.required = abstract/);
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
    assert.match(view, /Watch \/ time bookkeeping/);
    assert.equal(/recordWatchAssistant\([^)]*resultingHex/.test(view), false);
});

test("direct assistant entry does not create or fetch an Overworld", () => {
    const source = read("modules/assistants/assistant-entry-view.ts");
    assert.equal(source.includes("getOverworld"), false);
    assert.equal(source.includes("createOverworld"), false);
    assert.match(source, /startStandaloneSession/);
    assert.match(source, /kind: "NonSpatial"/);
    assert.match(source, /kind: "AbstractHex"/);
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

test("interactive canvas exposes a named keyboard interaction surface and selected hex state", () => {
    const surface = read("map-surface.ts");
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

test("direction controls retain numeric values while the unified workspace exposes six accessible intent actions", () => {
    const runtime = read("runtime-view.ts");
    const expedition = read("modules/expeditions/expedition-view.ts");
    const watch = read("modules/expeditions/expedition-watch-controller.ts");
    const assistant = read("modules/assistants/expedition-assistant-view.ts");
    assert.match(runtime, /"Toward \+q"/);
    assert.match(runtime, /"Toward -q"/);
    assert.match(expedition, /for \(let direction = 0; direction < 6; direction \+= 1\)/);
    assert.match(expedition, /control\.dataset\.direction = String\(direction\)/);
    assert.match(expedition, /openTravelWorkspace\(direction\)/);
    assert.match(expedition, /aria-label", "Adjacent hex travel direction"/);
    assert.match(assistant, /<option value="">Select intended direction<\/option>/);
    assert.match(assistant, /\$\{directionLabel\(value\)\}<\/option>/);
    assert.match(watch, /state\.intendedDirection === null \? "" : String\(state\.intendedDirection\)/);
    assert.match(watch, /else if \(!direction\.value && state\.intendedDirection !== null\)/);
    assert.match(watch, /direction\.value = String\(state\.intendedDirection\)/);
    assert.match(watch, /persisted runtime values remain 0–5/);
});

test("non-spatial running-sheet ledger omits spatial-only route and navigation columns", () => {
    const presentation = read("modules/expeditions/expedition-presentation.ts");
    assert.match(presentation, /const spatial = runtime\.expedition\.isSpatial/);
    assert.match(presentation, /\["Day", "Watch", "Travel \/ progress", "Encounter", "State"\]/);
    assert.match(presentation, /if \(spatial\) cells\.push\(textCell\(entry\.route\)\)/);
    assert.match(presentation, /if \(spatial\) cells\.push\(textCell\(entry\.navigation\)\)/);
});

test("non-spatial running sheet exposes the same persisted procedure mechanics reference", () => {
    const presentation = read("modules/expeditions/expedition-presentation.ts");
    assert.match(presentation, /<summary>Procedure reference<\/summary>/);
    assert.match(presentation, /<div data-snapshots><\/div>/);
    assert.match(presentation, /renderExpeditionHistory\(root, runtime\);\s*renderExpeditionSnapshots\(root, runtime, false\);/);
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

test("expedition watch controller owns watch form policy and mutation submission", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.match(view, /ExpeditionWatchController/);
    assert.doesNotMatch(view, /api\.advanceExpedition/);
    assert.match(controller, /api\.advanceExpedition/);
    assert.match(controller, /navigationResolutionDue/);
    assert.match(controller, /encounterCheckDue/);
    assert.match(controller, /persisted runtime values remain 0–5/);
});

test("focused party workspace exposes the persisted party register through a dedicated controller", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const party = read("modules/expeditions/expedition-party-sheet.ts");
    assert.match(view, /ExpeditionPartySheetController/);
    assert.match(view, /summary\.dataset\.partySummary = ""/);
    assert.match(view, /editor\.dataset\.partyEditor = ""/);
    assert.match(view, /openDrawer\("Party & travel order"/);
    assert.match(party, /updateExpeditionParty/);
    assert.match(party, /Marching order/);
    assert.match(party, /Watch list/);
    assert.match(party, /Standing orders/);
    assert.match(party, /Per hour/);
    assert.match(party, /Per watch/);
    assert.match(party, /Per march/);
    assert.match(party, /runtime\.context\.hexCenterDistance\?\.unit/);
});

test("watch planning uses authoritative typed party assignments instead of a free-form activity input", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    const party = read("modules/expeditions/expedition-party-sheet.ts");
    assert.match(view, /partyActivitySummary\(runtime\)/);
    assert.match(view, /Party & activities/);
    assert.doesNotMatch(view, /name="activities"/);
    assert.doesNotMatch(controller, /activities:/);
    assert.match(party, /activityAssignments/);
    assert.match(party, /participantActivityPolicy/);
    assert.match(party, /The current procedure does not define participant activity assignments/);
    assert.match(party, /structural or future activity policy/);
});

test("party movement editor derives units from persisted session data without a mile fallback", () => {
    const party = read("modules/expeditions/expedition-party-sheet.ts");
    assert.match(party, /runtime\.context\.hexCenterDistance\?\.unit/);
    assert.match(party, /runtime\.expedition\.distanceTraveled\.unit/);
    assert.doesNotMatch(party, /\?\? \{ kind: "Mile", symbol: "mi", metersPerUnit: 1609\.344 \}/);
    assert.match(party, /custom movement unit conversion must be a finite positive number/i);
});

test("watch forms do not invent unresolved travel results", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    const assistant = read("modules/assistants/expedition-assistant-view.ts");
    assert.doesNotMatch(view, /name="hexSteps"[^>]*value="1"/);
    assert.doesNotMatch(controller, /actualDistance"\)\.value = String\(scale\)/);
    assert.match(controller, /suggestedWatchDistance\(runtime\)/);
    assert.doesNotMatch(assistant, /name="hexSteps"[^>]*value="1"/);
    assert.doesNotMatch(assistant, /name="distance"[^>]*value="\$\{scale\}"/);
    assert.match(assistant, /does not infer distance or hex steps from the map scale/);
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
    assert.match(view, /runtime\.pauseReason === "EncounterTriggered"/);
    assert.match(view, /api\.createEncounterHandoff/);
    assert.match(view, /blockInitiativeHandoffHref/);
    assert.match(assistant, /Open in Block Initiative/);
    assert.match(handoff, /sourceTool: "hex-crawl"/);
    assert.match(handoff, /\/tools\/block-initiative/);
    assert.match(handoff, /combatants: EncounterHandoffCombatant\[\]/);
});

test("world-bound unified workspace keeps the map primary and stacks cleanly on narrower layouts", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const styles = read("phase15-styles.ts");
    assert.match(view, /section\.className = world \? "hc-workspace-grid" : "hc-nonspatial-primary"/);
    assert.match(view, /primary\.className = "hc-panel hc-map-panel"/);
    assert.match(view, /host\.className = "hc-map-host"/);
    assert.match(styles, /\.hc-phase15-expedition \.hc-workspace-grid \{ grid-template-columns:minmax\(0,1fr\) minmax\(18rem,23rem\); align-items:start; \}/);
    assert.match(styles, /@media \(max-width: 1040px\)[\s\S]*\.hc-phase15-expedition \.hc-workspace-grid,[\s\S]*grid-template-columns:1fr/);
});

test("expedition setup accepts saved revisions and materializes preset selections through one start contract", () => {
    const setup = read("modules/expeditions/expedition-setup.ts");
    const selection = read("modules/expeditions/procedure-start-selection.ts");
    assert.match(setup, /api\.getProcedurePresets\(\)/);
    assert.match(setup, /ProcedureComposerApi\.create/);
    assert.match(setup, /populateProcedureStartChoices/);
    assert.match(setup, /applyWorldProcedureChoice/);
    assert.match(setup, /Saved procedures use the selected revision/);
    assert.match(setup, /applyWorldProcedureChoice/);
    assert.match(setup, /choice\.revision/);
    assert.match(selection, /procedureKey: choice\.presetKey/);
    assert.match(selection, /procedureId: choice\.procedureId/);
    assert.match(selection, /procedureRevision: choice\.revision/);
    assert.match(selection, /for \(const value of saved\)/);
    assert.doesNotMatch(selection, /saved\.filter\(value => value\.isExecutable\)/);
    assert.match(setup, /renderProcedureMechanicList/);
    assert.doesNotMatch(setup, /procedureSnapshot/);
    assert.doesNotMatch(setup, /name="customize"/);
    assert.doesNotMatch(setup, /compatibleResolutionHelpers/);
});

test("spatial structural procedures stay viewable without constructing executable watch controls", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    assert.match(view, /if \(!runtime\.expedition\.isSpatial \|\| runtime\.procedure\.runtime === null\) \{\s*openHistory\(\);\s*return;\s*\}/);
    assert.match(view, /const controller = new ExpeditionWatchController/);
    assert.match(view, /This structural expedition has no executable spatial or interval action/);
    assert.doesNotMatch(view, /api\.advanceExpedition/);
});

test("focused encounter assistant does not imply it can discard automatic helper timing", () => {
    const assistant = read("modules/assistants/expedition-assistant-view.ts");
    assert.match(assistant, /resolutionHelpers\?\.encounter/);
    assert.match(assistant, /does not apply it because the helper also resolves encounter timing/);
    assert.match(assistant, /manual, external, or DM-override bookkeeping/);
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
    assert.match(setup, /<summary>Procedure details<\/summary>/);
    assert.match(setup, /populateProcedureStartChoices/);
    assert.match(assistant, /<summary>Procedure details<\/summary>/);
    assert.match(assistant, /populateProcedureStartChoices/);
    assert.match(assistant, /applyStandaloneProcedureChoice/);
    assert.match(assistant, /ProcedureComposerApi\.create/);
    assert.doesNotMatch(assistant, /Procedure preset/);
});

test("procedure mechanics use one shared presentation policy across setup, assistants, and running sheet", () => {
    const campaignProcedureView = read("campaign-procedure-view.ts");
    const setup = read("modules/expeditions/expedition-setup.ts");
    const assistant = read("modules/assistants/assistant-entry-view.ts");
    const home = read("modules/home/tool-home-view.ts");
    const presentation = read("modules/expeditions/expedition-presentation.ts");
    assert.match(setup, /campaignProcedureSummary/);
    assert.match(assistant, /campaignProcedureSummary/);
    assert.match(home, /campaignProcedureSummary/);
    assert.match(presentation, /procedureMechanicLines/);
    assert.match(campaignProcedureView, /exit factors start/);
    assert.match(campaignProcedureView, /actual distance = expected distance/);
    assert.match(campaignProcedureView, /situational modifier vs\. the DM-confirmed DC/);
    assert.match(campaignProcedureView, /encounter time uses 1d/);
    assert.match(campaignProcedureView, /configured components are not applicable to the active procedure mechanics/);
});

test("automatic helper controls show the persisted procedure formulas at the point of use", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.match(view, /data-helper-travel-mechanic/);
    assert.match(view, /data-helper-navigation-mechanic/);
    assert.match(view, /data-helper-encounter-mechanic/);
    assert.match(controller, /procedureHelperMechanics\(runtime\.procedure\)/);
    assert.match(controller, /mechanics\.travel \?\? ""/);
    assert.match(controller, /mechanics\.navigation \?\? ""/);
    assert.match(controller, /mechanics\.encounter \?\? ""/);
});

test("running sheet wires server-verified automatic procedure resolution through the watch controller", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.match(view, /data-resolution-helper-button/);
    assert.match(controller, /resolveProcedureInputs/);
    assert.match(controller, /getExpedition/);
    assert.match(controller, /generatedProcedureResolutionId/);
    assert.match(controller, /AutomaticRoll/);
    assert.match(controller, /Edited after automatic generation/);
    assert.match(controller, /DmOverride/);
    assert.match(controller, /generatedEncounterLocationId === null/);
});

test("automatic resolution provenance expires when another session mutation advances the version", () => {
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.match(controller, /private generatedResolutionVersion: number \| null = null/);
    assert.match(controller, /expireGeneratedResolutionIfVersionChanged\(runtime\.version\)/);
    assert.match(controller, /this\.generatedResolutionVersion = result\.generatedResolutionId === null/);
    assert.match(controller, /this\.generatedResolutionVersion === runtimeVersion/);
    assert.match(controller, /source\.value = ""/);
    assert.match(controller, /Generated procedure inputs expired because the crawl session changed/);
});

test("automatic resolution and watch advancement lock each other while pending", () => {
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.match(controller, /private resolutionPending = false/);
    assert.match(controller, /button\.disabled = !any \|\| this\.advancePending \|\| this\.resolutionPending/);
    assert.match(controller, /this\.disposed \|\| this\.advancePending \|\| this\.resolutionPending/);
    assert.match(controller, /this\.advancePending \|\| this\.resolutionPending \|\| this\.disposed/);
    assert.match(controller, /this\.advanceButton\.disabled = true;[\s\S]{0,160}button\.disabled = true;/);
});

test("due navigation and encounter inputs have no implicit successful result", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.match(view, /name="navigationOutcome"><option value="">Select resolved result/);
    assert.match(view, /name="encounterOutcome"><option value="">Select resolved outcome/);
    assert.doesNotMatch(view, /name="veerSteps"[^>]*value="1"/);
    assert.match(controller, /A navigation check is due\. Select its resolved result\./);
    assert.match(controller, /An encounter check is due\. Select its resolved outcome\./);
});

test("watch controller clears resolved inputs only when authoritative segment state changes", () => {
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.match(controller, /segmentStateKey/);
    assert.match(controller, /activeWatchElapsedHours/);
    assert.match(controller, /clearResolvedSegmentInputs/);
    assert.match(controller, /input\(this\.form, "veerSteps"\)\.value = ""/);
    assert.match(controller, /checkbox\(this\.form, "doubleBack"\)\.checked = false/);
    assert.doesNotMatch(controller, /runtime\.version[\s\S]{0,100}segmentStateKey/);
});

test("running sheet requires explicit provenance and navigation-helper inputs", () => {
    const view = read("modules/expeditions/expedition-view.ts");
    const controller = read("modules/expeditions/expedition-watch-controller.ts");
    assert.doesNotMatch(view, /helperNavigationModifier"[^>]*value="0"/);
    assert.match(controller, /Select resolution source/);
    assert.match(controller, /readResolutionSource\("travelSource", "travel"\)/);
    assert.match(controller, /readResolutionSource\("navigationSource", "navigation"\)/);
    assert.match(controller, /readResolutionSource\("encounterSource", "encounter"\)/);
    assert.match(controller, /readResolutionSource\("boundarySource", "boundary", false\)/);
    assert.doesNotMatch(controller, /control\.value = "ManualRoll"/);
});