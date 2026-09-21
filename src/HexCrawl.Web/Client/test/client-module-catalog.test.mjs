import assert from "node:assert/strict";
import test from "node:test";

import {
    clientModules,
    findClientModule
} from "../.test-dist/client-module-catalog.js";
import { parseToolRoute } from "../.test-dist/tool-route.js";

test("client modules have stable feature ownership", () => {
    assert.deepEqual(
        clientModules.map(module => module.id),
        ["home", "worlds", "expeditions", "assistants"]);

    const cases = [
        ["/", "home"],
        ["/worlds", "worlds"],
        ["/worlds/world-1", "worlds"],
        ["/worlds/world-1/edit", "worlds"],
        ["/worlds/world-1/expeditions/exp-1", "expeditions"],
        ["/expeditions/exp-1", "expeditions"],
        ["/expeditions/exp-1/travel", "assistants"],
        ["/assistants/navigation", "assistants"]
    ];

    for (const [path, expectedModule] of cases) {
        const route = parseToolRoute(path);
        assert.notEqual(route.kind, "unknown");
        assert.equal(findClientModule(route).id, expectedModule);
    }
});
