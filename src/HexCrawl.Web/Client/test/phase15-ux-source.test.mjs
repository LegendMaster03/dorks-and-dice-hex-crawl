import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceDir = path.resolve("src");

test("Phase 15 procedure workspace keeps Compact Advanced and JSON on one CampaignProcedure authority", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/procedures/procedure-workspace-view.ts"),
        "utf8");
    const api = fs.readFileSync(path.join(sourceDir, "procedure-composer-api.ts"), "utf8");

    assert.match(workspace, /ProcedureWorkspaceMode = "compact" \| "advanced" \| "json"/);
    assert.match(workspace, /Presets are copied once; the saved procedure does not depend on the catalog afterward/);
    assert.match(workspace, /Normal Compact editing uses tabletop concepts rather than mechanic IDs or dependency keys/);
    assert.match(workspace, /Advanced exposes exact generic mechanics and contracts/);
    assert.match(workspace, /Canonical procedure JSON/);
    assert.match(workspace, /same CampaignProcedure used by Compact and Advanced/);
    assert.match(workspace, /server parsing, domain validation, and optimistic concurrency/);
    assert.match(workspace, /createCanonicalRevision/);
    assert.match(workspace, /mode !== "json" \|\| jsonBusy/);
    assert.match(api, /composer\/canonical\/draft/);
    assert.match(api, /composer\/canonical\/validate/);
    assert.match(api, /canonical\/revisions/);
});

test("Phase 15 expedition workspace derives the next action from authoritative runtime and journey state", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/phase15-expedition-workspace.ts"),
        "utf8");
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const signal = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-runtime-events.ts"),
        "utf8");

    assert.match(workspace, /Current action/);
    assert.match(workspace, /runtime\.pauseReason === "EncounterTriggered"/);
    assert.match(workspace, /runtime\.pauseReason === "LostRecognitionRequired"/);
    assert.match(workspace, /eventOccurrences\.find\(event => event\.status === "ResolutionRequired"\)/);
    assert.match(workspace, /activeProcesses\[0\]/);
    assert.match(workspace, /No repeating watch is required by this procedure/);
    assert.match(workspace, /no map or interval bookkeeping is fabricated/);
    assert.match(workspace, /focusedIntervalPolicy\.support === "Supported"/);
    assert.match(workspace, /Resources & effects/);
    assert.match(workspace, /subscribeExpeditionRuntimeChanged/);
    assert.match(view, /publishExpeditionRuntimeChanged\(root, next\)/);
    assert.match(signal, /root\.dispatchEvent\(new CustomEvent/);
    assert.match(signal, /root\.addEventListener/);
    assert.match(signal, /root\.removeEventListener/);
    assert.doesNotMatch(workspace, /installRuntimeHooks/);
    assert.doesNotMatch(workspace, /api\.getExpedition\s*=/);
    assert.doesNotMatch(workspace, /api\.advanceExpedition\s*=/);
    assert.doesNotMatch(workspace, /MutationObserver/);
});

test("Phase 15 secondary panels stay collapsed until requested", () => {
    const view = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-view.ts"),
        "utf8");
    const environment = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/environment-panel.ts"),
        "utf8");
    const presentation = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/expedition-presentation.ts"),
        "utf8");

    assert.doesNotMatch(view, /<details open class="hc-sheet-controls"/);
    assert.doesNotMatch(environment, /this\.panel\.open = true/);
    assert.doesNotMatch(presentation, /<details open class="hc-party-editor-panel"><summary>Party & participant assignments/);
});

test("Phase 15 map selection stays contextual instead of becoming an implicit mutation", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/phase15-expedition-workspace.ts"),
        "utf8");

    assert.match(workspace, /Selecting map context does not mutate expedition state/);
    assert.match(workspace, /Selected hex q/);
    assert.match(workspace, /mapCanvas\?\.addEventListener\("click"/);
    assert.match(workspace, /mapCanvas\?\.addEventListener\("keydown"/);
    assert.doesNotMatch(workspace, /readMapSelection[\s\S]*advanceExpedition/);
});

test("Phase 15 shared workspace primitives retain accessible drawer behavior", () => {
    const workspace = fs.readFileSync(path.join(sourceDir, "ui/workspace.ts"), "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(workspace, /role", "dialog"/);
    assert.match(workspace, /aria-modal/);
    assert.match(workspace, /Escape/);
    assert.match(workspace, /returnFocus\?\.isConnected/);
    assert.match(workspace, /returnFocus\.focus\(\)/);
    assert.match(styles, /hc-focus-workspace/);
    assert.match(styles, /@media \(max-width: 760px\)/);
});
