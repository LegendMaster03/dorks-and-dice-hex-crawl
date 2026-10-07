import assert from "node:assert/strict";
import test from "node:test";

import { presetGuidance } from "../.test-dist/modules/procedures/preset-guidance.js";

function module(moduleKey, parameters, automationLevel = "Assisted") {
    return { moduleKey, parameters, automationLevel };
}

function procedure(modules) {
    return { modules };
}

test("preset guidance excludes disabled native navigation, encounters, and helpers from minimal travel", () => {
    const guidance = presetGuidance(procedure([
        module("time.interval", { durationTicks: "144000000000" }, "Automatic"),
        module("movement.resolution", {
            travelResolution: "ContinuousDistance",
            actualDistanceResolution: "Fixed",
            tracksIntraHexProgress: "true"
        }, "Automatic"),
        module("movement.hex-progress", {
            directionChangesCostProgress: "false",
            supportsDeliberateDoubleBack: "false"
        }, "Automatic"),
        module("navigation.check", {
            usesNavigationChecks: "false",
            usesPersistentVeer: "false"
        }),
        module("encounters.cadence", { cadence: "None" }),
        module("procedure.helpers", {
            "travel.enabled": "false",
            "navigation.enabled": "false",
            "encounter.enabled": "false"
        })
    ]));

    assert.equal(guidance.areaCount, 1);
    assert.equal(guidance.breadth, "Focused");
    assert.match(guidance.bestFor, /repeated distance or cell travel/i);
    assert.match(guidance.manage, /travel time and movement/i);
    assert.doesNotMatch(guidance.manage, /navigation/i);
    assert.doesNotMatch(guidance.manage, /encounter/i);
    assert.doesNotMatch(guidance.manage, /automatic result generation/i);
});

test("preset guidance recognizes richer pace, terrain, activity, and extended-travel behavior", () => {
    const guidance = presetGuidance(procedure([
        module("time.interval", { durationTicks: "36000000000" }, "Automatic"),
        module("movement.budget", {
            budgetModel: "speed-and-pace",
            baseBudget: "1",
            budgetUnit: "hour",
            limitingScope: "slowest-traveler",
            travelModeKeys: "normal;fast;slow"
        }),
        module("movement.terrain", {
            adjustmentModel: "maximum-pace",
            terrainAdjustments: "forest=normal;mountain=slow",
            routeAdjustmentModel: "good-road-improves-one-step",
            weatherAdjustmentModel: "environment-specific"
        }),
        module("party.activities", {
            assignmentScope: "participant",
            activityBudgetModel: "travel-compatible",
            activityKeys: "navigate;forage;search;watch;stealth",
            roleKeys: "navigator;lookout"
        }, "Manual"),
        module("time.forced-travel", {
            normalTravelLimit: "8",
            limitUnit: "hours",
            checkModel: "escalating-constitution-save",
            failureConsequence: "exhaustion"
        }),
        module("effects.expedition", {
            effectKinds: "exhaustion",
            accumulationModel: "levels",
            recoveryModel: "rules-defined-rest",
            scope: "participant"
        }, "Manual")
    ]));

    assert.equal(guidance.areaCount, 3);
    assert.equal(guidance.breadth, "Moderate");
    assert.match(guidance.bestFor, /pace- or budget-based travel/i);
    assert.match(guidance.bestFor, /terrain/i);
    assert.match(guidance.bestFor, /party activities/i);
    assert.match(guidance.bestFor, /extended-travel or survival consequences/i);
    assert.doesNotMatch(guidance.bestFor, /small foundation/i);
    assert.match(guidance.manage, /travel roles and activities \(manual tracking\)/i);
    assert.match(guidance.manage, /survival, resources, and expedition effects \(DM-assisted \+ manual tracking\)/i);
});

test("preset guidance includes result-generation workload only when a helper is enabled", () => {
    const guidance = presetGuidance(procedure([
        module("time.interval", { durationTicks: "144000000000" }, "Automatic"),
        module("procedure.helpers", {
            "travel.enabled": "true",
            "navigation.enabled": "false",
            "encounter.enabled": "false"
        })
    ]));

    assert.equal(guidance.areaCount, 2);
    assert.match(guidance.manage, /automatic result generation/i);
});
