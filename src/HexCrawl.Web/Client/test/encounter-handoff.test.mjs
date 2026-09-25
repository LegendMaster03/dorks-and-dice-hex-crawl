import assert from "node:assert/strict";
import test from "node:test";
import { blockInitiativeHandoffHref, encounterHandoffFromRuntime } from "../.test-dist/encounter-handoff.js";

function runtime() {
    return {
        id: "exp-1",
        name: "Humblewood expedition",
        overworldId: "world-1",
        context: { kind: "WorldBound", name: "Humblewood", overworldId: "world-1", orientation: "PointyTop", hexCenterDistance: null },
        expedition: {
            isSpatial: true,
            currentHex: { q: 2, r: -1 },
            currentDay: 1,
            completedWatches: 2,
            activeWatchNumber: 3,
            activeEncounterKind: "WanderingEncounter",
            activeEncounterHour: 2
        },
        history: [{
            sequence: 30,
            watchNumber: 3,
            kind: "EncounterTriggered",
            expeditionElapsedHours: 10,
            hex: { q: 2, r: -1 },
            message: "WanderingEncounter: owlbear patrol",
            subjectId: null
        }]
    };
}

test("builds a versioned Block Initiative handoff from the authoritative triggered encounter", () => {
    const handoff = encounterHandoffFromRuntime(runtime(), { returnPath: "/tools/hex-crawl/expeditions/exp-1" });
    assert.equal(handoff.version, 1);
    assert.equal(handoff.outcome, "WanderingEncounter");
    assert.equal(handoff.watchNumber, 3);
    assert.equal(handoff.occursAtHours, 2);
    assert.deepEqual(handoff.hex, { q: 2, r: -1 });
    assert.deepEqual(handoff.combatants, []);
    const href = blockInitiativeHandoffHref(handoff);
    assert.match(href, /^\/tools\/block-initiative\?hexEncounter=/);
});

test("does not hand off a no-encounter result", () => {
    assert.equal(encounterHandoffFromRuntime(runtime(), { outcome: "None" }), null);
});
