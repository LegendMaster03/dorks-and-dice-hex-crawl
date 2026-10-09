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

    assert.match(workspace, /Start from a known ruleset/);
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

    assert.match(workspace, /procedurePresentationSections/);
    assert.ok((workspace.match(/renderPresentationFactGroups\(/g) ?? []).length >= 2);
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

test("navigator accent follows the host site primary token without changing shared focus semantics", () => {
    const styles = fs.readFileSync(path.join(sourceDir, "styles.ts"), "utf8");
    const phaseStyles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(styles, /--hc-primary: var\(--bs-primary, #6557d2\)/);
    assert.equal((styles.match(/--hc-primary: var\(--bs-primary, #6d61dc\);/g) ?? []).length, 2);
    assert.equal((styles.match(/--hc-focus: #6557d2;/g) ?? []).length, 1);
    assert.equal((styles.match(/--hc-focus: #a99df5;/g) ?? []).length, 2);
    assert.match(phaseStyles, /\.hc-adjacency-interface\.is-selected \.hc-adjacency-arrow-shape path \{ stroke:var\(--hc-primary\); \}/);
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

    assert.match(workspace, /Ruleset name/);
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
    const intent = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-travel-intent.ts"),
        "utf8");
    const navigator = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/cell-navigator.ts"),
        "utf8");

    assert.match(navigator, /hc-adjacency-navigator/);
    assert.match(view, /currentRuntimeCellAdjacency/);
    assert.match(view, /adjacencyForCell/);
    assert.match(intent, /hex-crawl\.expedition\.\$\{expeditionId\}\.travel-intent/);
    assert.match(view, /Reusable travel direction and pace stay filled until changed/);
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
    assert.match(view, /selectTravelIntent\(edge\.intentValue\)/);
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

test("current-cell topology is memoized independently from selected intent", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /let adjacencyCache:/);
    assert.match(view, /if \(adjacencyCache\?\.key !== key\)/);
    assert.match(view, /currentRuntimeCellAdjacency\(\{/);
    assert.match(view, /tilingGjhNotation: runtime\.procedure\.tilingGjhNotation/);
    assert.match(view, /selectedDirection: null/);
    assert.match(view, /const base = adjacencyCache\.value/);
    assert.match(view, /selectedAdjacencyId: selected\?\.id \?\? null/);
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

    assert.match(view, /directionControl\?\.removeEventListener\("change", captureTravelDirectionFromControls\)/);
    assert.match(view, /paceControl\?\.removeEventListener\("change", captureTravelPaceFromControls\)/);
    assert.match(view, /controller\.dispose\(\)/);
});

test("navigator renders supplied cell boundary and adjacency interfaces with consistent arrow affordance", () => {
    const navigator = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/cell-navigator.ts"),
        "utf8");
    const adjacency = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/spatial-adjacency.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(navigator, /adjacency\.boundary/);
    assert.match(navigator, /for \(const candidate of adjacency\.adjacencies\)/);
    assert.match(navigator, /const vector = candidate\.outwardVector/);
    assert.match(navigator, /candidate\.anchor\.x \+ vector\.x \* 0\.045/);
    assert.match(navigator, /data.*adjacencyInterfaceId|dataset\.adjacencyInterfaceId/);
    assert.match(navigator, /hc-adjacency-arrow-shape/);
    assert.match(navigator, /M2 15 H38 V4 L62 20 L38 36 V25 H2 Z/);
    assert.match(adjacency, /boundarySegment/);
    assert.match(adjacency, /outwardVector/);
    assert.doesNotMatch(adjacency, /HexCoordinate|HexOrientation|PointyTop|FlatTop|axialSteps/);
    assert.match(styles, /\.hc-adjacency-arrow-shape path/);
    assert.match(styles, /fill:#fff/);
    assert.match(styles, /stroke:transparent/);
    assert.match(styles, /#tool-root\.hex-crawl-app button\.hc-adjacency-interface \{/);
    assert.match(styles, /outline:3px solid var\(--hc-primary\)/);
    assert.match(styles, /min-width:2\.75rem; min-height:2\.75rem/);
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
    assert.doesNotMatch(adjacency, /HexCoordinate|HexOrientation|PointyTop|FlatTop/);
    assert.match(adjacency, /center: polygonCenter\(boundary\)/);
    assert.match(adjacency, /outwardVector: AdjacencyPoint/);
    assert.doesNotMatch(adjacency, /adjacencies\.length === 6/);
    assert.match(view, /renderCurrentCellNavigator/);
    assert.match(view, /renderCurrentCellNavigator/);
    assert.match(view, /actualIntent: runtime\.expedition\.actualDirection/);
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


test("persisted browser travel preferences do not own course direction or hardcode topology", () => {
    const intent = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-travel-intent.ts"),
        "utf8");

    assert.match(intent, /direction: fallback\.direction/);
    assert.match(intent, /JSON\.stringify\(\{ pace: preferences\.pace \}\)/);
    assert.doesNotMatch(intent, /parsed\.direction/);
    assert.doesNotMatch(intent, /direction\s*[<>]=?\s*[56]/);
});

test("current-cell navigator stays orientation-neutral unless authoritative compass metadata exists", () => {
    const adjacency = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/spatial-adjacency.ts"),
        "utf8");
    const topology = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/current-cell-topology.ts"),
        "utf8");
    const navigator = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/cell-navigator.ts"),
        "utf8");
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(adjacency, /screenRelativeAdjacencyLabel/);
    assert.match(topology, /hexCorners\(input\.worldGrid, input\.currentCell\)/);
    assert.doesNotMatch(adjacency + topology + navigator, /Northeast|Northwest|Southeast|Southwest/);
    assert.match(navigator, /Travel through \$\{candidate\.label\}/);
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

    const navigator = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/cell-navigator.ts"),
        "utf8");
    assert.match(baseStyles, /button:active:not\(:disabled\) \{ transform: translateY\(1px\); \}/);
    assert.doesNotMatch(navigator, /adjacency\.center.*candidate\.anchor/);
    assert.match(navigator, /candidate\.outwardVector/);
    assert.match(navigator, /--hc-adjacency-feedback-x/);
    assert.match(navigator, /--hc-adjacency-feedback-y/);
    assert.match(phaseStyles, /button\.hc-adjacency-interface:active:not\(:disabled\)/);
    assert.match(
        phaseStyles,
        /translate\(calc\(-50% \+ var\(--hc-adjacency-feedback-x\)\),calc\(-50% \+ var\(--hc-adjacency-feedback-y\)\)\)/);
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

test("urgent travel pauses preserve valid reusable course and ask only for changed inputs", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /action\.kind === "travel"[\s\S]*&& !action\.urgent/);
    assert.match(view, /const openTravelReviewWorkspace = \(\): void =>/);
    assert.match(view, /"Changed travel conditions"/);
    assert.match(view, /"Backtrack boundary"/);
    assert.match(view, /continueTravel\(true\)/);
    assert.match(view, /case "review":[\s\S]*openTravelReviewWorkspace\(\)/);
    const review = view.slice(
        view.indexOf("const openTravelReviewWorkspace"),
        view.indexOf("const openNavigationWorkspace"));
    assert.match(review, /const intendedEdge = preferences\.direction === null/);
    assert.match(review, /contextLine\(\s*"Course"/);
    assert.match(review, /labelled\("Pace \/ travel mode", pace\)/);
    assert.match(review, /button\("Change travel direction"/);
    assert.match(review, /button\("Choose travel direction"/);
    assert.match(review, /previous course is not available from the current cell/);
    assert.doesNotMatch(review, /labelled\("Course", course\)/);
    assert.doesNotMatch(review, /Select intended adjacent cell/);
    assert.doesNotMatch(review, /preferences\.direction\s*=(?!=)/);
    assert.doesNotMatch(review, /suppressNav|resetVeer|continueAcross|doubleBack/);
});

test("encounter resume requires an explicit resolved-at-table acknowledgement", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");

    assert.match(view, /api\.resolveEncounter/);
    assert.match(view, /pending\.triggerSequence/);
    assert.doesNotMatch(view, /Encounter resolved — continue travel/);
    assert.doesNotMatch(controller, /resumeEncounter/);
});

test("normal spatial travel has one primary continuation path and focused unresolved workspaces", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const primary = button\(copy\.label, \(\) => activateAction\(action.kind\)\)/);
    assert.doesNotMatch(view, /const continueControl = button\("Continue travel"/);
    assert.match(view, /if \(paceEditor && pace\)[\s\S]*actions\.append\(changePace\)/);
    assert.match(view, /actions\.append\(more\)/);
    assert.doesNotMatch(view, /button\("Travel controls"/);
    assert.match(view, /const continueTravel = \(resumeTravelReview = false\): void =>/);
    assert.match(view, /spatialTravelContinuationTarget/);
    assert.match(view, /case "navigation":[\s\S]*openNavigationWorkspace\(\)/);
    assert.match(view, /case "encounter":[\s\S]*openTravelWorkspace\("encounter"\)/);
    assert.match(view, /case "movement":[\s\S]*openTravelWorkspace\("movement"\)/);
    assert.match(view, /case "boundary":[\s\S]*openBoundaryWorkspace\(\)/);
    assert.match(view, /case "survival":[\s\S]*openSurvivalWorkspace\("attention"\)/);
    assert.match(view, /ExpeditionWatchController\.continueResolvedTravel/);
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");
    assert.match(controller, /return api\.advanceExpedition\(runtime\.id, request\)/);
    assert.match(view, /button\("More options", \(\) => openTravelWorkspace\("advanced"\)\)/);
    assert.doesNotMatch(view, /openTravelWorkspace\("travel"\)/);
});

test("narrow hierarchy keeps the map primary while rail context stacks without disappearing", () => {
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");
    const capture = fs.readFileSync(
        path.join(sourceDir, "..", "visual-review", "capture-phase15.sh"),
        "utf8");
    const fixture = fs.readFileSync(
        path.join(sourceDir, "..", "visual-review", "phase15.ts"),
        "utf8");

    assert.match(styles, /@media \(max-width: 760px\)[\s\S]*\.hc-phase15-expedition > \.hc-workspace-grid,[\s\S]*order:2/);
    assert.match(styles, /\.hc-phase15-expedition > \.hc-phase15-runtime-summary \{ order:3; \}/);
    assert.match(styles, /\.hc-phase15-expedition \.hc-map-panel > \.hc-map-frame \{ order:2; \}/);
    assert.match(styles, /\.hc-table-rail \{ grid-template-columns:1fr; \}/);
    assert.match(capture, /24-responsive-narrow\|selected-edge\|light\|500\|844\|390/);
    assert.match(capture, /narrow map does not begin in the initial viewport/);
    assert.match(fixture, /mapTop:/);
    assert.match(fixture, /currentTravelInRail:/);
});

test("desktop hierarchy moves current travel into the actionable rail so the map begins higher", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    const mapPanel = view.slice(
        view.indexOf('primary.className = "hc-panel hc-map-panel"'),
        view.indexOf('const secondary = document.createElement("aside")'));
    assert.match(mapPanel, /if \(!world\) primary\.append\(renderCurrentTravel\(\)\)/);
    assert.equal((mapPanel.match(/renderCurrentTravel\(\)/g) ?? []).length, 1);
    assert.match(view, /if \(world\) secondary\.append\(renderCurrentTravel\(\)\)/);
    assert.match(view, /partyRailContext\(runtime\)/);
    assert.match(view, /environmentRailDetail\(survival\)/);
    assert.doesNotMatch(view, /railAction\("Party & activities"/);
    assert.match(styles, /@media \(min-width: 1041px\)[\s\S]*\.hc-table-rail \.hc-current-travel \{ grid-template-columns:1fr/);
    assert.match(styles, /\.hc-table-rail > \.hc-current-travel \{ min-width:0; \}/);
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

test("course selection immediately updates the Next action copy without moving the party", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const currentActionCopy =/);
    assert.match(view, /data-current-action-label/);
    assert.match(view, /data-current-action-detail/);
    assert.match(view, /data-current-action-button/);
    assert.match(view, /const copy = currentActionCopy\(action\);[\s\S]*nextLabel\.textContent = copy\.label/);
});

test("no-course travel identifies course selection as the immediate task", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const courseRequired = routineSpatialTravel && preferences\.direction === null/);
    assert.match(view, /courseRequired\s*\? "Choose travel direction"/);
    assert.match(view, /selecting a direction does not move the party/);
});

test("focused expedition panels accept the drawer body itself as their page host", () => {
    for (const file of [
        "survival-resources-panel.ts",
        "journey-panel.ts",
        "environment-panel.ts"
    ]) {
        const source = fs.readFileSync(
            path.join(sourceDir, "modules/expeditions", file),
            "utf8");
        assert.match(source, /root\.matches\("\.hc-page"\)/);
    }
});

test("forced travel and pending consequences use focused survival workspaces", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const panel = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/survival-resources-panel.ts"),
        "utf8");

    assert.match(view, /openSurvivalWorkspace\("attention"\)/);
    assert.match(view, /\? "forcedTravel"\s*:\s*"pendingResourceConsequences"/);
    assert.match(view, /\? "Forced travel"\s*:\s*panelFocus === "pendingResourceConsequences"/);
    assert.match(panel, /export type SurvivalResourcesPanelFocus/);
    assert.match(panel, /this\.focus === "forcedTravel"/);
    assert.match(panel, /this\.focus === "pendingResourceConsequences"/);
});

test("rendered review exercises focused forced travel and navigator focus", () => {
    const capture = fs.readFileSync(
        path.join(sourceDir, "..", "visual-review", "capture-phase15.sh"),
        "utf8");
    const fixture = fs.readFileSync(
        path.join(sourceDir, "..", "visual-review", "phase15.ts"),
        "utf8");

    assert.match(capture, /"forced-travel-pending": "Forced travel"/);
    assert.match(capture, /metrics\["drawerCount"\] != 1/);
    assert.match(capture, /forcedTravelPrimaryDomainFacing/);
    assert.match(fixture, /findButton\("Resolve forced travel"\)\?\.click\(\)/);
    assert.match(fixture, /focusedEdge:/);
});

test("focused travel workspaces replace raw Run watch wording with the current tabletop task", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const watchSummary = body\.querySelector<HTMLElement>\("\[data-watch-summary\]"\)/);
    assert.match(view, /focus === "movement"\s*\? "Travel segment"\s*:\s*"Encounter resolution"/);
    assert.match(view, /if \(focus === "advanced"\) return/);
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
    assert.match(view, /if \(!edge && !sameHexCell\(runtime\.expedition\.currentHex, selectedHex\)\)[\s\S]*button\("Teleport party here", \(\) => openRepositionWorkspace\(selectedHex\)\)/);
    assert.match(view, /Teleport party directly repositions the party without resolving travel/);
    assert.match(view, /api\.repositionExpedition/);
    assert.match(view, /preferences\.direction = null/);
    assert.match(view, /q\.required = true/);
    assert.match(view, /r\.required = true/);
    assert.match(view, /void runUiMutation\(async \(\) => \{\s*if \(!q\.value\.trim\(\) \|\| !r\.value\.trim\(\)\)/);
    assert.match(api, /\/api\/expeditions\/\$\{encodeURIComponent\(expeditionId\)\}\/reposition/);
});

test("journey-first primary workspace projects current process state without spatial controls", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /main\.append\(textElement\("h2", "Journey"\)\)/);
    assert.match(view, /journeyFact\("Current stage"/);
    assert.match(view, /journeyFact\("Progress"/);
    assert.match(view, /journeyFact\("Roles"/);
    assert.match(view, /journeyFact\("Pending"/);
    assert.match(view, /journeyFact\("Consequences \/ state"/);
    assert.match(view, /No journey process is currently active/);
    assert.match(view, /This procedure advances its configured interval without spatial position, course, pace, hex progress, or map state/);
});

test("journey summary uses the authoritative effects projection for pending consequence state", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /journeyStateSummary\(stageState, survival, effects\)/);
    assert.match(view, /effectState\.pendingConsequences\.length/);
    assert.match(view, /consequence\$\{count === 1 \? "" : "s"\} need/);
    assert.match(view, /No pending consequences/);
    assert.doesNotMatch(view, /No unresolved consequence/);
});

test("normal journey and expedition history humanize audit records while Advanced retains raw identity", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const journey = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/journey-panel.ts"),
        "utf8");

    assert.match(view, /item\.textContent = journeyHistoryLabel\(record\.kind\)/);
    assert.match(view, /Journey \$\{record\.kind\} · \$\{record\.detail\}/);
    assert.match(journey, /this\.heading\("Pending journey event"\)/);
    assert.match(journey, /"Journey history",[\s\S]*journeyHistoryLabel\(record\.kind\)/);
    assert.match(journey, /record\.detail.*record\.resolutionId.*record\.eventOccurrenceId/s);
    assert.match(journey, /case "ResolutionRecorded": return "Journey result recorded"/);
});

test("nonspatial GM utilities do not expose spatial repositioning or travel-order controls", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /runtime\.expedition\.isSpatial \? "Party & travel order" : "Party & roles"/);
    assert.match(view, /if \(runtime\.expedition\.isSpatial\) \{\s*row\.append\(button\("Teleport party"/);
    assert.match(view, /if \(presentation\.capabilities\.journey\) \{\s*row\.append\(button\("Journey"/);
});

test("nonspatial summary only renders movement when the materialized procedure exposes travel capability", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /if \(presentation\.capabilities\.travel\) \{[\s\S]*stats\.append\(statAction\([\s\S]*"Movement"/);
    assert.match(view, /state\.eventOccurrences\.some\(event => event\.status === "ResolutionRequired"\)/);
});

test("procedure-defined travel modes use finite controls while no-choice pace stays fixed", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const types = fs.readFileSync(path.join(sourceDir, "types.ts"), "utf8");

    assert.match(types, /travelModeKeys: string\[\]/);
    assert.match(view, /const availableTravelModes = \(\): string\[\] => runtime\.movementComposition\.policy\.travelModeKeys/);
    assert.match(view, /if \(choices\.length <= 1\) return null/);
    assert.match(view, /document\.createElement\("select"\)/);
    assert.match(view, /if \(travelModes\.length <= 1\)[\s\S]*type="hidden"/);
    assert.doesNotMatch(view, /control\.type = "text"/);
    assert.match(view, /distanceInputLabel\("Effective distance", distanceUnit\)/);
    assert.match(view, /runtime\.context\.hexCenterDistance\?\.unit\.symbol/);
});

test("focused encounter cadence uses its assistant instead of hidden travel provenance", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const controller = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-watch-controller.ts"),
        "utf8");

    assert.match(view, /focus === "encounter" \? "encounterCadence" : "advance"/);
    assert.match(controller, /this\.submissionMode === "encounterCadence"/);
    assert.match(controller, /recordEncounterAssistant/);
    assert.match(controller, /readResolutionSource\("encounterSource", "encounter", false\)/);
    assert.match(controller, /No encounter check is currently due/);
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
    assert.match(view, /const synchronizeTravelTargetProjection = \(\): void =>/);
    assert.match(view, /synchronizeTravelTargetProjection\(\);/);
    assert.match(view, /selectedHexTracksTravelIntent = selectedHex !== null/);
    assert.match(view, /if \(selectedHex !== null && !selectedHexTracksTravelIntent\) return/);
    assert.match(view, /selectedHex = adjacency[\s\S]*adjacencyForIntent\(adjacency, preferences\.direction\)\?\.targetCell/);
    assert.match(view, /preferences = mergeRuntimeTravelPreferences\(runtime, preferences\);[\s\S]*synchronizeTravelTargetProjection\(\);/);
    assert.match(view, /if \(hex\)[\s\S]*selectTravelIntent\(edge\.intentValue\);[\s\S]*selectedHexTracksTravelIntent = false;/);
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


test("focused navigation reuses valid intended course and routes missing intent back to course selection", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    const navigation = view.slice(
        view.indexOf("const openNavigationWorkspace"),
        view.indexOf("const applyTravelPreferences"));
    assert.match(navigation, /const intendedEdge = preferences\.direction === null/);
    assert.match(navigation, /contextLine\("Intended travel direction", intendedEdge/);
    assert.match(navigation, /Choose an adjacent course before resolving navigation/);
    assert.match(navigation, /button\("Choose travel direction"/);
    assert.match(navigation, /button\("Change travel direction"/);
    assert.match(navigation, /const direction = intendedEdge\.intentValue/);
    assert.doesNotMatch(navigation, /name = "intendedDirection"/);
    assert.doesNotMatch(navigation, /labelled\("Intended course", course\)/);
});


test("secondary travel course changes use the same authoritative course mutation", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    const workspace = view.slice(
        view.indexOf("const openTravelWorkspace"),
        view.indexOf("const openRepositionWorkspace"));
    assert.match(workspace, /captureTravelDirectionFromControls/);
    assert.match(workspace, /adjacencyForIntent\(adjacency, parsed\)/);
    assert.match(workspace, /selectTravelIntent\(edge\.intentValue\)/);
    assert.doesNotMatch(workspace, /preferences\.direction = parsed/);
});

test("course intent UI serializes server mutations so rapid clicks can not race the same version", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /let courseIntentMutationPending = false/);
    assert.match(view, /if \(!runtime\.expedition\.isSpatial \|\| courseIntentMutationPending\) return/);
    assert.match(view, /setCourseIntentMutationPending\(true\)/);
    assert.match(view, /\[data-adjacency-interface-id\], \[data-adjacency-select\], select\[name="direction"\]/);
    assert.match(view, /finally \{\s*if \(!disposed\) setCourseIntentMutationPending\(false\)/);
});


test("expedition drawers restore opener focus and rerenders provide a deliberate fallback", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    assert.match(view, /const returnFocus = document\.activeElement instanceof HTMLElement/);
    assert.match(view, /openWorkspaceDrawer\(root, title/);
    assert.match(view, /\[data-current-action-button\]/);
    assert.match(view, /restoreFocusAfterRender/);
    assert.match(view, /queueMicrotask\(\(\) => \{/);
});


test("remediation review matrix exercises effects, history, encounter, journey, and progress at narrow widths", () => {
    const capture = fs.readFileSync(
        path.join(sourceDir, "../visual-review/capture-phase15.sh"),
        "utf8");
    const fixture = fs.readFileSync(
        path.join(sourceDir, "../visual-review/phase15.ts"),
        "utf8");

    for (const state of [
        "effects-workspace",
        "history-workspace",
        "encounter-pending",
        "journey-pending",
        "partial-progress"
    ]) {
        assert.match(capture, new RegExp(state));
    }
    assert.match(capture, /500\|844\|390/);
    assert.match(capture, /activeEffectVisible/);
    assert.match(capture, /unifiedHistoryVisible/);
    assert.match(capture, /positionProgressVisible/);
    assert.match(fixture, /pendingEncounter/);
    assert.match(fixture, /effectsFixture/);
});


test("encounter resolution finishes its authoritative mutation before travel can resume", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const start = view.indexOf('const resolve = button("Mark encounter resolved"');
    const end = view.indexOf("row.append(handoff, resolve)", start);
    assert.ok(start >= 0 && end > start);
    const resolutionBlock = view.slice(start, end);
    assert.match(resolutionBlock, /api\.resolveEncounter/);
    assert.match(resolutionBlock, /applyRuntime\(next\)/);
    assert.doesNotMatch(resolutionBlock, /continueTravel\(/);
});


test("readability presentation uses structured procedure groups without hiding exact configuration", () => {
    const authoring = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const reference = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-reference-view.ts"),
        "utf8");

    assert.match(authoring, /procedurePresentationSections/);
    assert.match(authoring, /hc-rule-fact-groups/);
    assert.match(authoring, /hc-rule-map/);
    assert.match(reference, /At the table/);
    assert.match(reference, /Exact parameter detail/);
    assert.match(reference, /procedurePresentationSections/);
});

test("active play exposes authoritative cell and journey progress visually", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /cellProgressIndicator\(runtime\)/);
    assert.match(view, /data\.cellProgress|dataset\.cellProgress/);
    assert.match(view, /Progress through the current cell/);
    assert.match(view, /journeyStageSequence\(activeJourney\)/);
    assert.match(view, /hc-stage-sequence/);
    assert.match(view, /process\.execution\.progressFloor/);
    assert.match(view, /process\.execution\.progressCeiling/);
});

test("movement resolution includes an accessible contributor ledger and authority distinctions", () => {
    const movement = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-movement-composition-view.ts"),
        "utf8");
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(movement, /movementCompositionLedger/);
    assert.match(movement, /Movement composition contributors/);
    assert.match(movement, /Retained, not applied/);
    assert.match(movement, /Reference fallback/);
    assert.match(movement, /Before DM override/);
    assert.match(view, /movementCompositionLedger\(runtime\)/);
    assert.match(view, /Reference fallback; not a fully resolved composition/);
});


test("readability follow-up exposes nonspatial movement and encounter schedule state", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const party = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-party-sheet.ts"),
        "utf8");
    const journeyOrder = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/journey-stage-presentation.ts"),
        "utf8");
    const journey = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/journey-panel.ts"),
        "utf8");

    assert.match(view, /!runtime\.expedition\.isSpatial[\s\S]*movementCompositionLedger\(runtime\)/);
    assert.match(view, /Encounter check schedule/);
    assert.match(view, /encounterScheduleSummary\(runtime\)/);
    assert.match(party, /dataset\.activityRoster/);
    assert.match(party, /Allowance model/);
    assert.match(party, /Active this interval/);
    assert.match(journeyOrder, /stageOrder\.map/);
    assert.match(journey, /Journey event record/);
    assert.match(journey, /Journey history/);
});


test("encounter schedule presentation distinguishes contextual procedure from automatic cadence", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const schedule = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/encounter-schedule-presentation.ts"),
        "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(view, /encounterScheduleAvailable\(runtime\)/);
    assert.match(view, /Contextual schedule/);
    assert.match(view, /Travel checks/);
    assert.match(view, /Camp check/);
    assert.match(view, /Terrain influence/);
    assert.match(view, /does not imply an automatic encounter cadence/);
    assert.match(schedule, /moduleKey === "encounters\.schedule"/);
    assert.match(styles, /overflow-wrap:normal; word-break:normal/);
    assert.match(styles, /\.hc-readable-table thead th[^}]*white-space:nowrap/);
    assert.match(styles, /\.hc-movement-ledger-table thead th[^}]*white-space:nowrap/);
});


test("rendered review records measured viewport separately from 390-pixel container coverage", () => {
    const capture = fs.readFileSync(
        path.join(sourceDir, "..", "visual-review", "capture-phase15.sh"),
        "utf8");

    assert.match(capture, /procedure-compact-container-390/);
    assert.match(capture, /readability-workspace-container-390/);
    assert.doesNotMatch(capture, /native-mobile/);
    assert.match(capture, /requestedWindowWidth/);
    assert.match(capture, /requestedContainerWidth/);
    assert.match(capture, /measuredViewportMatchesRequestedWindow/);
    assert.match(capture, /requested review container width was not established/);
});


test("Phase 15.1 Guided layers beginner help over existing Compact and runtime authority", () => {
    const guidance = fs.readFileSync(path.join(sourceDir, "ui/guidance.ts"), "utf8");
    const home = fs.readFileSync(path.join(sourceDir, "modules/home/tool-home-view.ts"), "utf8");
    const procedure = fs.readFileSync(path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"), "utf8");
    const expedition = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    assert.match(guidance, /hex-crawl\.guided\.enabled/);
    assert.match(guidance, /stored !== "false"/);
    assert.match(guidance, /hc-guidance-off/);
    assert.match(home, /Set up the expedition you want to run\./);
    assert.match(home, /Explore without a map/);
    assert.match(home, /Journey without a grid/);
    assert.match(home, /guidedExperienceEnabled/);
    assert.match(home, /data-setup-step="0"/);
    assert.match(home, /data-setup-step="1"/);
    assert.match(home, /data-setup-step="2"/);
    assert.match(procedure, /ProcedureAuthoringMode = "compact" \| "advanced" \| "json"/);
    assert.match(procedure, /How to use Compact/);
    assert.match(procedure, /guidedDisclosure\(/);
    assert.match(expedition, /Why is this next\?/);
    assert.match(expedition, /guidedActionExplanation/);
    assert.match(expedition, /guidedActionInput/);
    assert.match(expedition, /guidedActionResult/);
    assert.match(expedition, /movementComposition\.missingInputs\.length/);
    assert.match(expedition, /selecting a direction does not move the party/);
    assert.doesNotMatch(guidance, /fetch\(|CampaignProcedure|procedure\.modules|runtime\./);
});

test("Phase 15.1 explains domain-specific numeric grid settings at their point of use", () => {
    const home = fs.readFileSync(path.join(sourceDir, "modules/home/tool-home-view.ts"), "utf8");
    const worlds = fs.readFileSync(path.join(sourceDir, "modules/worlds/world-list-view.ts"), "utf8");
    const editor = fs.readFileSync(path.join(sourceDir, "modules/worlds/world-editor-view.ts"), "utf8");
    const assistant = fs.readFileSync(path.join(sourceDir, "modules/assistants/assistant-entry-view.ts"), "utf8");
    for (const source of [home, worlds, editor, assistant]) {
        assert.match(source, /attachFieldHelp/);
        assert.match(source, /Map scale/);
        assert.doesNotMatch(source, /Hex center distance/);
    }
    assert.match(home, /center of one cell to the center of an adjacent cell/);
    assert.match(worlds, /center of one adjacent cell to the next/);
    assert.match(editor, /They are not travel-distance settings/);
    assert.match(assistant, /q\/r are axial cell coordinates/);
});


test("Phase 15.1 preset discovery explains fit and DM workload from configured generic behavior", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const guidance = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/preset-guidance.ts"),
        "utf8");

    assert.match(workspace, /Choosing a ruleset/);
    assert.match(workspace, /View details/);
    assert.match(workspace, /Use this preset/);
    assert.match(workspace, /presetDecisionFacts/);
    assert.match(workspace, /presetGuidance\(preset\.procedure\)/);
    assert.doesNotMatch(workspace, /Good fit when/);
    assert.doesNotMatch(workspace, /You will manage/);
    assert.match(guidance, /usesNavigationChecks/);
    assert.match(guidance, /cadence/);
    assert.match(guidance, /travel\.enabled/);
    assert.match(guidance, /automationLevel/);
    assert.doesNotMatch(guidance, /presetKey/);
});

test("Phase 15.1 Compact supplies domain explanations for numeric procedure settings", () => {
    const presentation = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-presentation.ts"),
        "utf8");

    for (const key of [
        "startingExitProgressFactor",
        "nearExitProgressFactor",
        "farExitProgressFactor",
        "backExitProgressFactor",
        "directionChangeProgressCostFactor",
        "baseBudget",
        "travelChecksPerInterval",
        "timeCost",
        "normalTravelLimit",
        "progressFloor",
        "progressCeiling"
    ]) {
        assert.match(presentation, new RegExp(key + ":"));
    }
    assert.match(presentation, /numericHelp\[key\] \?\? definition\.description \?\? null/);
    assert.match(presentation, /0\.5 means half the map scale/);
});

test("Phase 15.1 rendered review requires Guided onboarding, preset advice, and next-action explanations", () => {
    const capture = fs.readFileSync(
        path.join(sourceDir, "../visual-review/capture-phase15.sh"),
        "utf8");
    const fixture = fs.readFileSync(
        path.join(sourceDir, "../visual-review/phase15.ts"),
        "utf8");

    assert.match(fixture, /guidedSetupVisible/);
    assert.match(fixture, /presetDecisionFactGroups/);
    assert.match(fixture, /guidedRuleWhyCount/);
    assert.ok((fixture.match(/guidedNextActionWhyVisible/g) ?? []).length >= 2);
    assert.match(fixture, /visualPreset/);
    assert.match(capture, /Guided home onboarding is incomplete/);
    assert.match(capture, /Guided preset discovery is missing/);
    assert.match(capture, /Guided Compact explanations are incomplete/);
    assert.match(capture, /Guided next-action explanation is missing/);
});


test("Phase 15.1 procedure entry surfaces expose the same beginner-help preference control", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");
    const capture = fs.readFileSync(
        path.join(sourceDir, "../visual-review/capture-phase15.sh"),
        "utf8");

    assert.match(workspace, /page\.querySelector<HTMLElement>\("\.hc-page-header"\)\?\.append\(guidancePreferenceButton\(root\)\)/);
    assert.match(workspace, /toolbar\.append\(back, guidancePreferenceButton\(root\)\)/);
    assert.match(capture, /Guided procedure preference control is missing/);
});


test("Phase 15.1 explains pending consequences without inventing their outcome", () => {
    const effects = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/effects-panel.ts"),
        "utf8");
    const survival = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/survival-resources-panel.ts"),
        "utf8");
    const fixture = fs.readFileSync(
        path.join(sourceDir, "../visual-review/phase15.ts"),
        "utf8");
    const capture = fs.readFileSync(
        path.join(sourceDir, "../visual-review/capture-phase15.sh"),
        "utf8");

    assert.match(effects, /Why is this pending\?/);
    assert.match(effects, /pending\.reason/);
    assert.match(effects, /pending\.requiredAction/);
    assert.match(effects, /without inventing an automatic outcome/);
    assert.match(survival, /Why is this pending\?/);
    assert.match(survival, /does not reroll or reinterpret the consequence/);
    assert.match(fixture, /guided-pending-consequence/);
    assert.match(fixture, /guidedConsequenceHelpCount/);
    assert.match(capture, /Guided pending-consequence explanation is missing/);
});
