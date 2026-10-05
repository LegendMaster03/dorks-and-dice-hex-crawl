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
    assert.match(api, /composer\/canonical\/draft/);
    assert.match(api, /composer\/canonical\/validate/);
    assert.match(api, /canonical\/revisions/);
});

test("Phase 15 expedition workspace derives the next action from authoritative runtime and journey state", () => {
    const workspace = fs.readFileSync(
        path.join(sourceDir, "modules/expeditions/phase15-expedition-workspace.ts"),
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
    assert.match(workspace, /installRuntimeHooks/);
    assert.doesNotMatch(workspace, /MutationObserver/);
});

test("Phase 15 shared workspace primitives retain accessible drawer behavior", () => {
    const workspace = fs.readFileSync(path.join(sourceDir, "ui/workspace.ts"), "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "phase15-styles.ts"), "utf8");

    assert.match(workspace, /role", "dialog"/);
    assert.match(workspace, /aria-modal/);
    assert.match(workspace, /Escape/);
    assert.match(workspace, /trigger\.focus/);
    assert.match(styles, /hc-workspace-drawer/);
    assert.match(styles, /@media \(max-width: 760px\)/);
}
);
