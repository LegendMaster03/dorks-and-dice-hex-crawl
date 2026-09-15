import assert from "node:assert/strict";
import test from "node:test";
import { HexCrawlApi } from "../.embedded-smoke-dist/api.js";

test("Embedded Module mode routes persistent reads and writes through Tool Host upstream", async () => {
    const originalFetch = globalThis.fetch;
    const calls = [];
    const context = {
        contractVersion: 1,
        toolSlug: "hex-crawl",
        siteMode: "dorks",
        apiBaseUrl: "/tool-host/hex-crawl/api/",
        toolBasePath: "/tools/hex-crawl",
        toolRoute: "/worlds"
    };

    globalThis.fetch = async (input, init = {}) => {
        const url = String(input);
        calls.push({ url, method: init.method ?? "GET" });
        if (url === "/tool-context") return Response.json(context);
        if (url === "/tool-host/hex-crawl/api/upstream/api/overworlds") {
            if ((init.method ?? "GET") === "POST") return Response.json({ id: "world-1" });
            return Response.json([]);
        }
        throw new Error(`Unexpected smoke-test fetch: ${url}`);
    };

    try {
        const root = { dataset: { toolContextUrl: "/tool-context" } };
        const { api, context: loadedContext } = await HexCrawlApi.create(root);
        assert.equal(loadedContext.toolSlug, "hex-crawl");
        assert.deepEqual(await api.listOverworlds(), []);
        const created = await api.createOverworld({
            name: "Smoke world",
            orientation: "PointyTop",
            origin: { x: 0, y: 0 },
            rotationDegrees: 0,
            hexRadiusWorldUnits: 1,
            neighborCenterDistance: 12,
            distanceUnit: { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 }
        });
        assert.equal(created.id, "world-1");
        assert.deepEqual(calls, [
            { url: "/tool-context", method: "GET" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds", method: "GET" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds", method: "POST" }
        ]);
    } finally {
        globalThis.fetch = originalFetch;
    }
});
