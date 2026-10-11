import assert from "node:assert/strict";
import test from "node:test";
import { encounterLocationId } from "../.test-dist/modules/expeditions/keyed-location-encounter.js";

const locations = [{id:"123e4567-e89b-12d3-a456-426614174000"}, {id:"123e4567-e89b-12d3-a456-426614174001"}];

test("keyed discovery carries the selected saved location identity", () => {
    assert.equal(encounterLocationId("KeyedLocationDiscovery", locations[1].id, locations), locations[1].id);
});
test("keyed discovery rejects blank, stale and foreign world locations", () => {
    assert.throws(() => encounterLocationId("KeyedLocationDiscovery", "", locations), /Choose an existing/);
    assert.throws(() => encounterLocationId("KeyedLocationDiscovery", "foreign", locations), /Choose an existing/);
    assert.throws(() => encounterLocationId("KeyedLocationDiscovery", locations[0].id, []), /Choose an existing/);
});
test("nonkeyed encounters discard a previous keyed choice", () => {
    for (const kind of ["None", "WanderingEncounter", "ManualCustom", ""])
        assert.equal(encounterLocationId(kind, locations[0].id, locations), null);
});
