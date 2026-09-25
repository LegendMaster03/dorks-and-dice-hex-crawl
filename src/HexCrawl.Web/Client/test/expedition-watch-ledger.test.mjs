import test from "node:test";
import assert from "node:assert/strict";
import { buildWatchLedger } from "../.test-dist/modules/expeditions/expedition-watch-ledger.js";

function event(sequence, watchNumber, kind, elapsed, hex, message, distanceValue = null, distanceUnit = null) {
    return {
        sequence,
        watchNumber,
        kind,
        expeditionElapsedHours: elapsed,
        hex,
        message,
        distanceValue,
        distanceUnit,
        subjectId: null,
        subjectType: null
    };
}

function runtime(overrides = {}) {
    return {
        profile: { watchHours: 4 },
        pauseReason: null,
        expedition: {
            completedWatches: 1,
            activeWatchNumber: null,
            activeWatchTotalHours: null,
            activeWatchRemainingHours: null,
            activeEncounterKind: null,
            activeEncounterHandled: null,
            ...overrides.expedition
        },
        history: overrides.history ?? []
    };
}

test("watch ledger aggregates one row per watch from structured events", () => {
    const ledger = buildWatchLedger(runtime({
        history: [
            event(1, 1, "WatchStarted", 0, { q: 0, r: 0 }, "Watch started."),
            event(2, 1, "NavigationCheckResolved", 0, { q: 0, r: 0 }, "Navigation check succeeded."),
            event(3, 1, "EncounterCheckPerformed", 0, { q: 0, r: 0 }, "Encounter check resolved as None."),
            event(4, 1, "HexEntered", 2, { q: 1, r: 0 }, "Entered hex."),
            event(5, 1, "DistanceTraveled", 4, { q: 1, r: 0 }, "Traveled.", 12, "mi"),
            event(6, 1, "WatchCompleted", 4, { q: 1, r: 0 }, "Watch completed.")
        ]
    }));

    assert.equal(ledger.length, 1);
    assert.deepEqual(ledger[0], {
        day: 1,
        watchNumber: 1,
        progress: "12 mi · 4 h elapsed",
        route: "0,0 → 1,0",
        navigation: "Succeeded",
        encounter: "No encounter triggered",
        status: "Complete"
    });
});

test("active watch exposes pending encounter without parsing prose", () => {
    const ledger = buildWatchLedger(runtime({
        expedition: {
            completedWatches: 1,
            activeWatchNumber: 2,
            activeWatchTotalHours: 4,
            activeWatchRemainingHours: 3,
            activeEncounterKind: "WanderingEncounter",
            activeEncounterHandled: false
        },
        history: [
            event(10, 2, "WatchStarted", 4, { q: 1, r: 0 }, "Watch started."),
            event(11, 2, "EncounterCheckPerformed", 4, { q: 1, r: 0 }, "Encounter check resolved.")
        ]
    }));

    assert.equal(ledger[0].encounter, "Pending wandering encounter");
    assert.equal(ledger[0].status, "Active");
});

test("focused resolution for the upcoming watch is shown as prepared", () => {
    const ledger = buildWatchLedger(runtime({
        expedition: {
            completedWatches: 1,
            activeWatchNumber: null,
            activeWatchTotalHours: null,
            activeWatchRemainingHours: null,
            activeEncounterKind: null,
            activeEncounterHandled: null
        },
        history: [
            event(20, 2, "EncounterCheckPerformed", 4, { q: 1, r: 0 }, "Encounter cadence assistant recorded None.")
        ]
    }));

    assert.equal(ledger[0].watchNumber, 2);
    assert.equal(ledger[0].status, "Prepared");
});

test("watch ledger keeps distinct distance units separate instead of assuming conversion", () => {
    const ledger = buildWatchLedger(runtime({
        history: [
            event(1, 1, "DistanceTraveled", 1, { q: 0, r: 0 }, "A", 2, "mi"),
            event(2, 1, "DistanceTraveled", 2, { q: 0, r: 0 }, "B", 3, "hex")
        ]
    }));

    assert.equal(ledger[0].progress, "2 mi + 3 hex · 2 h elapsed");
});
