import test from "node:test";
import assert from "node:assert/strict";
import {
    expeditionWorkspaceAction,
    expeditionWorkspacePresentation
} from "../.test-dist/modules/expeditions/expedition-workspace-model.js";

function nonSpatialRuntime({
    activeWatchNumber = null,
    completedWatches = 0,
    elapsedTravelHours = 0,
    currentDay = 1,
    intervalSupport = "Supported",
    intervalHours = 4,
    modules = [{ moduleKey: "time.interval" }]
} = {}) {
    return {
        pauseReason: null,
        procedure: {
            name: "Test procedure",
            runtime: {},
            focusedIntervalPolicy: {
                support: intervalSupport,
                intervalHours,
                unsupportedReason: null
            },
            modules
        },
        expedition: {
            isSpatial: false,
            activeWatchNumber,
            activeWatchRemainingHours: activeWatchNumber === null ? null : 2,
            completedWatches,
            elapsedTravelHours,
            currentDay
        }
    };
}

test("active nonspatial interval resumes through the watch action rather than spatial travel", () => {
    const runtime = nonSpatialRuntime({
        activeWatchNumber: 3,
        completedWatches: 2,
        elapsedTravelHours: 10
    });

    const action = expeditionWorkspaceAction(runtime, null);

    assert.equal(action.kind, "watch");
    assert.equal(action.label, "Resume watch 3");
    assert.match(action.detail, /without fabricating spatial travel state/i);
});

test("no-interval nonspatial presentation reports elapsed time without fabricating watch language", () => {
    const runtime = nonSpatialRuntime({
        intervalSupport: "None",
        intervalHours: null,
        elapsedTravelHours: 7.5,
        currentDay: 2,
        modules: [{ moduleKey: "journey.process" }]
    });

    const presentation = expeditionWorkspacePresentation(runtime, null, null);

    assert.equal(presentation.timeLabel, "Day 2 · 7.5 h elapsed");
    assert.doesNotMatch(presentation.timeLabel, /watch/i);
});

test("real interval presentation retains watch bookkeeping", () => {
    const runtime = nonSpatialRuntime({
        activeWatchNumber: 2,
        completedWatches: 1,
        elapsedTravelHours: 6,
        currentDay: 1
    });

    const presentation = expeditionWorkspacePresentation(runtime, null, null);

    assert.match(presentation.timeLabel, /watch 2/i);
    assert.match(presentation.timeLabel, /2 h remaining/i);
});
