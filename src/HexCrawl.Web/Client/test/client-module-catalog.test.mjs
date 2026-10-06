import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/", import.meta.url);

async function source(path) {
    return await readFile(new URL(path, root), "utf8");
}

test("client modules have stable, complete route ownership", async () => {
    const [catalog, home, procedures, worlds, expeditions, assistants] = await Promise.all([
        source("client-module-catalog.ts"),
        source("modules/home/module.ts"),
        source("modules/procedures/module.ts"),
        source("modules/worlds/module.ts"),
        source("modules/expeditions/module.ts"),
        source("modules/assistants/module.ts")
    ]);

    assert.match(catalog, /homeModule[\s\S]*proceduresModule[\s\S]*worldsModule[\s\S]*expeditionsModule[\s\S]*assistantsModule/);
    assert.match(home, /id:\s*"home"[\s\S]*routeKinds:\s*\["home"\]/);
    assert.match(procedures, /id:\s*"procedures"[\s\S]*routeKinds:\s*\["procedures",\s*"procedure",\s*"procedure-revision",\s*"procedure-reference"\]/);
    assert.match(worlds, /id:\s*"worlds"[\s\S]*routeKinds:\s*\["worlds",\s*"world",\s*"edit"\]/);
    assert.match(expeditions, /id:\s*"expeditions"[\s\S]*routeKinds:\s*\["expedition"\]/);
    assert.match(assistants, /id:\s*"assistants"[\s\S]*routeKinds:\s*\["assistant",\s*"assistant-entry"\]/);

    for (const kind of [
        "home",
        "procedures",
        "procedure",
        "procedure-revision",
        "procedure-reference",
        "worlds",
        "world",
        "edit",
        "expedition",
        "assistant",
        "assistant-entry"
    ]) {
        assert.match(catalog, new RegExp(`"${kind}"`));
    }
});

test("procedure module exposes read-only reference navigation for the explicit selected revision", async () => {
    const procedures = await source("modules/procedures/module.ts");
    const reference = await source("modules/procedures/procedure-reference-view.ts");

    assert.match(procedures, /View procedure reference/);
    assert.match(procedures, /route\.kind === "procedure-revision" \? route\.revision : null/);
    assert.match(procedures, /revision === null[\s\S]*\$\{base\}\/reference[\s\S]*revisions\/\$\{encodeURIComponent\(String\(revision\)\)\}\/reference/);
    assert.doesNotMatch(procedures, /button\[data-revision\]:disabled/);
    assert.match(reference, /Print reference/);
    assert.match(reference, /@media print/);
    assert.match(reference, /Back to procedure/);
    assert.match(reference, /Stored unknown parameter/);
    assert.doesNotMatch(reference, /createElement\("input"\)|<input/);
});
