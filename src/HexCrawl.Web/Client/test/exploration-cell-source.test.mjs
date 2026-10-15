import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

test("Spatial exploration sites use one generic, server-authoritative journey contract", () => {
    const ui = fs.readFileSync(path.resolve("src/modules/expeditions/journey-panel.ts"), "utf8");
    const api = fs.readFileSync(path.resolve("src/journey-api.ts"), "utf8");
    const contract = fs.readFileSync(path.resolve("../../../src/HexCrawl.Web/Modules/Expeditions/JourneyContracts.cs"), "utf8");
    assert.match(ui, /this\.explorationCellView\(state\)/);
    assert.match(ui, /cell\.availableActions/);
    assert.match(ui, /recordExplorationCell\(this\.expeditionId/);
    assert.match(ui, /GM location details remain private/);
    assert.doesNotMatch(ui, /setExplorationTable|explorationSavedTables|ExplorationTableRoller/);
    assert.match(api, /journeys\/exploration-cell/);
    assert.match(contract, /ExplorationCellContextContract/);
    assert.doesNotMatch(ui, /fallout-wasteland|wasteland-warfare/i);
});
