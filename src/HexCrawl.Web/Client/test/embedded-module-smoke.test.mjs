import assert from "node:assert/strict";
import test from "node:test";
import { HexCrawlApi } from "../.embedded-smoke-dist/api.js";

test("Embedded Module mode routes persistent reads, writes, map upload, and map assets through Tool Host upstream", async () => {
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
        const method = init.method ?? "GET";
        calls.push({ url, method, form: init.body instanceof FormData });
        if (url === "/tool-context") return Response.json(context);
        if (url === "/tool-host/hex-crawl/api/upstream/api/overworlds") {
            if (method === "POST") return Response.json({ id: "world-1", version: 1 });
            return Response.json([]);
        }
        if (url === "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps" && method === "POST") {
            return Response.json({ id: "world-1", version: 2, sourceMaps: [] });
        }
        if (url === "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps/wonderdraft/inspect" && method === "POST") {
            return Response.json({
                formatVersion: 15,
                pixelWidth: 1024,
                pixelHeight: 768,
                symbolCount: 2,
                labelCount: 1,
                pathCount: 3,
                territoryCount: 2,
                hasGrid: true,
                includedPacks: ["Custom Pack"],
                includedDefaultPacks: []
            });
        }
        if (url === "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps/map-1/wonderdraft/candidates" && method === "POST") {
            return Response.json({
                sourceMapId: "map-1",
                sourceScaleX: 1,
                sourceScaleY: 1,
                summary: {
                    formatVersion: 15,
                    pixelWidth: 1024,
                    pixelHeight: 768,
                    symbolCount: 2,
                    labelCount: 1,
                    pathCount: 3,
                    territoryCount: 2,
                    hasGrid: true,
                    includedPacks: ["Custom Pack"],
                    includedDefaultPacks: []
                },
                candidates: []
            });
        }
        if (url === "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps/map-1/wonderdraft/import" && method === "POST") {
            return Response.json({ id: "world-1", version: 3, sourceMaps: [], locations: [], features: [] });
        }
        if (url === "/tool-host/hex-crawl/api/upstream/api/expeditions" && method === "POST") {
            return Response.json({
                id: "session-1",
                version: 1,
                overworldId: null,
                context: { kind: "NonSpatial", name: "Hosted watch", overworldId: null }
            });
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

        const file = new File([new Uint8Array([137, 80, 78, 71])], "map.png", { type: "image/png" });
        await api.uploadSourceMap("world-1", {
            file,
            name: "Hosted map",
            geographyKey: "Hosted geography",
            role: "Neutral",
            containsBakedGrid: false,
            expectedVersion: 1
        });
        assert.equal(
            api.sourceMapAssetUrl("world-1", "map-1"),
            "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps/map-1/asset");

        const project = new File([new Uint8Array([71, 67, 80, 70])], "world.wonderdraft_map");
        const inspection = await api.inspectWonderdraftProject("world-1", project);
        assert.equal(inspection.formatVersion, 15);
        assert.equal(inspection.symbolCount, 2);
        const candidatePreview = await api.previewWonderdraftCandidates("world-1", "map-1", project);
        assert.equal(candidatePreview.sourceMapId, "map-1");
        const importedWorld = await api.importWonderdraftCandidates("world-1", "map-1", project, [{
            candidateKey: "label:0",
            target: "Location",
            name: "Old Harbor",
            category: "settlement",
            discoverability: "Obvious"
        }], 2);
        assert.equal(importedWorld.version, 3);

        const standalone = await api.startStandaloneSession({
            name: "Hosted watch",
            procedureKey: "simple-fixed-distance",
            context: { kind: "NonSpatial", name: "Hosted watch" }
        });
        assert.equal(standalone.overworldId, null);
        assert.equal(standalone.context.kind, "NonSpatial");

        assert.deepEqual(calls.map(call => ({ url: call.url, method: call.method })), [
            { url: "/tool-context", method: "GET" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds", method: "GET" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds", method: "POST" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps", method: "POST" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps/wonderdraft/inspect", method: "POST" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps/map-1/wonderdraft/candidates", method: "POST" },
            { url: "/tool-host/hex-crawl/api/upstream/api/overworlds/world-1/source-maps/map-1/wonderdraft/import", method: "POST" },
            { url: "/tool-host/hex-crawl/api/upstream/api/expeditions", method: "POST" }
        ]);
        const uploadCall = calls.find(call => call.url.endsWith("/source-maps") && call.method === "POST");
        assert.equal(uploadCall?.form, true);
        const inspectCall = calls.find(call => call.url.endsWith("/wonderdraft/inspect") && call.method === "POST");
        assert.equal(inspectCall?.form, true);
    } finally {
        globalThis.fetch = originalFetch;
    }
});
