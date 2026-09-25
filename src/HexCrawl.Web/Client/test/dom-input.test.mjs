import test from "node:test";
import assert from "node:assert/strict";
import { integer, numeric } from "../.test-dist/ui/dom.js";

function field(value, name = "testValue") {
    return {
        value,
        getAttribute(attribute) {
            return attribute === "name" ? name : null;
        }
    };
}

test("numeric input rejects blank values instead of coercing them to zero", () => {
    assert.throws(() => numeric(field("")), /requires a number/);
    assert.throws(() => numeric(field("   ")), /requires a number/);
    assert.equal(numeric(field("0")), 0);
    assert.equal(numeric(field("12.5")), 12.5);
});

test("integer input rejects blank and fractional values", () => {
    assert.throws(() => integer(field("")), /requires an integer/);
    assert.throws(() => integer(field("1.5")), /must be an integer/);
    assert.equal(integer(field("-2")), -2);
});
