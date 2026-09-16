import assert from "node:assert/strict";
import test from "node:test";
import { apiError } from "../.test-dist/api.js";

test("API errors preserve useful status categories without exposing arbitrary server bodies", async () => {
    const validation = await apiError(new Response(JSON.stringify({ error: "Name is required." }), { status: 400 }), "Create overworld");
    assert.equal(validation.kind, "validation");
    assert.match(validation.message, /Name is required/);

    const conflict = await apiError(new Response(JSON.stringify({ error: "raw internal conflict details" }), { status: 409 }), "Update overworld");
    assert.equal(conflict.kind, "conflict");
    assert.doesNotMatch(conflict.message, /raw internal conflict details/);

    const server = await apiError(new Response("sensitive failure", { status: 500 }), "Update overworld");
    assert.equal(server.kind, "server");
    assert.doesNotMatch(server.message, /sensitive failure/);
});
