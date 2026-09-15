import assert from "node:assert/strict";
import test from "node:test";
import { hexDistance, hexToWorld, worldToHex } from "../.test-dist/hex-math.js";

const grid = orientation => ({
    id: "grid",
    orientation,
    coordinateConvention: "AxialQr",
    origin: { x: 3.25, y: -1.5 },
    rotationDegrees: 17,
    hexRadiusWorldUnits: 1,
    neighborCenterDistance: { value: 12, unit: { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 } }
});

test("axial distance is deterministic", () => {
    assert.equal(hexDistance({ q: 0, r: 0 }, { q: 3, r: -2 }), 3);
    assert.equal(hexDistance({ q: -4, r: 1 }, { q: 2, r: -3 }), 6);
});

for (const orientation of ["PointyTop", "FlatTop"]) {
    test(`${orientation} projection round-trips stable coordinates`, () => {
        for (const hex of [{ q: 0, r: 0 }, { q: 4, r: -3 }, { q: -5, r: 2 }, { q: 7, r: 6 }]) {
            assert.deepEqual(worldToHex(grid(orientation), hexToWorld(grid(orientation), hex)), hex);
        }
    });
}
