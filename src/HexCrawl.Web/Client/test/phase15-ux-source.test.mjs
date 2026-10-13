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
    const sharedAction = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/phase15-next-action.ts"), "utf8");
    assert.match(view, /renderPhase15NextAction/);
    assert.match(sharedAction, /hc-current-action/);
    assert.match(view, /runtime\.expedition\.isSpatial/);
    assert.match(view, /Journey \/ challenge/);
    assert.match(view, /GM Tools/);
    assert.match(view, /return renderPeriodicExpeditionWorkbench/);
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

test("focused expedition mutations share the visible workspace error boundary and authoritative runtime application", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /const runUiMutation = async \(action: \(\) => Promise<void>\): Promise<void> =>/);
    assert.match(view, /await mutate\(action\);\s*\} catch \{\s*\/\/ mutate already surfaced the failure in the workspace error region\./);
    assert.match(view, /new ExpeditionPartySheetController\([\s\S]*\(\) => runtime,\s*applyRuntime,/);
    assert.match(view, /new ExpeditionEnvironmentPanel\([\s\S]*\(\) => runtime,\s*applyRuntime,/);
    assert.ok((view.match(/await runUiMutation\(action\)/g) ?? []).length >= 4);
    assert.match(view, /void runUiMutation\(async \(\) => \{/);
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
    assert.match(view, /renderPeriodicExpeditionWorkbench/);
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
test("periodic spatial travel uses topology-derived adjacency and angular navigation", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const periodic = fs.readFileSync(path.join(sourceDir, "modules/expeditions/periodic-expedition-workbench.ts"), "utf8");
    const adjacency = fs.readFileSync(path.join(sourceDir, "modules/expeditions/spatial-adjacency.ts"), "utf8");
    assert.match(view, /return renderPeriodicExpeditionWorkbench/);
    assert.match(periodic, /renderCurrentCellNavigator/);
    assert.match(periodic, /veerDegreesOnFailure/);
    assert.match(periodic, /activeActivityAssignments/);
    assert.match(adjacency, /createCurrentCellAdjacency/);
    assert.doesNotMatch(adjacency, /adjacencies\.length === 6/);
    assert.doesNotMatch(periodic, /new HexDirection/);
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
    assert.match(topology, /currentPeriodicCellAdjacency/);
    assert.match(topology, /context\.interfaces\.map\(edge =>/);
    assert.doesNotMatch(topology, /axialSteps|hexCorners|currentRuntimeCellAdjacency/);
    assert.doesNotMatch(adjacency + topology + navigator, /Northeast|Northwest|Southeast|Southwest/);
    assert.match(navigator, /Travel through \$\{candidate\.label\}/);
    assert.match(view, /return renderPeriodicExpeditionWorkbench/);
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

test("encounter resume requires an explicit resolved-at-table acknowledgement", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /api\.resolveEncounter/);
    assert.match(view, /pending\.triggerSequence/);
    assert.doesNotMatch(view, /Encounter resolved — continue travel/);
});

test("periodic spatial travel uses one native cell-watch action", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const periodic = fs.readFileSync(path.join(sourceDir, "modules/expeditions/periodic-expedition-workbench.ts"), "utf8");
    assert.match(view, /return renderPeriodicExpeditionWorkbench/);
    assert.match(periodic, /api\.advanceCellWatch/);
    assert.match(periodic, /data-gm-travel/);
    assert.doesNotMatch(periodic, /ExpeditionWatchController|api\.advanceExpedition/);
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

test("rendered review retains forced travel in the shared gameplay panel and navigator focus", () => {
    const capture = fs.readFileSync(
        path.join(sourceDir, "..", "visual-review", "capture-phase15.sh"),
        "utf8");
    const fixture = fs.readFileSync(
        path.join(sourceDir, "..", "visual-review", "phase15.ts"),
        "utf8");

    assert.match(capture, /forcedTravelPrimaryDomainFacing/);
    assert.match(capture, /forcedTravelNamedTargetVisible/);
    assert.match(fixture, /root\.querySelector\("\[data-gameplay-disclosure\]"\)/);
    assert.match(fixture, /focusedEdge:/);
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

    assert.match(view, /button\("Party & roles", openPartyWorkspace\)/);
    assert.doesNotMatch(view, /Teleport party|openRepositionWorkspace/);
    assert.match(view, /if \(presentation\.capabilities\.journey\)\s*row\.append\(button\("Journey"/);
});

test("nonspatial summary only renders movement when the materialized procedure exposes travel capability", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");

    assert.match(view, /if \(presentation\.capabilities\.travel\)\s*stats\.append\(statAction\("Movement"/);
    assert.match(view, /state\.eventOccurrences\.some\(event => event\.status === "ResolutionRequired"\)/);
});

test("Compact procedure reference does not claim display order is authoritative", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-authoring-view.ts"),
        "utf8");

    assert.match(workspace, /grouped for quick reference/);
    assert.match(workspace, /configured trigger or dependency/);
    assert.doesNotMatch(workspace, /Run these rules in watch order/);
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
    assert.match(view, /return renderPeriodicExpeditionWorkbench/);
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
    assert.match(capture, /partialWatchPositionVisible/);
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

test("active play exposes generalized cell navigation and journey progress visually", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const cells = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/periodic-expedition-workbench.ts"),
        "utf8");

    assert.match(cells, /topology\.currentCell\.address/);
    assert.match(cells, /data-cell-navigator/);
    assert.doesNotMatch(view, /cellProgressIndicator/);
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

    assert.match(view, /movementCompositionLedger\(runtime\)/);
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
    const sharedAction = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/phase15-next-action.ts"), "utf8");
    assert.match(sharedAction, /Why is this next\?/);
    assert.match(expedition, /guidedActionExplanation/);
    assert.match(expedition, /renderGuidedActionGuide/);
    assert.match(expedition, /guidedActionExplanation/);
    assert.match(expedition, /movementComposition\.missingInputs\.length/);
    assert.doesNotMatch(expedition, /selectedHex|ExpeditionWatchController/);
    assert.doesNotMatch(guidance, /fetch\(|CampaignProcedure|procedure\.modules|runtime\./);
});

test("Phase 15.1 explains domain-specific numeric grid settings at their point of use", () => {
    const home = fs.readFileSync(path.join(sourceDir, "modules/home/tool-home-view.ts"), "utf8");
    const worlds = fs.readFileSync(path.join(sourceDir, "modules/worlds/world-list-view.ts"), "utf8");
    const editor = fs.readFileSync(path.join(sourceDir, "modules/worlds/world-editor-view.ts"), "utf8");
    const assistant = fs.readFileSync(path.join(sourceDir, "modules/assistants/assistant-entry-view.ts"), "utf8");
    for (const source of [home, assistant]) {
        assert.match(source, /attachFieldHelp/);
        assert.doesNotMatch(source, /Hex center distance/);
    }
    assert.match(home, /physical distance represented by one geometry unit/i);
    assert.match(worlds, /Physical map scale/);
    assert.match(worlds, /name="physicalScale"[^>]*required/);
    assert.doesNotMatch(worlds, /Grid orientation|Hex radius|<option value="square"|<option value="triangle"/);
    assert.match(editor, /data-world-form/);
    assert.doesNotMatch(editor, /data-grid-form|Save world and grid|Hex radius/);
    assert.match(assistant, /physical distance represented by one (realized )?geometry unit/i);
    assert.doesNotMatch(assistant, /Start q|Start r|name="orientation"/);
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
        "baseBudget",
        "travelChecksPerInterval",
        "timeCost",
        "normalTravelLimit",
        "progressFloor",
        "progressCeiling"
    ]) {
        assert.match(presentation, new RegExp(key + ":"));
    }
    assert.match(presentation, /neighborCenterDistanceFactor:/);
    assert.match(presentation, /"movement.turn-cost": \[/);
    assert.match(presentation, /numericHelp\[key\] \?\? definition\.description \?\? null/);
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

test("the Phase 15 spatial entry delegates all movement to the native polygonal workbench", () => {
    const entry = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    const spatial = fs.readFileSync(path.join(sourceDir, "modules/expeditions/periodic-expedition-workbench.ts"), "utf8");
    assert.match(entry, /return renderPeriodicExpeditionWorkbench/);
    assert.match(spatial, /api\.advanceCellWatch/);
    assert.match(spatial, /api\.repositionCellExpedition/);
    assert.match(spatial, /map\.setCellSelectionHandler/);
    assert.doesNotMatch(spatial, /map\.setClickInterceptor/);
    assert.doesNotMatch(entry, /ExpeditionWatchController|setHexSelectionHandler|HexCoordinate|selectedHex/);
    assert.doesNotMatch(spatial, /api\.advanceExpedition|name="hexSteps"/);
});

test("native polygonal watch maintains generated inputs and concurrent watch state", () => {
    const spatial = fs.readFileSync(path.join(sourceDir, "modules/expeditions/periodic-expedition-workbench.ts"), "utf8");
    assert.match(spatial, /resolveCellProcedureInputs/);
    assert.match(spatial, /generatedProcedureResolutionId: generated\?\.generatedResolutionId/);
    assert.match(spatial, /next\.version !== result\.expeditionVersion/);
    assert.match(spatial, /displayedWatchNumber !== state\.activeWatchNumber/);
    assert.match(spatial, /state\.activeDeliberateDoubleBack/);
    assert.match(spatial, /state\.activeContinueAcrossBoundaries/);
});

test("nonspatial Phase 15 retains guided actions, editable supporting tools and history", () => {
    const view = fs.readFileSync(path.join(sourceDir, "modules/expeditions/expedition-view.ts"), "utf8");
    assert.match(view, /renderNonSpatialWorkspace/);
    assert.match(view, /renderPhase15NextAction/);
    assert.match(view, /renderGuidedActionGuide/);
    assert.match(view, /ExpeditionPartySheetController/);
    assert.match(view, /ExpeditionEnvironmentPanel/);
    assert.match(view, /ExpeditionSurvivalResourcesPanel/);
    assert.match(view, /ExpeditionJourneyPanel/);
    assert.match(view, /recordWatchAssistant/);
    assert.match(view, /createEncounterHandoff/);
    assert.doesNotMatch(view, /ExpeditionWatchController|openTravelWorkspace|currentHex/);
});

test("the active polygonal workbench resolves pace without browser-stored axial direction", () => {
    const periodic = fs.readFileSync(path.join(sourceDir, "modules/expeditions/periodic-expedition-workbench.ts"), "utf8");
    const pace = fs.readFileSync(path.join(sourceDir, "modules/expeditions/periodic-travel-pace.ts"), "utf8");
    assert.match(periodic, /resolvePeriodicPace/);
    assert.match(pace, /activePace\?\.trim\(\) \|\| "normal"/);
    assert.match(pace, /keys\.includes\(selected\)/);
    assert.doesNotMatch(periodic, /loadTravelPreferences|mergeRuntimeTravelPreferences|normalizeTravelDirectionPreference/);
});
