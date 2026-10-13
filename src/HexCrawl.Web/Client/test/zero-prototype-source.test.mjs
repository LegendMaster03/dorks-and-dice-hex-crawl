import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../..");
const sourceRoot = path.join(repositoryRoot, "src");
const excludedDirectories = new Set(["node_modules", "dist", ".test-dist", ".embedded-smoke-dist"]);
const archivedConverters = new Set([
    "HexCrawl.Infrastructure/Persistence/ArchivedGridCutoverGeometry.cs",
    "HexCrawl.Infrastructure/Persistence/PostgresLegacyWorldCutover.cs"
]);

// These identifiers belong to the removed executable prototype, not to hexagonal
// polygon data, the product's existing branding, or the temporary database cutover.
const retiredSymbols = [
    "AbstractHexCrawlSessionContext",
    "AbstractCellSessionGeometry",
    "HexCoordinate",
    "HexGridDefinition",
    "LegacyHexTilingCompatibility",
    "HexGeometry",
    "HasLegacyHexGrid",
    "HexId",
    "KnownHexes",
    "MarkEnteredHexKnown",
    "HexStepTravel",
    "hexProgress",
    "currentHex",
    "selectedHex",
    "worldToHex",
    "startHex",
    "setHexSelectionHandler",
    "gridPreview",
    "hex-grid-analysis",
    "raster-grid-alignment"
];
const retiredPatterns = retiredSymbols.map(symbol => ({
    symbol,
    pattern: new RegExp("(?<![A-Za-z0-9_])" +
        symbol.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") +
        "(?![A-Za-z0-9_])", "g")
}));

function* trackedRuntimeSources(folder) {
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
        if (entry.isDirectory()) {
            if (!excludedDirectories.has(entry.name)) {
                yield* trackedRuntimeSources(path.join(folder, entry.name));
            }
        } else if (entry.isFile() && /\.(?:cs|ts)$/.test(entry.name)
            && !entry.name.endsWith(".d.ts")) {
            yield path.join(folder, entry.name);
        }
    }
}

test("no executable source still references the original hex prototype", () => {
    const errors = [];
    let checked = 0;
    for (const filename of trackedRuntimeSources(sourceRoot)) {
        const relative = path.relative(sourceRoot, filename).replaceAll(path.sep, "/");
        if (archivedConverters.has(relative)) continue;
        const source = fs.readFileSync(filename, "utf8");
        checked++;
        for (const { symbol, pattern } of retiredPatterns) {
            for (const match of source.matchAll(pattern)) {
                const line = source.slice(0, match.index).split("\n").length;
                errors.push(`${relative}:${line}: retired identifier ${symbol}`);
            }
        }
    }
    assert.ok(checked > 150, "Expected to scan all domain, application, infrastructure and web sources");
    assert.deepEqual(errors, [], errors.join("\n"));
});

test("archived axial conversion cannot be used by the runtime", () => {
    const converterNames = ["ArchivedGridCutoverGeometry", "ArchivedHexGeometry", "ArchivedAxialCoordinate"];
    const violations = [];
    for (const filename of trackedRuntimeSources(sourceRoot)) {
        const relative = path.relative(sourceRoot, filename).replaceAll(path.sep, "/");
        if (archivedConverters.has(relative)) continue;
        const source = fs.readFileSync(filename, "utf8");
        for (const name of converterNames) {
            if (source.includes(name)) violations.push(`${relative}: ${name}`);
        }
    }
    assert.deepEqual(violations, []);
});
