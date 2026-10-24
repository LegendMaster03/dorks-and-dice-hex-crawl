import test from "node:test";
import assert from "node:assert/strict";
import {
    discoveredSubjectIds,
    directionLabel,
    formatDistance,
    formatHours,
    formatRuntimeEventMessage
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

test("saved record-shaped cell references become compact motif/lattice addresses", () => {
    const id = "0da1c371-d22f-4b5a-949b-697f412e0552";
    const cell = address => `WorldCellId { TilingId = ${id}, Address = ${address} }`;
    assert.equal(formatRuntimeEventMessage(`DM repositioned from ${cell("face-0@21,-3")} to ${cell("triangle-1@21,-4")}.`),
        "DM repositioned from face-0@21,-3 to triangle-1@21,-4.");
    assert.equal(formatRuntimeEventMessage("Cell triangle-1@-2,3 boundary crossed."),
        "Cell triangle-1@-2,3 boundary crossed.");
    assert.equal(formatRuntimeEventMessage("A note with {braces} and an unrelated ID."),
        "A note with {braces} and an unrelated ID.");
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
