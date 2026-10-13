import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";

const source = (...pieces) => fs.readFileSync(path.join("src", ...pieces), "utf8");

test("generalized expedition keeps the existing route, context and shared gameplay editors", () => {
    const legacy = source("modules","expeditions","expedition-view.ts");
    const periodic = source("modules","expeditions","periodic-expedition-workbench.ts");
    const panels = source("modules","expeditions","periodic-gameplay-panels.ts");

    assert.match(legacy, /renderPeriodicExpeditionWorkbench\(root, api, runtime, world, navigate, toolContext\)/);
    assert.match(periodic, /mountPeriodicGameplayPanels\(/);
    assert.match(periodic, /\[data-gameplay-panels\]/);
    assert.match(periodic, /gameplay\.sync\(\)/);
    assert.match(periodic, /gameplay\.dispose\(\)/);
    assert.match(periodic, /publishExpeditionRuntimeChanged\(root, runtime\)/);
    for (const className of [
        "ExpeditionPartySheetController", "ExpeditionEnvironmentPanel",
        "ExpeditionJourneyPanel", "ExpeditionEffectsPanel",
        "ExpeditionSurvivalResourcesPanel"
    ]) {
        assert.match(panels, new RegExp(`new ${className}\\(`));
    }
    assert.match(panels, /new JourneyApi\(toolContext\)/);
    assert.match(panels, /new ExpeditionEffectsApi\(toolContext\)/);
    assert.match(panels, /new SurvivalResourcesApi\(toolContext\)/);
    assert.match(panels, /party\.sync\(getRuntime\(\)\)/);
    assert.match(panels, /environment\.sync\(\)/);
    assert.match(panels, /journey\.sync\(\), effects\.sync\(\), survival\.sync\(\)/);
});

test("generalized environment fact editing uses qualified Cell rather than fake axial hex", () => {
    const panel = source("modules","expeditions","environment-panel.ts");
    assert.match(panel, /const inCell = runtime\.expedition\.isSpatial && !!runtime\.expedition\.currentCell/);
    assert.match(panel, /if \(inCell\) scope\.value = "Cell"/);
    assert.doesNotMatch(panel, /<option value="Hex">/);
    assert.match(panel, /cell: selectedScope === "Cell" \? currentCell \?\? null : null/);
    assert.doesNotMatch(panel, /hex: null|annotation\.scope\.hex/);
    assert.match(panel, /selectedScope === "Cell" && !currentCell/);
});

test("keyed encounter UI offers saved destination locations without pre-watch filtering", () => {
    const periodic = source("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(periodic, /name="keyedLocation"/);
    assert.match(periodic, /for \(const location of world\?\.locations \?\? \[\]\)/);
    assert.doesNotMatch(periodic, /world\.locations\.filter\(location =>/);
    assert.match(periodic, /encounterLocationId\([\s\S]*?encounterChoice, keyedChoices\.value, world\?\.locations \?\? \[\]\)/);
    assert.match(periodic, /the actual encounter trigger/);
    assert.match(periodic, /locationId: encounterLocationId/);
    assert.match(periodic, /api\.resolveCellEncounter/);
    assert.match(periodic, /cadence === "None"/);
    assert.match(periodic, /encounterControl\.disabled = busy/);
    assert.match(periodic, /data-encounter-cadence/);
});
