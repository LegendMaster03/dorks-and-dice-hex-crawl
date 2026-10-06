import test from "node:test";
import assert from "node:assert/strict";
import {
    discoveredSubjectIds,
    directionLabel,
    formatDistance,
    formatHours
} from "../.test-dist/runtime-view.js";

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
    assert.equal(directionLabel(0), "Edge 1");
    assert.equal(directionLabel(1), "Edge 2");
    assert.equal(directionLabel(3), "Edge 4");
    assert.equal(directionLabel(2, "FlatTop"), "Edge 3");
    assert.equal(directionLabel(5, "FlatTop"), "Edge 6");
    assert.equal(directionLabel(null), "—");
    assert.equal(formatHours(1.5), "1.5 h");
    assert.equal(formatDistance({ value: 6, unit: { symbol: "mi" } }), "6 mi");
});
