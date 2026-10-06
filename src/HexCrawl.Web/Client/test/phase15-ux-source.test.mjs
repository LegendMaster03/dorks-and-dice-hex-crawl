import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceDir = path.resolve("src");
const repositoryRoot = path.resolve("..", "..", "..");

test("Phase 15 procedure authoring separates entry choice from one shared CampaignProcedure workspace", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const module = fs.readFileSync(path.join(sourceDir, "modules/procedures/module.ts"), "utf8");
    const types = fs.readFileSync(path.join(sourceDir, "procedure-composer-types.ts"), "utf8");
    const contracts = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Web/Modules/Procedures/ProcedureComposerContracts.cs"),
        "utf8");
    const factory = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Application/Expeditions/ProcedureComposerCustomProcedureFactory.cs"),
        "utf8");

    assert.match(workspace, /Start from a known procedure/);
    assert.match(workspace, /Build my own/);
    assert.match(workspace, /EntryState = "landing" \| "presets" \| "workspace"/);
    assert.match(workspace, /ProcedureAuthoringMode = "compact" \| "advanced" \| "json"/);
    assert.match(workspace, /These are the rules the DM runs/);
    assert.match(workspace, /Select one module to inspect its exact generic structure/);
    assert.match(workspace, /exact same CampaignProcedure produced by Compact structural choices and Advanced edits/);
    assert.match(workspace, /sourceProcedureId === null && \(sourcePresetKey !== null \|\| \(draft\?\.modules\.length \?\? 0\) > 0\)/);
    assert.match(workspace, /moduleSelections/);
    assert.match(types, /ProcedureComposerModuleSelectionInput/);
    assert.match(contracts, /ProcedureComposerModuleSelectionRequest/);
    assert.match(module, /renderProcedureAuthoringWorkspace/);
    assert.match(factory, /Modules = \[\]/);
    assert.doesNotMatch(factory, /Modules = \[CreateDefaultModule\(GenericProcedureCatalog\.TimeIntervalModule\)\]/);
    assert.doesNotMatch(factory, /GenericProcedureCatalog\.Catalog\.Select/);
});

test("Compact presentation covers all current generic procedure parameter labels", () => {
    const presentation = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-presentation.ts"),
        "utf8");
    const generic = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Application/Expeditions/GenericProcedureCatalog.cs"),
        "utf8");
    const journey = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Application/Expeditions/JourneyProcedureContractSchema.cs"),
        "utf8");
    const survival = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Application/Expeditions/Phase11ProcedurePolicies.cs"),
        "utf8");

    const parameterKeys = new Set();
    for (const source of [generic, journey, survival]) {
        for (const match of source.matchAll(/\["([^"]+)"\]\s*=\s*new\("(?:enum|boolean|number|integer|decimal|string|key-list|map<string>)"/g)) {
            parameterKeys.add(match[1]);
        }
        for (const match of source.matchAll(/\("([^"]+)",\s*"(?:enum|boolean|number|integer|decimal|string|key-list|map<string>)",/g)) {
            parameterKeys.add(match[1]);
        }
    }

    const labelBlock = presentation.slice(
        presentation.indexOf("const labels"),
        presentation.indexOf("const choiceSets"));
    for (const key of parameterKeys) {
        assert.ok(
            labelBlock.includes(`${key}: "`) || labelBlock.includes(`"${key}": "`),
            key);
    }
});



test("Phase 15 Compact keeps journey authoring singular and makes ordinary rules discoverable", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const presentation = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-presentation.ts"),
        "utf8");

    const compactStart = workspace.indexOf("const renderCompact");
    const libraryStart = workspace.indexOf("const renderRuleLibrary", compactStart);
    assert.notEqual(compactStart, -1);
    assert.notEqual(libraryStart, -1);
    const neutral = workspace.slice(compactStart, libraryStart);
    assert.doesNotMatch(neutral, /value\.moduleKey === "journey\.process"/);
    assert.match(workspace, /Add exploration rules/);
    assert.match(workspace, /Travel, course, and pace/);
    assert.match(workspace, /Navigation needs spatial movement/);
    assert.match(workspace, /Add movement and navigation/);
    assert.match(workspace, /Encounters/);
    assert.match(workspace, /Survival and recovery/);
    assert.match(workspace, /Journeys and events/);
    assert.doesNotMatch(presentation, /Procedure support/);
    assert.doesNotMatch(presentation, /label: "Resolution helpers"/);
});

test("Phase 15 journey-first Compact runs journey rules before supporting survival effects", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.match(workspace, /travel\.length === 0 && activeRules\("Journey & events"\)\.length > 0/);
    assert.match(workspace, /\["Journey & events", "Survival & resources", "Automation"\]/);
});

test("Phase 15 Inspect and Compact consume one semantic fact formatter", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.ok((workspace.match(/procedureParameterFacts\(/g) ?? []).length >= 2);
    assert.match(workspace, /hc-inspect-rule-facts/);
    assert.doesNotMatch(workspace, /presetModuleSummary/);
});

test("Phase 15 Advanced uses a grouped keyboard-navigable structure rail and focused inspector", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(workspace, /hc-advanced-index-group/);
    assert.match(workspace, /hc-advanced-module-select/);
    assert.match(workspace, /aria-current/);
    assert.match(workspace, /ArrowDown/);
    assert.match(workspace, /ArrowUp/);
    assert.match(workspace, /Home/);
    assert.match(workspace, /End/);
    assert.match(workspace, /Required dependencies/);
    assert.match(workspace, /Optional dependencies/);
    assert.match(workspace, /Execution handler/);
    assert.match(styles, /grid-template-columns:minmax\(20rem,24rem\) minmax\(0,1fr\)/);
    assert.match(styles, /\.hc-advanced-module-select code/);
    assert.doesNotMatch(styles, /hc-advanced-module-toggle/);
});

test("Phase 15 preset classification and saved procedure cards use explicit product metadata", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const types = fs.readFileSync(path.join(sourceDir, "types.ts"), "utf8");

    assert.match(types, /category: string/);
    assert.match(workspace, /value\.category === "Generic starting points"/);
    assert.doesNotMatch(workspace, /presetKey\.startsWith\("simple-"\)/);
    assert.match(workspace, /hc-saved-procedure-summary/);
    assert.match(workspace, /hc-saved-procedure-actions/);
});

test("Phase 15 procedure surfaces inherit shared theme tokens", () => {
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    for (const selector of [
        ".hc-preset-card",
        ".hc-focus-workspace",
        ".hc-area-card",
        ".hc-rule-library-group",
        ".hc-advanced-module-row.is-selected"
    ]) {
        assert.ok(styles.includes(selector), selector);
    }
    assert.match(styles, /background:var\(--hc-surface\)/);
    assert.match(styles, /background:var\(--hc-surface-elevated\)/);
    assert.match(styles, /color:var\(--hc-text\)/);
    assert.match(styles, /outline:3px solid var\(--hc-focus\)/);
});

test("Compact procedure edits keep their focused workspace across draft recomposition", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.match(workspace, /let activeDrawer: WorkspaceDrawer \| null = null/);
    assert.match(workspace, /if \(!refreshCompactDrawer\(next\)\) render\(\)/);
    assert.match(workspace, /const refreshCompactDrawer = \(current: ProcedureComposer\): boolean =>/);
    assert.match(workspace, /populateCompactArea\(body, group\.modules\)/);
    assert.match(workspace, /control\.dataset\.compactModule = module\.moduleKey/);
    assert.match(workspace, /control\.dataset\.compactField = key/);
    assert.match(workspace, /replacement\?\.focus\(\)/);
    assert.match(workspace, /open\.dataset\.compactArea = group\.section/);
    assert.match(workspace, /save\.dataset\.procedureSave = ""/);
    assert.match(workspace, /root\.querySelector<HTMLButtonElement>\("\[data-procedure-save\]"\)/);
    assert.match(
        workspace,
        /if \(save\) save\.disabled = savePending[\s\S]*!hasStructuredChanges\(\)[\s\S]*hasBlockingStructuredIssues\(\)/);
});

test("structured procedure save honors validation and dependency blockers", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.match(workspace, /hasBlockingStructuredIssues/);
    assert.match(workspace, /draft\?\.modules\.some\(saveBlocked\)/);
    assert.match(workspace, /need \${required\.join\(", "\)} before this procedure can be saved/);
    assert.match(workspace, /Add required companion rules/);
    assert.doesNotMatch(workspace, /Fix dependency issues/);
});

test("Advanced always renders the pinned mechanic without polluting catalog alternatives", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const contracts = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Web/Modules/Procedures/ProcedureComposerContracts.cs"),
        "utf8");

    assert.match(workspace, /module\.mechanic,\s*\.\.\.module\.alternatives\.filter/);
    assert.doesNotMatch(contracts, /\.Append\(selected\.Mechanic\)/);
});

test("structured procedure authoring exposes human-readable procedure naming", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const contracts = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Web/Modules/Procedures/ProcedureComposerContracts.cs"),
        "utf8");

    assert.match(workspace, /Procedure name/);
    assert.match(workspace, /nameInput\.name = "procedureName"/);
    assert.match(workspace, /nameOverride/);
    assert.match(workspace, /name: nameOverride/);
    assert.match(contracts, /string\? Name/);
});

test("procedure authoring exposes revision navigation and protects unsaved work", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.match(workspace, /dataset\.procedureRevision/);
    assert.match(workspace, /Choose the latest revision above before saving further changes/);
    assert.match(workspace, /Discard unsaved procedure changes\?/);
    assert.match(workspace, /Discard unsaved JSON changes\?/);
    assert.match(workspace, /mode === "json"[\s\S]*jsonText !== jsonBaseline[\s\S]*window\.confirm/);
    assert.match(workspace, /beforeunload/);
    assert.match(workspace, /window\.removeEventListener\("beforeunload"/);
    assert.match(workspace, /jsonText\.length > 0 && jsonText !== jsonBaseline/);
});

test("Phase 15 procedure authoring keeps save and canonical-load failures visible after busy state clears", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.match(workspace, /let failure: unknown \| null = null;/);
    assert.match(workspace, /savePending = false;\s*if \(!disposed\) \{\s*render\(\);\s*if \(failure !== null\)/);
    assert.match(workspace, /jsonBusy = false;\s*if \(!disposed\) \{\s*render\(\);\s*if \(failure !== null\)/);
    assert.match(workspace, /Retry canonical JSON/);
    assert.match(workspace, /retry\.dataset\.jsonRetry/);
    assert.doesNotMatch(workspace, /catch \(value\) \{\s*if \(!disposed\) \{\s*render\(\);[\s\S]{0,220}finally \{\s*(?:savePending|jsonBusy) = false;\s*if \(!disposed\) render\(\);/);
});

test("Phase 15 expedition workspace is one procedure-driven surface instead of map and tracker modes", () => {
    const model = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-workspace-model.ts"),
        "utf8");
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const module = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/module.ts"),
        "utf8");

    assert.match(model, /runtime\.pauseReason === "EncounterTriggered"/);
    assert.match(model, /runtime\.pauseReason === "LostRecognitionRequired"/);
    assert.match(model, /journey\?\.activeProcesses\.find/);
    assert.match(model, /canUseFocusedNonSpatialWatch/);
    assert.match(model, /expeditionWorkspaceCapabilities/);
    assert.match(view, /hc-current-action/);
    assert.match(view, /runtime\.expedition\.isSpatial/);
    assert.match(view, /Journey \/ challenge/);
    assert.match(view, /GM Tools/);
    assert.match(view, /MapSurface/);
    assert.doesNotMatch(view, /data-view-tracker/);
    assert.doesNotMatch(view, /data-view-map/);
    assert.doesNotMatch(module, /enhanceExpeditionWorkspace/);
    assert.doesNotMatch(module, /ExpeditionEnvironmentPanel/);
    assert.doesNotMatch(module, /subscribeExpeditionRuntimeChanged/);
});

test("Phase 15 nonspatial actions never route an interval or no-interval journey through spatial travel", () => {
    const model = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-workspace-model.ts"),
        "utf8");

    assert.match(model, /runtime\.expedition\.isSpatial\s*&&\s*runtime\.expedition\.activeWatchNumber !== null/);
    assert.match(model, /!runtime\.expedition\.isSpatial && canUseFocusedNonSpatialWatch\(runtime\)/);
    assert.match(model, /kind: "watch"/);
    assert.match(model, /Resume watch/);
    assert.match(model, /if \(!hasInterval\) \{\s*return `Day \$\{state\.currentDay\} · \$\{formatNumber\(state\.elapsedTravelHours\)\} h elapsed`;/);
    assert.doesNotMatch(model, /if \(runtime\.expedition\.activeWatchNumber !== null && runtime\.procedure\.runtime !== null\)/);
});

test("routine spatial travel reuses intent and suppresses fixed movement inputs already resolved by the server", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");
    const movement = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-party-movement.ts"),
        "utf8");

    assert.match(view, /hc-adjacency-navigator/);
    assert.match(view, /currentHexAdjacency/);
    assert.match(view, /adjacencyEdgeForCell/);
    assert.match(view, /hex-crawl\.expedition\.\$\{runtime\.id\}\.travel-intent/);
    assert.match(view, /Reusable course and pace stay filled until changed/);
    assert.match(view, /movementComposition\.suggestedExpectedDistance/);
    assert.match(controller, /suggestedWatchDistance\(runtime\)/);
    assert.match(controller, /authoritativeFixedWatchDistance\(runtime\)/);
    assert.match(controller, /travelSource"\)\.value = "ProcedureDefault"/);
    assert.match(controller, /Override movement/);
    assert.match(controller, /source\.value = "DmOverride"/);
    assert.match(controller, /No movement value or provenance needs to be re-entered/);
    assert.match(controller, /activePaceKey/);
    assert.match(controller, /state\.intendedDirection/);
    assert.match(movement, /composition\.status !== "Resolved"/);
    assert.match(movement, /composition\.status !== "ReferenceFallback"/);
    assert.doesNotMatch(view, /currentHex\s*=/);
});

test("automatic procedure helpers keep generated values and token in the focused drawer until watch submission", () => {
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");
    const start = controller.indexOf("private generateProcedureResolution(): void");
    const end = controller.indexOf("private applyGeneratedResolution", start);
    assert.notEqual(start, -1);
    assert.notEqual(end, -1);
    const helper = controller.slice(start, end);

    assert.match(helper, /generateProcedureResolutionInPlace/);
    assert.match(helper, /this\.api\.resolveProcedureInputs/);
    assert.match(helper, /this\.applyGeneratedResolution\(result\)/);
    assert.doesNotMatch(helper, /this\.runMutation/);
    assert.match(controller, /request\.generatedProcedureResolutionId = this\.generatedResolutionId/);
});

test("focused spatial watch failures stay visible in the drawer and do not escape as discarded rejections", () => {
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");

    assert.match(controller, /private showAdvanceError\(value: unknown\): void/);
    assert.match(controller, /error\.dataset\.watchAdvanceError = ""/);
    assert.match(controller, /error\.setAttribute\("role", "alert"\)/);
    assert.match(controller, /this\.clearAdvanceError\(\);\s*this\.advancePending = true/);
    assert.match(controller, /this\.runMutation\([\s\S]*\)\.catch\(value => \{\s*if \(!this\.disposed\) this\.showAdvanceError\(value\);\s*\}\);/);
});

test("focused expedition mutations share the visible workspace error boundary and authoritative runtime application", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const runUiMutation = async \(action: \(\) => Promise<void>\): Promise<void> =>/);
    assert.match(view, /await mutate\(action\);\s*\} catch \{\s*\/\/ mutate already surfaced the failure in the workspace error region\./);
    assert.match(view, /new ExpeditionWatchController\([\s\S]*\(\) => runtime,\s*applyRuntime,/);
    assert.match(view, /new ExpeditionPartySheetController\([\s\S]*\(\) => runtime,\s*applyRuntime,/);
    assert.match(view, /new ExpeditionEnvironmentPanel\([\s\S]*\(\) => runtime,\s*applyRuntime,/);
    assert.ok((view.match(/await runUiMutation\(action\)/g) ?? []).length >= 4);
    assert.match(view, /void runUiMutation\(async \(\) => \{/);
});

test("Phase 15 map selection is contextual and never directly mutates expedition position", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const map = fs.readFileSync(path.join(sourceDir, "map-surface.ts"), "utf8");

    assert.match(view, /Selecting an adjacent cell expresses travel intent only; it does not move the party/);
    assert.match(view, /map\.setHexSelectionHandler/);
    assert.match(view, /selectTravelIntent\(edge\.directionValue, edge\.targetCell\)/);
    assert.match(view, /syncTravelIntentControls\(\)/);
    const selection = view.slice(
        view.indexOf("const selectTravelIntent"),
        view.indexOf("const syncTravelIntentControls"));
    assert.doesNotMatch(selection, /api\./);
    assert.doesNotMatch(selection, /advanceExpedition/);
    assert.match(map, /setHexSelectionHandler/);
    assert.match(map, /this\.hexSelectionHandler\?\.\(selected\)/);
    assert.doesNotMatch(view, /setHexSelectionHandler[\s\S]{0,800}advanceExpedition/);
});

test("current-cell adjacency is memoized across unchanged render and summary reads", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /let adjacencyCache:/);
    assert.match(view, /if \(adjacencyCache\?\.key !== key\)/);
    assert.match(view, /currentHexAdjacency\(cell, orientation, null, rotation\)/);
    assert.match(view, /const base = adjacencyCache\.value/);
    assert.match(view, /selectedEdgeId: selected\?\.id \?\? null/);
    const keyBlock = view.slice(
        view.indexOf("const key = ["),
        view.indexOf("].join", view.indexOf("const key = [")));
    assert.doesNotMatch(keyBlock, /preferences\.direction/);
});

test("Phase 15 workspace rerenders preserve the existing map surface and viewport lifecycle", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const map = fs.readFileSync(path.join(sourceDir, "map-surface.ts"), "utf8");

    const renderStart = view.indexOf("const render = (): void =>");
    const presentationStart = view.indexOf("const presentation =", renderStart);
    assert.notEqual(renderStart, -1);
    assert.notEqual(presentationStart, -1);
    assert.doesNotMatch(view.slice(renderStart, presentationStart), /map\?\.dispose\(\)/);

    assert.match(view, /if \(map\) \{\s*map\.attach\(host\)/);
    assert.match(view, /map = new MapSurface\(host, \(\) => world\)/);
    assert.match(view, /map\.renderer\.selectedHex = selectedHex/);
    assert.match(map, /public attach\(host: HTMLElement\): void/);
    assert.match(map, /this\.resizeObserver\.disconnect\(\)/);
    assert.match(map, /host\.replaceChildren\(this\.canvas, this\.accessibilityHelp, this\.accessibilityStatus\)/);
    assert.match(map, /this\.resizeObserver\.observe\(host\)/);
});

test("focused travel drawer explicitly removes its course and pace listeners on cleanup", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /directionControl\?\.removeEventListener\("change", captureTravelPreferencesFromControls\)/);
    assert.match(view, /paceControl\?\.removeEventListener\("change", captureTravelPreferencesFromControls\)/);
    assert.match(view, /controller\.dispose\(\)/);
});

test("navigator controls keep visible focus and practical pointer targets at narrow widths", () => {
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(styles, /\.hc-adjacency-edge:focus-visible/);
    assert.match(styles, /outline:4px solid var\(--hc-focus\)/);
    assert.match(styles, /min-width:2\.75rem; min-height:2\.75rem/);
    assert.match(styles, /@media \(max-width: 760px\)[\s\S]*\.hc-adjacency-edge \{ min-width:2\.75rem; min-height:2\.75rem/);
});

test("Phase 15 focused editing uses accessible drawers and keeps secondary tools out of the primary surface", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const workspace = fs.readFileSync(path.join(sourceDir, "ui/workspace.ts"), "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(view, /openWorkspaceDrawer/);
    assert.match(view, /disclosure\("GM Tools", false\)/);
    assert.match(view, /openTravelWorkspace/);
    assert.match(view, /openPartyWorkspace/);
    assert.match(view, /openEnvironmentWorkspace/);
    assert.match(view, /openSurvivalWorkspace/);
    assert.match(view, /openJourneyWorkspace/);
    assert.match(workspace, /role", "dialog"/);
    assert.match(workspace, /aria-modal/);
    assert.match(workspace, /Escape/);
    assert.match(workspace, /onClose\?\.\(\)/);
    assert.match(workspace, /returnFocus\?\.isConnected/);
    assert.match(styles, /hc-focus-workspace/);
    assert.match(styles, /@media \(max-width: 760px\)/);
});
test("Phase 15 spatial travel uses semantic current-cell adjacency and focused navigation", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const adjacency = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/spatial-adjacency.ts"),
        "utf8");
    const model = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-workspace-model.ts"),
        "utf8");
    const obsolete = path.join(sourceDir, "modules/expeditions/phase15-expedition-workspace.ts");

    assert.match(adjacency, /createCurrentCellAdjacency/);
    assert.match(adjacency, /CurrentCellAdjacency/);
    assert.match(adjacency, /currentHexAdjacency/);
    assert.match(adjacency, /center: polygonCenter\(polygon\)/);
    assert.match(adjacency, /adjacencyFeedbackVector/);
    assert.doesNotMatch(adjacency, /edges\.length === 6/);
    assert.match(view, /aria-label", "Current-cell adjacent travel"/);
    assert.match(view, /aria-pressed/);
    assert.match(view, /current actual resolved course/);
    assert.match(view, /recordNavigationAssistant/);
    assert.match(view, /openNavigationWorkspace/);
    assert.match(model, /navigationResolutionDue/);
    assert.match(model, /kind: "boundary"/);
    assert.match(model, /kind: "survival"/);
    assert.equal(fs.existsSync(obsolete), false);
    assert.doesNotMatch(view, /getAttribute\("aria-label"\)/);
});


test("Compact authoring offers generic one-click dependency repair", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const service = fs.readFileSync(
        path.join(repositoryRoot, "src/HexCrawl.Application/Expeditions/ProcedureComposerService.cs"),
        "utf8");

    assert.match(workspace, /draft\.dependencyFixes\[0\]/);
    assert.match(workspace, /Add required companion rules/);
    assert.match(workspace, /repairLabel = required\.length <= 2/);
    assert.doesNotMatch(workspace, /Fix dependency issues/);
    assert.match(workspace, /fix\.moduleKeys\.map\(moduleKey => \[moduleKey, true\]/);
    assert.match(service, /SuggestDependencyFixes/);
    assert.match(service, /candidates\.Length == 1/);
    assert.match(service, /CurrentReport\(\)\.HasErrors/);
    assert.doesNotMatch(service, /PresetKey.*dependency/i);
});


test("persisted travel intent does not hardcode a six-edge direction range", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /Number\.isInteger\(parsed\.direction\) && Number\(parsed\.direction\) >= 0/);
    assert.doesNotMatch(view, /Number\(parsed\.direction\) <= 5/);
});

test("current-cell navigator stays orientation-neutral unless authoritative compass metadata exists", () => {
    const adjacency = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/spatial-adjacency.ts"),
        "utf8");
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(adjacency, /screenRelativeEdgeLabel/);
    assert.match(adjacency, /outwardArrow/);
    assert.match(adjacency, /rotationDegrees/);
    assert.doesNotMatch(adjacency, /Northeast|Northwest|Southeast|Southwest/);
    assert.match(view, /Travel through \$\{identity\}/);
    assert.match(view, /edgeCourseLabel/);
    assert.doesNotMatch(view, /data-travel-primary/);
});


test("navigator feedback preserves edge centering and moves along geometry instead of the global button press direction", () => {
    const adjacency = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/spatial-adjacency.ts"),
        "utf8");
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const phaseStyles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");
    const baseStyles = fs.readFileSync(path.join(sourceDir, "styles.ts"), "utf8");

    assert.match(baseStyles, /button:active:not\(:disabled\) \{ transform: translateY\(1px\); \}/);
    assert.match(adjacency, /adjacencyFeedbackVector/);
    assert.match(adjacency, /Math\.hypot\(dx, dy\)/);
    assert.match(view, /--hc-edge-feedback-x/);
    assert.match(view, /--hc-edge-feedback-y/);
    assert.match(phaseStyles, /button\.hc-adjacency-edge:active:not\(:disabled\)/);
    assert.match(
        phaseStyles,
        /translate\(calc\(-50% \+ var\(--hc-edge-feedback-x\)\),calc\(-50% \+ var\(--hc-edge-feedback-y\)\)\)/);
});

test("lost-boundary resolution is a dedicated focused mutation and does not require hidden travel input", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const api = fs.readFileSync(path.join(sourceDir, "api.ts"), "utf8");

    assert.match(view, /const openBoundaryWorkspace = \(\): void =>/);
    assert.match(view, /api\.resolveBoundaryDecision/);
    assert.match(view, /case "boundary":[\s\S]*openBoundaryWorkspace\(\)/);
    const boundary = view.slice(
        view.indexOf("const openBoundaryWorkspace"),
        view.indexOf("const openTravelReviewWorkspace"));
    assert.match(boundary, /Party recognizes it is lost/);
    assert.match(boundary, /Party reorients/);
    assert.doesNotMatch(boundary, /effectiveDistance|expectedDistance|actualDistance|hexSteps/);
    assert.match(api, /\/boundary-decision/);
});

test("urgent travel pauses preserve their task wording and use a focused course-and-pace review", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /action\.kind === "travel"[\s\S]*&& !action\.urgent/);
    assert.match(view, /const openTravelReviewWorkspace = \(\): void =>/);
    assert.match(view, /"Changed travel conditions"/);
    assert.match(view, /"Backtrack boundary"/);
    assert.match(view, /continueTravel\(false, true\)/);
    assert.match(view, /case "review":[\s\S]*openTravelReviewWorkspace\(\)/);
    const review = view.slice(
        view.indexOf("const openTravelReviewWorkspace"),
        view.indexOf("const openNavigationWorkspace"));
    assert.match(review, /labelled\("Course", course\)/);
    assert.match(review, /labelled\("Pace \/ travel mode", pace\)/);
    assert.doesNotMatch(review, /suppressNav|resetVeer|continueAcross|doubleBack/);
});

test("encounter resume requires an explicit resolved-at-table acknowledgement", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");

    assert.match(view, /Encounter resolved — continue travel/);
    assert.match(view, /continueTravel\(true\)/);
    assert.match(controller, /runtime\.pauseReason === "EncounterTriggered" && !resumeEncounter/);
});

test("normal spatial travel has one primary continuation path and focused unresolved workspaces", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const primary = button\(actionLabel, \(\) => activateAction\(action.kind\)\)/);
    assert.doesNotMatch(view, /const continueControl = button\("Continue travel"/);
    assert.match(view, /actions\.append\(changePace, more\)/);
    assert.doesNotMatch(view, /button\("Travel controls"/);
    assert.match(view, /const continueTravel = \(resumeEncounter = false, resumeTravelReview = false\): void =>/);
    assert.match(view, /spatialTravelContinuationTarget/);
    assert.match(view, /case "navigation":[\s\S]*openNavigationWorkspace\(\)/);
    assert.match(view, /case "encounter":[\s\S]*openTravelWorkspace\("encounter"\)/);
    assert.match(view, /case "movement":[\s\S]*openTravelWorkspace\("movement"\)/);
    assert.match(view, /case "boundary":[\s\S]*openBoundaryWorkspace\(\)/);
    assert.match(view, /case "survival":[\s\S]*openSurvivalWorkspace\(\)/);
    assert.match(view, /ExpeditionWatchController\.continueResolvedTravel/);
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");
    assert.match(controller, /return api\.advanceExpedition\(runtime\.id, request\)/);
    assert.match(view, /button\("More options", \(\) => openTravelWorkspace\("advanced"\)\)/);
    assert.doesNotMatch(view, /openTravelWorkspace\("travel"\)/);
});

test("desktop hierarchy exposes current travel before the map and compacts summary chrome", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    const mapPanel = view.slice(
        view.indexOf('primary.className = "hc-panel hc-map-panel"'),
        view.indexOf('const secondary = document.createElement("aside")'));
    assert.ok(mapPanel.indexOf("renderCurrentTravel()") < mapPanel.indexOf("primary.append(frame)"));
    assert.match(styles, /@media \(min-width: 1041px\)[\s\S]*\.hc-current-action \{ grid-template-columns:/);
    assert.match(styles, /\.hc-current-travel \{ grid-template-columns:auto minmax\(0,1fr\) auto/);
    assert.match(styles, /@media \(min-width: 1200px\)[\s\S]*\.hc-phase15-expedition \.hc-stat-action-grid \{ grid-template-columns:repeat\(8,minmax\(0,1fr\)\)/);
});

test("current travel stays beside the map while selection detail is contextual overlay", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(view, /context\.className = "hc-map-context-overlay"/);
    assert.match(view, /context\.hidden = true/);
    assert.match(view, /frame\.append\(host, renderAdjacencyNavigator\(\), context\)/);
    assert.match(view, /if \(!selectedHex\) \{\s*host\.hidden = true;/);
    assert.match(view, /host\.hidden = false;\s*host\.append\(textElement\("h3", "Selected map cell"\)\)/);
    assert.match(view, /section\.className = "hc-current-travel"/);
    assert.match(view, /Current travel/);
    assert.match(view, /Change pace/);
    assert.match(styles, /\.hc-map-context-overlay \{ position:absolute/);
    assert.match(styles, /\.hc-phase15-expedition \.hc-map-host \{ height:clamp\(28rem,58vh,46rem\); min-height:0; \}/);
    assert.match(styles, /\.hc-phase15-expedition\.hc-page \{ max-width:none; \}/);
    assert.match(styles, /\.hc-rail-action/);
});

test("focused watch presentation hides unrelated exceptional controls outside More options", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(view, /focus: "advanced" \| "movement" \| "encounter"/);
    assert.match(view, /if \(focus === "advanced"\) return/);
    assert.match(view, /group\.hidden = !keys\.includes\(focus\)/);
    assert.match(view, /const genericHelper = body\.querySelector<HTMLElement>\("\[data-resolution-helper\]"\)/);
    assert.match(view, /genericHelper\.classList\.add\("hc-focused-hidden"\)/);
    assert.match(styles, /\.hc-focused-hidden \{ display:none !important; \}/);
    assert.match(view, /data-plan-fields data-focus-group="advanced"/);
    assert.match(view, /data-travel-resolution data-focus-group="advanced movement"/);
    assert.match(view, /data-encounter-resolution data-focus-group="advanced encounter"/);
    assert.match(view, /data-boundary-resolution data-focus-group="advanced"/);
    assert.match(view, /details data-focus-group="advanced"/);
});

test("DM can reposition the party without routing through ordinary travel procedure advancement", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const api = fs.readFileSync(path.join(sourceDir, "api.ts"), "utf8");

    assert.match(view, /button\("Teleport party", \(\) => openRepositionWorkspace\(selectedHex\)\)/);
    assert.match(view, /button\("Teleport party here", \(\) => openRepositionWorkspace\(selectedHex\)\)/);
    assert.match(view, /Teleport party directly repositions the party without resolving travel/);
    assert.match(view, /api\.repositionExpedition/);
    assert.match(view, /preferences\.direction = null/);
    assert.match(view, /q\.required = true/);
    assert.match(view, /r\.required = true/);
    assert.match(view, /void runUiMutation\(async \(\) => \{\s*if \(!q\.value\.trim\(\) \|\| !r\.value\.trim\(\)\)/);
    assert.match(api, /\/api\/expeditions\/\$\{encodeURIComponent\(expeditionId\)\}\/reposition/);
});

test("Compact procedure reference does not claim display order is authoritative", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.match(workspace, /grouped for quick reference/);
    assert.match(workspace, /configured trigger or dependency/);
    assert.doesNotMatch(workspace, /Run these rules in watch order/);
});


test("travel-target map selection follows persisted course across authoritative runtime movement", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /let selectedHexTracksTravelIntent = false/);
    assert.match(view, /selectedHexTracksTravelIntent = true/);
    assert.match(view, /if \(selectedHexTracksTravelIntent && runtime\.expedition\.isSpatial && preferences\.direction !== null\)/);
    assert.match(view, /adjacencyEdgeForDirection\(adjacency, preferences\.direction\)\?\.targetCell/);
    assert.match(view, /if \(hex\)[\s\S]*selectTravelIntent\(edge\.directionValue, edge\.targetCell\);[\s\S]*selectedHexTracksTravelIntent = false;/);
});


test("navigation summary can not record an out-of-sequence focused resolution", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const navigationDue = navigationResolutionDue\(runtime, false, false\)/);
    assert.match(view, /navigationDue \? "Navigation required" : "Navigation status"/);
    assert.match(view, /if \(!navigationDue\) \{/);
    assert.match(view, /No navigation resolution is due for the current watch state/);
    const guard = view.indexOf("if (!navigationDue)");
    const form = view.indexOf('const form = document.createElement("form")', guard);
    assert.ok(guard >= 0 && form > guard);
});


test("only lost recognition routes through explicit boundary-decision controls", () => {
    const model = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-workspace-model.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");

    assert.match(model, /pauseReason === "LostRecognitionRequired"[\s\S]*kind: "boundary"/);
    assert.match(model, /pauseReason === "BacktrackBoundaryReached"[\s\S]*kind: "travel"/);
    assert.match(controller, /pauseReason !== "LostRecognitionRequired"/);
    assert.match(controller, /if \(runtime\.pauseReason === "LostRecognitionRequired"\)/);
});


test("Compact spatial operation does not require axial coordinates", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const model = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-workspace-model.ts"),
        "utf8");

    assert.match(model, /Current cell ·/);
    assert.doesNotMatch(model, /Hex \$\{state\.currentHex\.q\}/);
    assert.match(view, /Selected map cell/);
    assert.doesNotMatch(view, /Selected cell \$\{selectedHex\.q\}/);
    assert.doesNotMatch(view, /return `Hex \$\{runtime\.expedition\.currentHex\.q\}/);
});


test("Compact travel reference does not imply sequence through numbered markup", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(workspace, /const list = document\.createElement\("div"\)/);
    assert.match(workspace, /compactRuleCard\(module, false\)/);
    assert.doesNotMatch(styles, /hc-procedure-step-list > li::marker/);
});


test("focused navigation retains access to the verified procedure-helper path", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    const navigation = view.slice(
        view.indexOf("const openNavigationWorkspace"),
        view.indexOf("const applyTravelPreferences"));
    assert.match(navigation, /Advanced travel controls/);
    assert.match(navigation, /openTravelWorkspace\("advanced"\)/);
    assert.doesNotMatch(navigation, /resolutionSource:\s*"AutomaticRoll"/);
});


test("focused navigation does not silently choose the first edge", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    const navigation = view.slice(
        view.indexOf("const openNavigationWorkspace"),
        view.indexOf("const applyTravelPreferences"));
    assert.match(navigation, /unselectedCourse\.value = ""/);
    assert.match(navigation, /Select intended adjacent cell/);
    assert.match(navigation, /if \(course\.value === ""\)/);
});


test("secondary travel course changes synchronize navigator and map target", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    const capture = view.slice(
        view.indexOf("const captureTravelPreferences"),
        view.indexOf("const openPartyWorkspace"));
    assert.match(capture, /adjacencyEdgeForDirection\(adjacency, parsed\)/);
    assert.match(capture, /selectedHex = edge\.targetCell/);
    assert.match(capture, /selectedHexTracksTravelIntent = true/);
    assert.match(capture, /map\.renderer\.selectedHex = edge\.targetCell/);
    assert.match(capture, /syncTravelIntentControls\(\)/);
});
