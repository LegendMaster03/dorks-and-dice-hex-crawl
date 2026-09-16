import assert from "node:assert/strict";
import test from "node:test";
import { encounterCheckDue, navigationResolutionDue, pauseInstruction, watchActionLabel, watchPhase } from "../.test-dist/expedition-workflow.js";

function runtime(overrides = {}) {
    return {
        profile: { encounterCadence: "PerWatch", usesNavigationChecks: true },
        pauseReason: null,
        expedition: { activeWatchNumber: null, completedWatches: 0, currentDay: 1 },
        history: [],
        ...overrides
    };
}

test("conditional workflow hides resolutions that are not due", () => {
    const none = runtime({ profile: { encounterCadence: "None", usesNavigationChecks: false } });
    assert.equal(encounterCheckDue(none), false);
    assert.equal(navigationResolutionDue(none, false, false), false);

    const watch = runtime();
    assert.equal(encounterCheckDue(watch), true);
    assert.equal(navigationResolutionDue(watch, false, false), true);
    assert.equal(navigationResolutionDue(watch, true, false), false);
    assert.equal(navigationResolutionDue(watch, false, true), false);
});

test("per-day cadence only requests one encounter resolution per expedition day", () => {
    const due = runtime({ profile: { encounterCadence: "PerDay", usesNavigationChecks: false }, expedition: { activeWatchNumber: null, completedWatches: 0, currentDay: 2 } });
    assert.equal(encounterCheckDue(due), true);

    const resolved = { ...due, history: [{ kind: "EncounterCheckPerformed", expeditionElapsedHours: 25 }] };
    assert.equal(encounterCheckDue(resolved), false);

    const previousDayOnly = { ...due, history: [{ kind: "EncounterCheckPerformed", expeditionElapsedHours: 12 }] };
    assert.equal(encounterCheckDue(previousDayOnly), true);
});

test("partial watch stays a resume workflow and exposes pending decisions", () => {
    const paused = runtime({
        pauseReason: "ConditionsReviewRequired",
        expedition: { activeWatchNumber: 3, completedWatches: 2, currentDay: 1 }
    });
    assert.equal(watchPhase(paused), "paused");
    assert.equal(watchActionLabel(paused), "Resume watch 3");
    assert.match(pauseInstruction(paused), /same watch/i);

    const encounter = { ...paused, pauseReason: "EncounterTriggered" };
    assert.match(pauseInstruction(encounter), /encounter interrupted/i);

    const lost = { ...paused, pauseReason: "LostRecognitionRequired" };
    assert.match(pauseInstruction(lost), /reorients/i);
});
