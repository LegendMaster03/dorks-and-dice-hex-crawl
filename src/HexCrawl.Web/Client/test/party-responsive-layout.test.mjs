import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const sourceDir = path.resolve("src");

test("party layout responds to its rendered container width", () => {
    const app = fs.readFileSync(path.join(sourceDir, "app.ts"), "utf8");
    const styles = fs.readFileSync(path.join(sourceDir, "party-responsive-styles.ts"), "utf8");

    assert.match(app, /ensurePartyResponsiveStyles\(\);/);
    assert.match(styles, /\.hc-party-editor-panel,[\s\S]*\.hc-party-register[\s\S]*container-type: inline-size;/);
    assert.match(styles, /@container \(max-width: 720px\)/);
    assert.match(styles, /\.hc-party-editor-panel \.hc-party-editor-row,[\s\S]*\.hc-party-editor-panel \.hc-party-movement-grid[\s\S]*grid-template-columns: minmax\(0, 1fr\);/);
    assert.match(styles, /\.hc-party-editor-panel \.hc-party-editor-row > \.hc-danger-action[\s\S]*justify-self: start;/);
    assert.match(styles, /\.hc-party-editor-panel \.hc-party-save-row[\s\S]*flex-direction: column;/);
    assert.match(styles, /\.hc-party-register \.hc-party-register-grid[\s\S]*grid-template-columns: minmax\(0, 1fr\);/);
});
