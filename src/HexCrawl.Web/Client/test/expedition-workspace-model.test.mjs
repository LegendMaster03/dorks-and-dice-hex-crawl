import assert from "node:assert/strict";
import test from "node:test";

import { expeditionWorkspaceAction } from "../.test-dist/modules/expeditions/expedition-workspace-model.js";

function runtime(overrides = {}) {
    const base = {
        pauseReason: null,
        procedure: {
            runtime: { usesNavigationChecks: false, encounterCadence: "None" }
        },
        expedition: {
            isSpatial: true,
            activeWatchNumber: null,
            completedWatches: 0,
            currentDay: 1
        },
        history: []
    };
    return {
        ...base,
        ...overrides,
        procedure: overrides.procedure ?? base.procedure,
        expedition: overrides.expedition ?? base.expedition,
        history: overrides.history ?? base.history
    };
}

function survival({ forced = false, consequences = 0 } = {}) {
    return {
        forcedTravel: { checkDue: forced },
        pendingResourceConsequences: Array.from({ length: consequences }, (_, index) => ({ id: String(index) }))
    };
}

test("forced travel and persistent consequences block routine travel as focused survival work", () => {
    const forced = expeditionWorkspaceAction(runtime(), null, survival({ forced: true }));
    assert.equal(forced.kind, "survival");
    assert.equal(forced.label, "Resolve forced travel");
    assert.equal(forced.urgent, true);

    const consequence = expeditionWorkspaceAction(runtime(), null, survival({ consequences: 1 }));
    assert.equal(consequence.kind, "survival");
    assert.equal(consequence.label, "Resolve travel consequence");
    assert.equal(consequence.urgent, true);
});

test("encounter interruption outranks forced travel and routine continuation", () => {
    const action = expeditionWorkspaceAction(
        runtime({ pauseReason: "EncounterTriggered" }),
        null,
        survival({ forced: true }));
    assert.equal(action.kind, "encounter");
    assert.equal(action.label, "Resolve encounter");
});

test("journey resolution blocks routine travel when no higher-priority survival work is pending", () => {
    const journey = {
        activeProcesses: [{
            status: "ResolutionRequired",
            pendingActions: [],
            currentStageKey: "crossing",
            definition: {
                displayName: "River crossing",
                stages: [{ stageKey: "crossing", displayName: "Crossing" }]
            }
        }]
    };
    const action = expeditionWorkspaceAction(runtime(), journey, survival());
    assert.equal(action.kind, "journey");
    assert.equal(action.label, "Resolve journey stage");
});

test("spatial routine action is tabletop-facing Continue travel rather than Run watch", () => {
    const ready = expeditionWorkspaceAction(runtime(), null, survival());
    assert.equal(ready.kind, "travel");
    assert.equal(ready.label, "Continue travel");

    const active = expeditionWorkspaceAction(runtime({
        expedition: {
            isSpatial: true,
            activeWatchNumber: 2,
            completedWatches: 1,
            currentDay: 1
        }
    }), null, survival());
    assert.equal(active.kind, "travel");
    assert.equal(active.label, "Continue travel");
});
