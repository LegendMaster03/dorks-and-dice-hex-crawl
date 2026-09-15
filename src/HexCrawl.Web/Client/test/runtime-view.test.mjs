import test from "node:test";
import assert from "node:assert/strict";
import {
    alexandrianActualDistance,
    discoveredSubjectIds,
    directionLabel,
    formatDistance,
    formatHours
} from "../.test-dist/runtime-view.js";

test("Alexandrian distance helper is deterministic from resolved dice", () => {
    assert.equal(alexandrianActualDistance(12, 2, 5), 12);
    assert.equal(alexandrianActualDistance(12, 1, 1), 6);
    assert.equal(alexandrianActualDistance(12, 6, 6), 18);
});

test("discovery projection remains subject-specific", () => {
    const runtime = {
        knowledge: [
            { subjectId: "location-a", state: "Discovered" },
            { subjectId: "feature-b", state: "Observed" },
            { subjectId: "location-c", state: "Revealed" }
        ]
    };
    const discovered = discoveredSubjectIds(runtime);
    assert.deepEqual([...discovered], ["location-a", "location-c"]);
    assert.equal(discovered.has("feature-b"), false);
});

test("runtime presentation formatters expose resolved state compactly", () => {
    assert.equal(directionLabel(3), "Direction 3");
    assert.equal(directionLabel(null), "—");
    assert.equal(formatHours(1.5), "1.5 h");
    assert.equal(formatDistance({ value: 6, unit: { symbol: "mi" } }), "6 mi");
});
