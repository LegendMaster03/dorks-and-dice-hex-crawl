import assert from "node:assert/strict";
import test from "node:test";
import { manualEntryResolutionSources } from "../.test-dist/modules/expeditions/expedition-input-policy.js";
import { assistantEncounterCheckDue, encounterCheckDue, navigationResolutionDue, pauseInstruction, spatialTravelContinuationTarget, watchActionLabel, watchPhase } from "../.test-dist/modules/expeditions/expedition-workflow.js";

function runtime(overrides = {}) {
    const base = {
        procedure: { runtime: { encounterCadence: "PerWatch", usesNavigationChecks: true } },
        pauseReason: null,
        expedition: { isSpatial: true, activeWatchNumber: null, completedWatches: 0, currentDay: 1 },
        history: []
    };
    return {
        ...base,
        ...overrides,
        procedure: overrides.procedure ?? base.procedure
    };
}

function withExecution(overrides = {}) {
    return { procedure: { runtime: { encounterCadence: "PerWatch", usesNavigationChecks: true, ...overrides } } };
}

test("manual provenance choices can not claim an automatic helper roll", () => {
    assert.deepEqual(manualEntryResolutionSources, ["ProcedureDefault", "ManualRoll", "ExternalSystem", "DmOverride"]);
    assert.equal(manualEntryResolutionSources.includes("AutomaticRoll"), false);
});

test("conditional workflow hides resolutions that are not due", () => {
    const none = runtime(withExecution({ encounterCadence: "None", usesNavigationChecks: false }));
    assert.equal(encounterCheckDue(none), false);
    assert.equal(navigationResolutionDue(none, false, false), false);

    const watch = runtime();
    assert.equal(encounterCheckDue(watch), true);
    const resolvedWatch = {
        ...watch,
        history: [{ kind: "EncounterCheckPerformed", watchNumber: 1, expeditionElapsedHours: 0 }]
    };
    assert.equal(encounterCheckDue(resolvedWatch), false);
    const followingWatch = {
        ...resolvedWatch,
        expedition: { ...resolvedWatch.expedition, completedWatches: 1 }
    };
    assert.equal(encounterCheckDue(followingWatch), true);
    assert.equal(navigationResolutionDue(watch, false, false), true);
    const navigationResolved = {
        ...watch,
        history: [{ kind: "NavigationCheckResolved", watchNumber: 1, expeditionElapsedHours: 0 }]
    };
    assert.equal(navigationResolutionDue(navigationResolved, false, false), false);
    const nextNavigationWatch = {
        ...navigationResolved,
        expedition: { ...navigationResolved.expedition, completedWatches: 1 }
    };
    assert.equal(navigationResolutionDue(nextNavigationWatch, false, false), true);
    assert.equal(navigationResolutionDue(watch, true, false), false);
    assert.equal(navigationResolutionDue(watch, false, true), false);
});

test("spatial continuation routes each unresolved requirement before authoritative advance", () => {
    const base = runtime();
    assert.equal(spatialTravelContinuationTarget(base, false, true), "course");
    assert.equal(spatialTravelContinuationTarget(base, true, true), "navigation");
    assert.equal(spatialTravelContinuationTarget(base, true, true, true), "encounter");

    const navigationResolved = {
        ...base,
        history: [{ kind: "NavigationCheckResolved", watchNumber: 1, expeditionElapsedHours: 0 }]
    };
    assert.equal(spatialTravelContinuationTarget(navigationResolved, true, false), "encounter");

    const allChecksResolved = {
        ...navigationResolved,
        history: [
            ...navigationResolved.history,
            { kind: "EncounterCheckPerformed", watchNumber: 1, expeditionElapsedHours: 0 }
        ]
    };
    assert.equal(spatialTravelContinuationTarget(allChecksResolved, true, false), "movement");
    assert.equal(spatialTravelContinuationTarget(allChecksResolved, true, true), "advance");
});

test("forced travel and pending consequences block routine continuation without outranking encounter pauses", () => {
    const base = runtime();

    assert.equal(
        spatialTravelContinuationTarget(base, true, true, true, false, true),
        "survival");
    assert.equal(
        spatialTravelContinuationTarget(
            { ...base, pauseReason: "EncounterTriggered" },
            true,
            true,
            true,
            false,
            true),
        "encounter");
    assert.equal(
        spatialTravelContinuationTarget(
            { ...base, pauseReason: "LostRecognitionRequired" },
            true,
            true,
            true,
            false,
            true),
        "boundary");
});

test("encounter pause requires explicit post-encounter resume before travel can continue", () => {
    const paused = { ...runtime(), pauseReason: "EncounterTriggered" };

    assert.equal(
        spatialTravelContinuationTarget(paused, true, true, true, false, false, false),
        "encounter");
    assert.equal(
        spatialTravelContinuationTarget(paused, true, true, true, false, false, true),
        "advance");
});

test("spatial continuation respects blocking pauses and never fabricates spatial work", () => {
    const base = runtime();
    assert.equal(
        spatialTravelContinuationTarget({ ...base, pauseReason: "EncounterTriggered" }, true, true),
        "encounter");
    assert.equal(
        spatialTravelContinuationTarget({ ...base, pauseReason: "LostRecognitionRequired" }, true, true),
        "boundary");

    const nonSpatial = {
        ...base,
        expedition: { ...base.expedition, isSpatial: false }
    };
    assert.equal(spatialTravelContinuationTarget(nonSpatial, true, true), "unavailable");

    const structural = { ...base, procedure: { runtime: null } };
    assert.equal(spatialTravelContinuationTarget(structural, true, true), "unavailable");
});

test("structural procedures do not expose executable watch resolutions", () => {
    const structural = runtime({ procedure: { runtime: null } });
    assert.equal(encounterCheckDue(structural), false);
    assert.equal(assistantEncounterCheckDue(structural), false);
    assert.equal(navigationResolutionDue(structural, false, false), false);
    assert.equal(watchActionLabel(structural), "Procedure not executable");
});

test("per-day cadence only requests one encounter resolution per expedition day", () => {
    const due = runtime({
        ...withExecution({ encounterCadence: "PerDay", usesNavigationChecks: false }),
        expedition: { activeWatchNumber: null, completedWatches: 0, currentDay: 2 }
    });
    assert.equal(encounterCheckDue(due), true);

    const resolved = { ...due, history: [{ kind: "EncounterCheckPerformed", expeditionElapsedHours: 25 }] };
    assert.equal(encounterCheckDue(resolved), false);

    const previousDayOnly = { ...due, history: [{ kind: "EncounterCheckPerformed", expeditionElapsedHours: 12 }] };
    assert.equal(encounterCheckDue(previousDayOnly), true);
});

test("partial watch stays a resume workflow and exposes pending decisions", () => {
    const paused = runtime({
        pauseReason: "ConditionsReviewRequired",
        expedition: { isSpatial: true, activeWatchNumber: 3, completedWatches: 2, currentDay: 1 }
    });
    assert.equal(watchPhase(paused), "paused");
    assert.equal(watchActionLabel(paused), "Resume watch 3");
    assert.match(pauseInstruction(paused), /same watch/i);

    const encounter = { ...paused, pauseReason: "EncounterTriggered" };
    assert.match(pauseInstruction(encounter), /encounter interrupted/i);

    const lost = { ...paused, pauseReason: "LostRecognitionRequired" };
    assert.match(pauseInstruction(lost), /reorients/i);
});

test("focused encounter assistant does not duplicate a per-watch check", () => {
    const upcoming = runtime({
        expedition: { isSpatial: true, activeWatchNumber: null, completedWatches: 2, currentDay: 1 }
    });
    assert.equal(assistantEncounterCheckDue(upcoming), true);

    const recorded = {
        ...upcoming,
        history: [{ kind: "EncounterCheckPerformed", watchNumber: 3, expeditionElapsedHours: 8 }]
    };
    assert.equal(assistantEncounterCheckDue(recorded), false);

    const nextWatch = {
        ...recorded,
        expedition: { ...recorded.expedition, completedWatches: 3 }
    };
    assert.equal(assistantEncounterCheckDue(nextWatch), true);
});

test("focused encounter assistant respects per-day history", () => {
    const currentDay = runtime({
        ...withExecution({ encounterCadence: "PerDay", usesNavigationChecks: false }),
        expedition: { isSpatial: true, activeWatchNumber: null, completedWatches: 3, currentDay: 2 },
        history: [{ kind: "EncounterCheckPerformed", watchNumber: 3, expeditionElapsedHours: 25 }]
    });
    assert.equal(assistantEncounterCheckDue(currentDay), false);
});

test("non-spatial active watch still permits its encounter-cadence check", () => {
    const active = runtime({
        expedition: {
            isSpatial: false,
            activeWatchNumber: 2,
            completedWatches: 1,
            currentDay: 1
        }
    });
    assert.equal(assistantEncounterCheckDue(active), true);

    const recorded = {
        ...active,
        history: [{ kind: "EncounterCheckPerformed", watchNumber: 2, expeditionElapsedHours: 5 }]
    };
    assert.equal(assistantEncounterCheckDue(recorded), false);
});
