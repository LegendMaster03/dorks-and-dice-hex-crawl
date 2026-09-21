import assert from "node:assert/strict";
import test from "node:test";
import { canonicalDistanceUnit, createOverworldInput, customUnitFieldsVisible, gridWithSelectedUnit } from "../.test-dist/modules/worlds/world-form.js";

const draft = {
    name: "Test world",
    orientation: "PointyTop",
    centerDistance: 12,
    unitKind: "Mile",
    customSymbol: "league",
    customMetersPerUnit: 4828.032,
    origin: { x: 0, y: 0 },
    rotationDegrees: 0,
    hexRadiusWorldUnits: 1
};

const grid = {
    id: "grid-1",
    orientation: "PointyTop",
    coordinateConvention: "AxialQr",
    origin: { x: 0, y: 0 },
    rotationDegrees: 0,
    hexRadiusWorldUnits: 1,
    neighborCenterDistance: { value: 6, unit: { kind: "Custom", symbol: "league", metersPerUnit: 4828.032 } }
};

test("predefined units hide custom controls and Custom shows them", () => {
    assert.equal(customUnitFieldsVisible("Mile"), false);
    assert.equal(customUnitFieldsVisible("Kilometer"), false);
    assert.equal(customUnitFieldsVisible("Custom"), true);
});

test("create payload canonicalizes Miles", () => {
    assert.deepEqual(createOverworldInput({ ...draft, unitKind: "Mile" }).distanceUnit,
        { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 });
});

test("create payload canonicalizes Kilometers", () => {
    assert.deepEqual(createOverworldInput({ ...draft, unitKind: "Kilometer" }).distanceUnit,
        { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 });
});

test("create payload preserves supplied Custom unit values", () => {
    assert.deepEqual(createOverworldInput({ ...draft, unitKind: "Custom" }).distanceUnit,
        { kind: "Custom", symbol: "league", metersPerUnit: 4828.032 });
});

test("editor can change Custom to canonical Miles", () => {
    assert.deepEqual(gridWithSelectedUnit(grid, "Mile", 12, "league", 4828.032).neighborCenterDistance,
        { value: 12, unit: { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 } });
});

test("editor can change Custom to canonical Kilometers", () => {
    assert.deepEqual(gridWithSelectedUnit(grid, "Kilometer", 8, "league", 4828.032).neighborCenterDistance,
        { value: 8, unit: { kind: "Kilometer", symbol: "km", metersPerUnit: 1000 } });
});

test("custom unit requires a positive meters-per-unit conversion", () => {
    assert.throws(() => canonicalDistanceUnit("Custom", "u", null), /greater than zero/);
});
