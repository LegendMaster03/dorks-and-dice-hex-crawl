import assert from "node:assert/strict";
import { readFileSync, existsSync } from "node:fs";
import { registerHooks, stripTypeScriptTypes } from "node:module";
import test from "node:test";

// Exercise the real panel methods without constructing their browser DOM.
// Node's transform mode supports the constructor parameter properties.
registerHooks({
    resolve(specifier, context, nextResolve) {
        if (specifier.startsWith(".") && context.parentURL) {
            const candidate = new URL(specifier.endsWith(".js")
                ? specifier.slice(0, -3) + ".ts" : specifier + ".ts", context.parentURL);
            if (existsSync(candidate)) return { url: candidate.href, shortCircuit: true };
        }
        return nextResolve(specifier, context);
    },
    load(url, context, nextLoad) {
        if (url.endsWith(".ts")) return {
            format: "module", shortCircuit: true,
            source: stripTypeScriptTypes(readFileSync(new URL(url), "utf8"), { mode: "transform" })
        };
        return nextLoad(url, context);
    }
});

const { ExpeditionJourneyPanel } = await import("../src/modules/expeditions/journey-panel.ts");
const { ExpeditionSurvivalResourcesPanel } = await import("../src/modules/expeditions/survival-resources-panel.ts");
const { ExpeditionEffectsPanel } = await import("../src/modules/expeditions/effects-panel.ts");

function deferred() {
    let resolve, reject;
    const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
    return { promise, resolve, reject };
}

function fixture(Class) {
    const requests = [];
    const renders = [];
    const panel = Object.create(Class.prototype);
    Object.assign(panel, {
        expeditionId: "isolated-panel-test", state: null,
        disposed: false, syncGeneration: 0,
        api: { get: () => {
            const request = deferred();
            requests.push(request);
            return request.promise;
        } },
        render: () => renders.push(panel.state),
        panel: { remove() {} }
    });
    return { panel, requests, renders };
}

for (const Class of [ExpeditionJourneyPanel, ExpeditionSurvivalResourcesPanel, ExpeditionEffectsPanel]) {
    test(`${Class.name}: an older response does not overwrite a newer editor`, async () => {
        const { panel, requests, renders } = fixture(Class);
        const older = panel.sync(), newer = panel.sync();
        const state = { expeditionVersion: 2 };
        requests[1].resolve(state);
        await newer;
        requests[0].resolve({ expeditionVersion: 1 });
        await older;
        assert.equal(panel.state, state);
        assert.deepEqual(renders, [state]);
    });

    test(`${Class.name}: a superseded response stays rejected after the latest fetch fails`, async () => {
        const { panel, requests, renders } = fixture(Class);
        const older = panel.sync(), newer = panel.sync();
        const failed = assert.rejects(newer, /Unavailable/);
        requests[1].reject(new Error("Unavailable"));
        await failed;
        requests[0].resolve({ expeditionVersion: 1 });
        await older;
        assert.equal(panel.state, null);
        assert.deepEqual(renders, []);
    });

    test(`${Class.name}: disposal prevents late response application`, async () => {
        const { panel, requests, renders } = fixture(Class);
        const pending = panel.sync();
        panel.dispose();
        requests[0].resolve({ expeditionVersion: 1 });
        await pending;
        assert.equal(panel.state, null);
        assert.deepEqual(renders, []);
    });
}

for (const Class of [ExpeditionJourneyPanel, ExpeditionSurvivalResourcesPanel]) {
    test(`${Class.name}: a saved mutation supersedes an outstanding read`, async () => {
        const { panel, requests, renders } = fixture(Class);
        const pending = panel.sync();
        const saved = { expeditionVersion: 2 };
        panel.apply(saved);
        requests[0].resolve({ expeditionVersion: 1 });
        await pending;
        assert.equal(panel.state, saved);
        assert.deepEqual(renders, [saved]);
    });
}
