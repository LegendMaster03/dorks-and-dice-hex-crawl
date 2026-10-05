import assert from "node:assert/strict";
import test from "node:test";
import { blockInitiativeHandoffHref, encounterHandoffStoragePrefix } from "../.test-dist/encounter-handoff.js";

function handoff() {
    return {
        version: 2,
        sourceTool: "hex-crawl",
        identity: {
            handoffId: "0e953165-42ca-48b5-9a74-d834ddfbcd73",
            encounterOccurrenceId: "runtime:30",
            expeditionId: "ef8ec6ef-41f9-47de-b9d1-3ed6e23f0867",
            expeditionName: "Humblewood expedition"
        },
        returnContext: { returnPath: "/tools/hex-crawl/expeditions/ef8ec6ef-41f9-47de-b9d1-3ed6e23f0867" },
        timeContext: { day: 1, watchNumber: 3, expeditionElapsedHours: 10 },
        worldContext: {
            overworldId: "1da3a793-6b85-4472-b006-28969a60f5a2",
            hex: { q: 2, r: -1 },
            location: null
        },
        encounter: {
            outcome: "WanderingEncounter",
            summary: "WanderingEncounter: owlbear patrol",
            dmNote: null
        },
        combatants: [],
        circumstances: [],
        effects: [],
        resources: [],
        journeyProvenance: null,
        linkedScenes: []
    };
}

test("stages the exact server-produced v2 handoff and carries only its stable id in the URL", () => {
    const expected = handoff();
    const stored = new Map();
    const storage = { setItem: (key, value) => stored.set(key, value) };
    const href = blockInitiativeHandoffHref(expected, storage);
    const url = new URL(href, "https://dorks-and-dice.test");

    assert.equal(url.pathname, "/tools/block-initiative");
    assert.equal(url.searchParams.get("hexEncounterId"), expected.identity.handoffId);
    assert.equal(url.searchParams.has("hexEncounter"), false);
    assert.deepEqual(
        JSON.parse(stored.get(`${encounterHandoffStoragePrefix}${expected.identity.handoffId}`)),
        expected);
});
