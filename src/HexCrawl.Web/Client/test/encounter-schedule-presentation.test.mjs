import assert from "node:assert/strict";
import test from "node:test";
import {
    encounterScheduleConfiguration,
    hasEncounterSchedule
} from "../.test-dist/modules/expeditions/encounter-schedule-presentation.js";

function procedure(modules, runtime = null) {
    return {
        procedureId: "procedure",
        revision: 1,
        key: "procedure",
        name: "Procedure",
        isExecutable: true,
        runtime,
        focusedIntervalPolicy: {
            support: "None",
            intervalHours: null,
            mechanicKey: null,
            mechanicVersion: null,
            executionHandler: null,
            unsupportedReason: null
        },
        modules
    };
}

test("contextual encounter schedule remains discoverable without native cadence runtime", () => {
    const configured = procedure([{
        moduleKey: "encounters.schedule",
        moduleName: "Encounter schedule",
        mechanicKey: "encounters.schedule",
        mechanicVersion: 1,
        executionHandler: "manual",
        automationLevel: "Manual",
        parameters: {
            scheduleModel: "travel-and-camp",
            travelChecksPerInterval: "1",
            campCheck: "true",
            terrainProbabilityModel: "terrain-tagged"
        }
    }]);

    assert.equal(hasEncounterSchedule(configured), true);
    assert.deepEqual(encounterScheduleConfiguration(configured), {
        scheduleModel: "travel-and-camp",
        travelChecksPerInterval: "1",
        campCheck: true,
        terrainProbabilityModel: "terrain-tagged"
    });
});

test("native cadence alone does not invent contextual schedule parameters", () => {
    const configured = procedure([], {
        encounterCadence: "PerWatch"
    });

    assert.equal(hasEncounterSchedule(configured), false);
    assert.equal(encounterScheduleConfiguration(configured), null);
});
