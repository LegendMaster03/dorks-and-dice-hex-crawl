import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { hexToWorld } from "../.test-dist/hex-math.js";
import {
    applicableFeatureEnvironmentFacts,
    featureHasEnvironmentRules,
    featuresIntersectingCell,
    locationsInCell,
    removeEnvironmentFact,
    replaceFeatureTagFact,
    replaceHexTerrain,
    terrainFactsForCell
} from "../.test-dist/modules/worlds/selected-cell-authoring.js";

const sourceRoot = path.resolve("src");

function grid(overrides = {}) {
    return {
        id: "grid",
        orientation: "PointyTop",
        coordinateConvention: "AxialQr",
        origin: { x: 17.5, y: -9.25 },
        rotationDegrees: 31,
        hexRadiusWorldUnits: 2,
        neighborCenterDistance: {
            value: 6,
            unit: { kind: "Mile", symbol: "mi", metersPerUnit: 1609.344 }
        },
        ...overrides
    };
}

function world(overrides = {}) {
    return {
        id: "world",
        name: "World",
        version: 1,
        createdAt: "",
        updatedAt: "",
        grid: grid(),
        features: [],
        locations: [],
        sourceMaps: [],
        ...overrides
    };
}

function environment(annotations = []) {
    return { overworldId: "world", version: 1, annotations };
}

function ids(...values) {
    let index = 0;
    return () => values[index++] ?? `id-${index}`;
}

test("location membership follows the authoritative rotated grid transform", () => {
    for (const orientation of ["PointyTop", "FlatTop"]) {
        const g = grid({ orientation, rotationDegrees: 43 });
        const target = { q: 4, r: -2 };
        const other = { q: -3, r: 5 };
        const targetCenter = hexToWorld(g, target);
        const w = world({
            grid: g,
            locations: [
                { id: "target", name: "Ruined Tower", category: "Ruin", position: targetCenter, discoverability: "Hidden" },
                { id: "exact", name: "Exact Camp", category: "Camp", position: { x: targetCenter.x + .15, y: targetCenter.y - .1 }, discoverability: "Obvious" },
                { id: "other", name: "Village", category: "Settlement", position: hexToWorld(g, other), discoverability: "Obvious" }
            ]
        });
        assert.deepEqual(locationsInCell(w, target).map(value => value.id), ["target", "exact"]);
        assert.equal(locationsInCell(w, target)[0].discoverability, "Hidden");
    }
});

test("one continuous line feature is derived as intersecting each crossed cell", () => {
    const g = grid({ origin: { x: 0, y: 0 }, rotationDegrees: 17 });
    const feature = {
        id: "road",
        name: "Old King's Road",
        category: "road",
        kind: "Line",
        position: null,
        path: [
            hexToWorld(g, { q: -1, r: 0 }),
            hexToWorld(g, { q: 1, r: 0 })
        ],
        boundary: null
    };
    const w = world({ grid: g, features: [feature] });
    for (const cell of [{ q: -1, r: 0 }, { q: 0, r: 0 }, { q: 1, r: 0 }]) {
        assert.deepEqual(featuresIntersectingCell(w, cell).map(value => value.id), ["road"]);
    }
    assert.equal(w.features.length, 1);
});

test("region category remains semantic geometry and does not fabricate terrain mechanics", () => {
    const g = grid({ origin: { x: 0, y: 0 }, rotationDegrees: 0 });
    const center = hexToWorld(g, { q: 0, r: 0 });
    const feature = {
        id: "forest-region",
        name: "Blackwood",
        category: "forest",
        kind: "Region",
        position: null,
        path: null,
        boundary: [
            { x: center.x - 1, y: center.y - 1 },
            { x: center.x + 1, y: center.y - 1 },
            { x: center.x + 1, y: center.y + 1 },
            { x: center.x - 1, y: center.y + 1 }
        ]
    };
    const w = world({ grid: g, features: [feature] });
    assert.deepEqual(featuresIntersectingCell(w, { q: 0, r: 0 }).map(value => value.id), ["forest-region"]);
    assert.deepEqual(applicableFeatureEnvironmentFacts(environment(), [feature]), []);
});

test("selected-cell terrain replacement removes contradictory terrain but preserves other facts and cells", () => {
    const selected = { q: 0, r: 0 };
    const other = { q: 1, r: 0 };
    const original = [{
        id: "selected-annotation",
        scope: { kind: "Hex", hex: selected, featureId: null },
        facts: [
            { id: "terrain-a", dimension: "terrain", valueKind: "Tag", tag: "swamp", measurement: null, provenance: null, note: null },
            { id: "hazard", dimension: "hazard", valueKind: "Tag", tag: "quicksand", measurement: null, provenance: null, note: null },
            { id: "terrain-b", dimension: "Terrain", valueKind: "Tag", tag: "marsh", measurement: null, provenance: null, note: null }
        ]
    }, {
        id: "other-annotation",
        scope: { kind: "Hex", hex: other, featureId: null },
        facts: [{ id: "other-terrain", dimension: "terrain", valueKind: "Tag", tag: "desert", measurement: null, provenance: null, note: null }]
    }];

    const replaced = replaceHexTerrain(original, selected, "forest", "test", "explicit", ids("new-fact"));
    const selectedTerrain = terrainFactsForCell(environment(replaced), selected);
    assert.equal(selectedTerrain.length, 1);
    assert.equal(selectedTerrain[0].fact.tag, "forest");
    assert.equal(selectedTerrain[0].fact.provenance, "test");
    assert.equal(selectedTerrain[0].fact.note, "explicit");
    assert.equal(replaced.flatMap(value => value.facts).some(value => value.id === "hazard"), true);
    assert.equal(terrainFactsForCell(environment(replaced), other)[0].fact.tag, "desert");

    const removed = replaceHexTerrain(replaced, selected, null);
    assert.equal(terrainFactsForCell(environment(removed), selected).length, 0);
    assert.equal(removed.flatMap(value => value.facts).some(value => value.id === "hazard"), true);
});

test("feature-scoped mechanics replace only the matching feature dimension", () => {
    const original = [{
        id: "road-rules",
        scope: { kind: "SpatialFeature", hex: null, featureId: "road" },
        facts: [
            { id: "route-old", dimension: "route", valueKind: "Tag", tag: "rough-road", measurement: null, provenance: null, note: null },
            { id: "visibility", dimension: "visibility", valueKind: "Tag", tag: "open", measurement: null, provenance: null, note: null }
        ]
    }];
    const next = replaceFeatureTagFact(original, "road", "route", "good-road", "test", null, ids("new-fact", "new-annotation"));
    const facts = next.flatMap(value => value.facts);
    assert.equal(facts.filter(value => value.dimension.toLowerCase() === "route").length, 1);
    assert.equal(facts.find(value => value.dimension.toLowerCase() === "route").tag, "good-road");
    assert.equal(facts.some(value => value.id === "visibility"), true);
    assert.equal(featureHasEnvironmentRules(environment(next), "road"), true);

    const removed = removeEnvironmentFact(next, "new-annotation", "new-fact");
    assert.equal(featureHasEnvironmentRules(environment(removed), "road"), true, "visibility remains an attached environment rule");
});

test("selected-cell derivation stays bounded for a representative authored world", () => {
    const g = grid({ origin: { x: 0, y: 0 }, rotationDegrees: 23 });
    const locations = Array.from({ length: 1200 }, (_, index) => {
        const cell = { q: (index % 41) - 20, r: (Math.floor(index / 41) % 41) - 20 };
        return {
            id: `location-${index}`,
            name: `Location ${index}`,
            category: "site",
            position: hexToWorld(g, cell),
            discoverability: index % 3 === 0 ? "Hidden" : "Obvious"
        };
    });
    const features = Array.from({ length: 180 }, (_, index) => {
        const row = (index % 19) - 9;
        return {
            id: `line-${index}`,
            name: `Line ${index}`,
            category: index % 2 === 0 ? "road" : "river",
            kind: "Line",
            position: null,
            path: [
                hexToWorld(g, { q: -20, r: row }),
                hexToWorld(g, { q: 20, r: row })
            ],
            boundary: null
        };
    });
    const representative = world({ grid: g, locations, features });
    const started = performance.now();
    const selectedLocations = locationsInCell(representative, { q: 0, r: 0 });
    const selectedFeatures = featuresIntersectingCell(representative, { q: 0, r: 0 });
    const elapsed = performance.now() - started;

    assert.ok(selectedLocations.length >= 1);
    assert.ok(selectedFeatures.length >= 1);
    assert.ok(elapsed < 1500, `representative selected-cell derivation took ${elapsed.toFixed(1)}ms`);
});

test("world editor composes existing authorities and does not mutate player knowledge when selecting content", () => {
    const source = fs.readFileSync(path.join(sourceRoot, "modules/worlds/world-editor-view.ts"), "utf8");
    assert.match(source, /setHexSelectionHandler/);
    assert.match(source, /locationsInCell\(world, cell\)/);
    assert.match(source, /featuresIntersectingCell\(world, cell\)/);
    assert.match(source, /replaceHexTerrain/);
    assert.match(source, /api\.createLocation/);
    assert.match(source, /api\.createFeature/);
    assert.match(source, /kind: "Line"/);
    assert.match(source, /replaceFeatureTagFact/);
    assert.match(source, /featureHasEnvironmentRules/);
    assert.doesNotMatch(source, /revealSubject|discoverSubject|PlayerKnowledge|playerKnowledge/);
});
