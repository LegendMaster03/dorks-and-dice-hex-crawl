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
    assert.match(workspace, /Only the rules shown here are part of this procedure/);
    assert.match(workspace, /Advanced exposes exact generic module keys, mechanics, versions, parameters, and contracts/);
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
        assert.match(labelBlock, new RegExp("\\b" + key + ":\\s*\""), key);
    }
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
    assert.match(workspace, /save\.disabled = savePending \|\| historical\(\) \|\| !hasStructuredChanges\(\)/);
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

    assert.match(view, /Adjacent hex travel direction/);
    assert.match(view, /adjacentDirection/);
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

    assert.match(view, /Selecting a hex does not mutate expedition state/);
    assert.match(view, /Travel \$\{directionLabel\(direction\)\}/);
    assert.match(view, /map\.setHexSelectionHandler/);
    assert.match(map, /setHexSelectionHandler/);
    assert.match(map, /this\.hexSelectionHandler\?\.\(selected\)/);
    assert.doesNotMatch(view, /setHexSelectionHandler[\s\S]{0,800}advanceExpedition/);
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