import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../src/", import.meta.url);

async function source(path) {
    return await readFile(new URL(path, root), "utf8");
}

test("client modules have stable, complete route ownership", async () => {
    const [catalog, home, worlds, expeditions, assistants] = await Promise.all([
        source("client-module-catalog.ts"),
        source("modules/home-module.ts"),
        source("modules/worlds-module.ts"),
        source("modules/expeditions-module.ts"),
        source("modules/assistants-module.ts")
    ]);

    assert.match(catalog, /homeModule[\s\S]*worldsModule[\s\S]*expeditionsModule[\s\S]*assistantsModule/);
    assert.match(home, /id:\s*"home"[\s\S]*routeKinds:\s*\["home"\]/);
    assert.match(worlds, /id:\s*"worlds"[\s\S]*routeKinds:\s*\["worlds",\s*"world",\s*"edit"\]/);
    assert.match(expeditions, /id:\s*"expeditions"[\s\S]*routeKinds:\s*\["expedition",\s*"tracker"\]/);
    assert.match(assistants, /id:\s*"assistants"[\s\S]*routeKinds:\s*\["assistant",\s*"assistant-entry"\]/);

    for (const kind of [
        "home",
        "worlds",
        "world",
        "edit",
        "expedition",
        "tracker",
        "assistant",
        "assistant-entry"
    ]) {
        assert.match(catalog, new RegExp(`"${kind}"`));
    }
});
