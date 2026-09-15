import assert from "node:assert/strict";
import test from "node:test";
import { HexCrawlApi } from "../.embedded-smoke-dist/api.js";

test("Embedded Module mode loads Tool Host context and routes reads and writes through upstream", async () => {
    const originalFetch = globalThis.fetch;
    const calls = [];
    const context = {
        contractVersion: 2,
        toolSlug: "hex-crawl",
        siteMode: "dorks",
        apiBaseUrl: "/tool-host/hex-crawl/api/",
        toolBasePath: "/tools/hex-crawl",
        toolRoute: "/"
    };

    globalThis.fetch = async (input, init = {}) => {
        const url = String(input);
        calls.push({ url, method: init.method ?? "GET" });

        if (url === "/tool-context") {
            return Response.json(context);
        }

        if (url === "/tool-host/hex-crawl/api/upstream/api/demo/runtime") {
            return Response.json({ mode: "embedded-runtime" });
        }

        if (url === "/tool-host/hex-crawl/api/upstream/api/demo/runtime/advance") {
            assert.equal(init.method, "POST");
            assert.match(String(init.body), /"intendedDirection":0/);
            return Response.json({ mode: "embedded-runtime-advanced" });
        }

        throw new Error(`Unexpected smoke-test fetch: ${url}`);
    };

    try {
        const root = { dataset: { toolContextUrl: "/tool-context" } };
        const { api, context: loadedContext } = await HexCrawlApi.create(root);
        assert.equal(loadedContext.toolSlug, "hex-crawl");

        const runtime = await api.getRuntime();
        assert.equal(runtime.mode, "embedded-runtime");

        const advanced = await api.advanceRuntime({ intendedDirection: 0 });
        assert.equal(advanced.mode, "embedded-runtime-advanced");

        assert.deepEqual(calls, [
            { url: "/tool-context", method: "GET" },
            { url: "/tool-host/hex-crawl/api/upstream/api/demo/runtime", method: "GET" },
            { url: "/tool-host/hex-crawl/api/upstream/api/demo/runtime/advance", method: "POST" }
        ]);
    } finally {
        globalThis.fetch = originalFetch;
    }
});
