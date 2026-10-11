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
    assert.match(panel, /const inGeneralizedCell = runtime\.expedition\.isSpatial && !!runtime\.expedition\.currentCell/);
    assert.match(panel, /if \(inGeneralizedCell\) scope\.value = "Cell"/);
    assert.match(panel, /cell: selectedScope === "Cell" \? currentCell : null/);
    assert.match(panel, /selectedScope === "Cell" && !currentCell/);
});

test("keyed encounter UI supplies a saved in-cell location and retains the backend contract", () => {
    const periodic = source("modules","expeditions","periodic-expedition-workbench.ts");
    assert.match(periodic, /name="keyedLocation"/);
    assert.match(periodic, /world\.locations\.filter\(location =>[\s\S]*?pointInPolygon\(location\.position, topology\.polygon\)/);
    assert.match(periodic, /encounterLocationId\([\s\S]*?encounterChoice, keyedChoices\.value, keyedLocations\)/);
    assert.match(periodic, /locationId: encounterLocationId/);
    assert.match(periodic, /api\.resolveCellEncounter/);
    assert.match(periodic, /cadence === "None"/);
    assert.match(periodic, /encounterControl\.disabled = busy/);
    assert.match(periodic, /data-encounter-cadence/);
});
