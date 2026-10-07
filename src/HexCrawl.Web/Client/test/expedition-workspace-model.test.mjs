import assert from "node:assert/strict";
import test from "node:test";
import { expeditionWorkspaceAction, spatialPositionPresentation } from "../.test-dist/modules/expeditions/expedition-workspace-model.js";

function runtime() {
    return {
        pauseReason: null,
        procedure: { runtime: null },
        expedition: {
            isSpatial: false,
            activeWatchNumber: null,
            completedWatches: 0
        }
    };
}

function process(overrides = {}) {
    return {
        id: "journey-1",
        status: "Active",
        isTerminal: false,
        definition: {
            displayName: "Crossing the Shattered Marches",
            stages: [{ stageKey: "fen", displayName: "Traverse the Fen" }]
        },
        currentStageKey: "fen",
        pendingActions: [],
        ...overrides
    };
}

function journey(overrides = {}) {
    return {
        activeProcesses: [],
        eventOccurrences: [],
        processPolicy: { support: "Supported" },
        ...overrides
    };
}

test("unresolved journey event is the actual current action", () => {
    const action = expeditionWorkspaceAction(runtime(), journey({
        activeProcesses: [process()],
        eventOccurrences: [{
            status: "ResolutionRequired",
            eventType: "Mishap",
            eventKey: null
        }]
    }), null);

    assert.equal(action.kind, "journey");
    assert.equal(action.label, "Resolve journey event");
    assert.equal(action.urgent, true);
    assert.match(action.detail, /Mishap/);
});

test("pending journey stage transition is surfaced specifically", () => {
    const action = expeditionWorkspaceAction(runtime(), journey({
        activeProcesses: [process({
            status: "ResolutionRequired",
            pendingActions: [{
                kind: "StageTransition",
                detail: "Choose the next route stage."
            }]
        })]
    }), null);

    assert.equal(action.kind, "journey");
    assert.equal(action.label, "Advance journey stage");
    assert.equal(action.urgent, true);
    assert.match(action.detail, /Choose the next route stage/);
});

test("active journey stays primary instead of falling through to generic interval work", () => {
    const action = expeditionWorkspaceAction(runtime(), journey({
        activeProcesses: [process()]
    }), null);

    assert.equal(action.kind, "journey");
    assert.equal(action.label, "Continue journey");
    assert.equal(action.urgent, false);
    assert.match(action.detail, /Crossing the Shattered Marches/);
});


test("abstract spatial position exposes authoritative cell and partial progress with units", () => {
    const state = {
        context: { kind: "AbstractHex" },
        procedure: { runtime: { tracksIntraHexProgress: true } },
        expedition: {
            isSpatial: true,
            currentHex: { q: 0, r: 0 },
            hexProgress: { value: 1.5, unit: { symbol: "mi" } },
            exitRequirement: { value: 6, unit: { symbol: "mi" } }
        }
    };

    assert.deepEqual(spatialPositionPresentation(state), {
        value: "Cell 0, 0",
        detail: "1.5 / 6 mi · 25% through current cell"
    });
});

test("spatial procedures without partial progress do not fabricate a progress value", () => {
    const state = {
        context: { kind: "WorldBound" },
        procedure: { runtime: { tracksIntraHexProgress: false } },
        expedition: {
            isSpatial: true,
            currentHex: { q: 4, r: -2 },
            hexProgress: { value: 0, unit: { symbol: "km" } },
            exitRequirement: { value: 8, unit: { symbol: "km" } }
        }
    };

    assert.deepEqual(spatialPositionPresentation(state), {
        value: "Cell 4, -2",
        detail: "Mapped spatial position"
    });
});
