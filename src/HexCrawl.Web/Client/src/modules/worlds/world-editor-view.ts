import { HexCrawlApiError, type HexCrawlApi } from "../../api";
import type { EnvironmentAnnotation, EnvironmentFact, WorldEnvironment } from "../../environment-types";
import { hexToWorld, worldToHex } from "../../hex-math";
import { MapSurface } from "../../map-surface";
import { SourceMapWorkspace } from "./source-map-workspace";
import type { HexCoordinate, Location, Overworld, SpatialFeature, WorldPoint } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { customUnitFieldsVisible, gridWithSelectedUnit } from "./world-form";
import type { DistanceUnitKind } from "./world-form";
import { input, integer, numeric, required, select } from "../../ui/dom";
import {
    addEnvironmentFact,
    addHexTagFact,
    applicableFeatureEnvironmentFacts,
    commonEnvironmentDimensions,
    featureHasEnvironmentRules,
    featureIntersectsCell,
    featuresIntersectingCell,
    hexEnvironmentFacts,
    locationsInCell,
    measurementFact,
    removeEnvironmentFact,
    replaceFeatureTagFact,
    replaceHexTerrain,
    sameHex,
    tagFact,
    terrainFactsForCell
} from "./selected-cell-authoring";

export async function renderWorldEditor(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    const [initialWorld, initialWorldEnvironment, procedurePresets, initialExpeditions] = await Promise.all([
        api.getOverworld(worldId),
        api.getWorldEnvironment(worldId),
        api.getProcedurePresets(),
        api.listExpeditions(worldId)
    ]);
    let world = initialWorld;
    let worldEnvironment = initialWorldEnvironment;
    let selectedCell = restoreSelectedCell(worldId);
    let selectedLocation: Location | null = null;
    let selectedFeature: SpatialFeature | null = null;
    let placement: "location" | "point" | "line" | "region" | "cell-location" | "cell-route" | null = null;
    let draft: WorldPoint[] = [];
    let cellLocationPoint: WorldPoint | null = null;
    let cellRouteDraft: WorldPoint[] = [];
    let disposed = false;
    let sourceMapWorkspace: SourceMapWorkspace | null = null;

    root.innerHTML = `
        <section class="hc-page hc-workspace hc-world-editor">
            <header class="hc-page-header">
                <div><span class="hc-sheet-kicker">Map preparation</span><h1 data-title></h1><p>World map editor · source maps, locations, and features.</p></div>
                <nav><button type="button" data-worlds>All overworlds</button><button type="button" data-reset-view>Reset map view</button></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-workspace-grid">
                <section class="hc-map-panel" aria-label="Overworld map">
                    <div class="hc-map-host" data-map></div>
                    <p class="hc-hint" data-map-hint>Click to select or author map content; drag to pan; wheel to zoom. The focused map also supports keyboard pan, zoom, and center-point selection.</p>
                </section>
                <aside class="hc-sidebar" aria-label="Overworld authoring controls">
                    <details open class="hc-selected-cell-panel" data-selected-cell-panel><summary>Selected cell</summary>
                        <div class="hc-form" data-selected-cell-empty>
                            <p class="hc-hint">Select a map cell to edit terrain, locations, routes, features, and explicit environment rules in tabletop terms.</p>
                        </div>
                        <div class="hc-stack" data-selected-cell-content hidden>
                            <div>
                                <h3 data-selected-cell-title></h3>
                                <p class="hc-hint">This is authoring selection only. It does not move an expedition or reveal hidden content to players.</p>
                            </div>
                            <section class="hc-stack">
                                <h4>Terrain / biome</h4>
                                <label>Terrain / biome
                                    <input data-cell-terrain list="hc-terrain-values" placeholder="Forest, swamp, custom…">
                                    <datalist id="hc-terrain-values"><option value="forest"><option value="swamp"><option value="grassland"><option value="desert"><option value="mountain"><option value="hills"><option value="tundra"><option value="jungle"><option value="coast"><option value="urban"></datalist>
                                </label>
                                <p class="hc-hint" data-cell-terrain-status></p>
                                <div class="hc-button-row"><button type="button" class="hc-primary-action" data-save-cell-terrain>Save terrain</button><button type="button" data-clear-cell-terrain>None / unspecified</button></div>
                            </section>
                            <section class="hc-stack">
                                <h4>Locations / POIs</h4>
                                <div data-cell-location-list></div>
                                <form class="hc-form" data-cell-location-form>
                                    <label>Name <input name="name" required></label>
                                    <label>Category <input name="category" required></label>
                                    <label>Discoverability <select name="discoverability"><option>Obvious</option><option>Hidden</option><option>Conditional</option></select></label>
                                    <p class="hc-hint" data-cell-location-position>Position: cell center.</p>
                                    <div class="hc-button-row"><button type="button" data-cell-location-exact>Choose exact position</button><button type="submit" class="hc-primary-action">Add location here</button></div>
                                </form>
                            </section>
                            <section class="hc-stack">
                                <h4>Routes & features</h4>
                                <div data-cell-feature-list></div>
                                <form class="hc-form" data-cell-route-form>
                                    <label>Name <input name="name" required></label>
                                    <label>Category <input name="category" list="hc-cell-feature-categories" required></label>
                                    <datalist id="hc-cell-feature-categories"><option value="road"><option value="trail"><option value="river"><option value="border"><option value="custom"></datalist>
                                    <p class="hc-hint" data-cell-route-draft>Draw a continuous line on the map; it may cross any number of cells.</p>
                                    <div class="hc-button-row"><button type="button" data-cell-route-road>Road</button><button type="button" data-cell-route-trail>Trail</button><button type="button" data-cell-route-river>River</button><button type="button" data-cell-route-draw>Draw route</button><button type="submit" class="hc-primary-action">Save line feature</button></div>
                                </form>
                            </section>
                            <section class="hc-stack">
                                <h4>Environment / mechanics</h4>
                                <div data-cell-environment-list></div>
                                <form class="hc-form" data-cell-environment-form>
                                    <label>Environmental detail <input name="dimension" list="hc-cell-environment-dimensions" required></label>
                                    <datalist id="hc-cell-environment-dimensions"></datalist>
                                    <label>Value <input name="value" required></label>
                                    <button type="submit">Add to this cell</button>
                                </form>
                                <form class="hc-form" data-cell-feature-environment-form>
                                    <strong>Feature behavior</strong>
                                    <label>Feature <select name="featureId" required></select></label>
                                    <label>Dimension <input name="dimension" list="hc-cell-environment-dimensions" required value="route"></label>
                                    <label>Value <input name="value" required placeholder="good-road"></label>
                                    <button type="submit">Set feature behavior</button>
                                </form>
                            </section>
                            <details data-cell-advanced><summary>Advanced</summary>
                                <div class="hc-form">
                                    <div data-cell-advanced-summary></div>
                                    <form class="hc-form" data-cell-advanced-environment-form>
                                        <label>Exact scope <select name="scope"><option value="Hex">This cell</option><option value="SpatialFeature">Spatial feature</option><option value="World">World</option></select></label>
                                        <label data-advanced-feature-row hidden>Feature <select name="featureId"></select></label>
                                        <label>Dimension <input name="dimension" list="hc-cell-environment-dimensions" required></label>
                                        <label>Value type <select name="valueKind"><option value="Tag">Tag/value</option><option value="Measurement">Measurement</option></select></label>
                                        <label data-advanced-tag-row>Value <input name="tag"></label>
                                        <div data-advanced-measurement-row hidden class="hc-inline"><label>Numeric value <input name="measurement" type="number" step="any"></label><label>Unit <input name="unit"></label></div>
                                        <label>Provenance <input name="provenance"></label>
                                        <label>Note <input name="note"></label>
                                        <button type="submit">Add exact environment fact</button>
                                    </form>
                                </div>
                            </details>
                        </div>
                    </details>

                    <details><summary>World and grid</summary><form class="hc-form" data-grid-form>
                        <label>Name <input name="name" required></label>
                        <label>Orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                        <label>Hex center distance <input name="scale" type="number" min="0.001" step="any" required><span class="hc-hint">Center-to-center distance between adjacent hexes.</span></label>
                        <label>Unit <select name="unitKind"><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                        <div class="hc-custom-unit-fields" data-custom-unit hidden>
                            <label>Custom symbol <input name="unitSymbol"></label>
                            <label>Custom meters per unit <input name="metersPerUnit" type="number" min="0.001" step="any"></label>
                        </div>
                        <details><summary>Advanced grid alignment</summary><div class="hc-form"><p class="hc-hint">These values define the internal world-coordinate frame. Most maps can keep the existing values.</p>
                            <div class="hc-inline"><label>Origin X <input name="originX" type="number" step="any"></label><label>Origin Y <input name="originY" type="number" step="any"></label></div>
                            <label>Rotation degrees <input name="rotation" type="number" step="any"></label>
                            <label>Hex radius (world units) <input name="radius" type="number" min="0.001" step="any"></label>
                        </div></details>
                        <button type="submit" class="hc-primary-action">Save world and grid</button>
                    </form></details>

                    <details data-advanced-world-objects><summary>Advanced world objects</summary>
                    <details><summary>Locations</summary>
                        <div data-location-list></div>
                        <form class="hc-form" data-location-form>
                            <input name="id" type="hidden">
                            <label>Name <input name="name" required></label>
                            <label>Category <input name="category" required></label>
                            <label>Discoverability <select name="discoverability"><option>Obvious</option><option>Hidden</option><option>Conditional</option></select></label>
                            <div class="hc-inline"><label>Map X <input name="x" type="number" step="any" required></label><label>Map Y <input name="y" type="number" step="any" required></label></div>
                            <p class="hc-hint">For normal authoring, choose the position on the map. Map X/Y remain available for precise or imported coordinates.</p>
                            <div class="hc-button-row"><button type="button" data-place-location>Choose position on map</button><button type="submit" class="hc-primary-action">Save location</button><button type="button" data-new-location>New</button><button type="button" class="hc-danger-action" data-delete-location>Delete</button></div>
                        </form>
                    </details>

                    <details><summary>Spatial features</summary>
                        <datalist id="hc-feature-categories"><option value="forest"><option value="swamp"><option value="mountain"><option value="grassland"><option value="desert"><option value="road"><option value="trail"><option value="river"><option value="border"></datalist>
                        <div data-feature-list></div>
                        <form class="hc-form" data-feature-form>
                            <input name="id" type="hidden">
                            <label>Name <input name="name" required></label>
                            <label>Category <input name="category" list="hc-feature-categories" required></label>
                            <label>Kind <select name="kind"><option value="Point">Point</option><option value="Line">Line/polyline</option><option value="Region">Region/polygon</option></select></label>
                            <div class="hc-inline" data-point-fields><label>X <input name="x" type="number" step="any"></label><label>Y <input name="y" type="number" step="any"></label></div>
                            <p class="hc-hint" data-draft>Geometry: none.</p>
                            <div class="hc-button-row"><button type="button" data-author-geometry>Author geometry on map</button><button type="button" data-clear-geometry>Clear geometry</button><button type="submit" class="hc-primary-action">Save feature</button><button type="button" data-new-feature>New</button><button type="button" class="hc-danger-action" data-delete-feature>Delete</button></div>
                        </form>
                    </details>

                    </details>
                    <details><summary>Expeditions using this world</summary><div data-expedition-list></div>
                        <form class="hc-form" data-expedition-form>
                            <label>Name <input name="name" required value="Expedition"></label>
                            <label>Procedure <select name="procedure"></select></label>
                            <div class="hc-inline"><label>Start hex q <input name="q" type="number" step="1" value="0"></label><label>Start hex r <input name="r" type="number" step="1" value="0"></label></div>
                            <p class="hc-hint">Advanced: q/r are axial hex coordinates and remain the persisted coordinate format.</p>
                            <button type="submit" class="hc-primary-action">Start expedition</button>
                        </form>
                    </details>
                    <details><summary>Source-map metadata</summary><p data-source-maps></p></details>
                </aside>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const title = required<HTMLElement>(root, "[data-title]");
    const mapHint = required<HTMLElement>(root, "[data-map-hint]");
    const mapSurface = new MapSurface(required(root, "[data-map]"), () => world, point => onMapClick(point));
    const gridForm = required<HTMLFormElement>(root, "[data-grid-form]");
    const locationForm = required<HTMLFormElement>(root, "[data-location-form]");
    const featureForm = required<HTMLFormElement>(root, "[data-feature-form]");
    const expeditionForm = required<HTMLFormElement>(root, "[data-expedition-form]");
    const draftLabel = required<HTMLElement>(root, "[data-draft]");
    const unitKindSelect = select(gridForm, "unitKind");
    const customUnitFields = required<HTMLElement>(gridForm, "[data-custom-unit]");
    const unitSymbolInput = input(gridForm, "unitSymbol");
    const metersPerUnitInput = input(gridForm, "metersPerUnit");
    const selectedCellEmpty = required<HTMLElement>(root, "[data-selected-cell-empty]");
    const selectedCellContent = required<HTMLElement>(root, "[data-selected-cell-content]");
    const selectedCellTitle = required<HTMLElement>(root, "[data-selected-cell-title]");
    const cellTerrain = required<HTMLInputElement>(root, "[data-cell-terrain]");
    const cellTerrainStatus = required<HTMLElement>(root, "[data-cell-terrain-status]");
    const cellLocationForm = required<HTMLFormElement>(root, "[data-cell-location-form]");
    const cellLocationPosition = required<HTMLElement>(root, "[data-cell-location-position]");
    const cellRouteForm = required<HTMLFormElement>(root, "[data-cell-route-form]");
    const cellRouteDraftLabel = required<HTMLElement>(root, "[data-cell-route-draft]");
    const cellEnvironmentForm = required<HTMLFormElement>(root, "[data-cell-environment-form]");
    const cellFeatureEnvironmentForm = required<HTMLFormElement>(root, "[data-cell-feature-environment-form]");
    const cellAdvancedEnvironmentForm = required<HTMLFormElement>(root, "[data-cell-advanced-environment-form]");
    const advancedWorldObjects = required<HTMLDetailsElement>(root, "[data-advanced-world-objects]");
    const environmentDimensions = required<HTMLDataListElement>(root, "#hc-cell-environment-dimensions");
    for (const dimension of commonEnvironmentDimensions) {
        const option = document.createElement("option");
        option.value = dimension;
        environmentDimensions.append(option);
    }

    const run = async (form: HTMLFormElement | null, action: () => Promise<void>): Promise<void> => {
        if (form?.dataset.pending === "true") return;
        clearUiError(error);
        if (form) setFormPending(form, true);
        try {
            await action();
        } catch (value) {
            if (!disposed && value instanceof HexCrawlApiError && value.kind === "conflict") {
                try {
                    const [freshWorld, freshEnvironment] = await Promise.all([
                        api.getOverworld(world.id),
                        api.getWorldEnvironment(world.id)
                    ]);
                    worldEnvironment = freshEnvironment;
                    applyWorld(freshWorld);
                    showUiError(error, new Error(`${value.message} Latest world state was reloaded; review the current values and retry.`));
                } catch {
                    showUiError(error, value);
                }
            } else if (!disposed) {
                showUiError(error, value);
            }
        } finally {
            if (form && !disposed) setFormPending(form, false);
        }
    };

    const syncCustomUnit = (): void => {
        const visible = customUnitFieldsVisible(unitKindSelect.value as DistanceUnitKind);
        customUnitFields.hidden = !visible;
        unitSymbolInput.required = visible;
        metersPerUnitInput.required = visible;
    };
    unitKindSelect.addEventListener("change", syncCustomUnit);

    const applyWorld = (next: Overworld): void => {
        world = next;
        if (worldEnvironment.version !== next.version) {
            worldEnvironment = { ...worldEnvironment, version: next.version };
        }
        title.textContent = next.name;
        fillGrid();
        renderLocations();
        renderFeatures();
        renderSelectedCell();
        mapSurface.requestRender();
    };

    const applyWorldEnvironment = (next: WorldEnvironment): void => {
        worldEnvironment = next;
        if (world.version !== next.version) world = { ...world, version: next.version };
        renderSelectedCell();
    };

    const saveWorldEnvironment = async (annotations: EnvironmentAnnotation[]): Promise<void> => {
        applyWorldEnvironment(await api.replaceWorldEnvironment(world.id, {
            expectedVersion: worldEnvironment.version,
            annotations
        }));
    };

    const fillGrid = (): void => {
        input(gridForm, "name").value = world.name;
        select(gridForm, "orientation").value = world.grid.orientation;
        input(gridForm, "scale").value = String(world.grid.neighborCenterDistance.value);
        unitKindSelect.value = world.grid.neighborCenterDistance.unit.kind;
        if (world.grid.neighborCenterDistance.unit.kind === "Custom") {
            unitSymbolInput.value = world.grid.neighborCenterDistance.unit.symbol;
            metersPerUnitInput.value = world.grid.neighborCenterDistance.unit.metersPerUnit?.toString() ?? "";
        }
        input(gridForm, "originX").value = String(world.grid.origin.x);
        input(gridForm, "originY").value = String(world.grid.origin.y);
        input(gridForm, "rotation").value = String(world.grid.rotationDegrees);
        input(gridForm, "radius").value = String(world.grid.hexRadiusWorldUnits);
        syncCustomUnit();
    };

    const renderLocations = (): void => {
        const host = required<HTMLElement>(root, "[data-location-list]");
        host.replaceChildren();
        if (world.locations.length === 0) {
            host.append(emptyState(
                "No locations yet.",
                "Enter a location below or choose its position on the map, then save it."));
            return;
        }
        for (const location of world.locations) host.append(resourceButton(`${location.name} · ${location.category}`, () => loadLocation(location)));
    };

    const renderFeatures = (): void => {
        const host = required<HTMLElement>(root, "[data-feature-list]");
        host.replaceChildren();
        if (world.features.length === 0) {
            host.append(emptyState(
                "No map features yet.",
                "Add a point, route, river, border, or region below, then author its geometry on the map."));
            return;
        }
        for (const feature of world.features) host.append(resourceButton(`${feature.name} · ${feature.kind} · ${feature.category}`, () => loadFeature(feature)));
    };

    const loadLocation = (location: Location): void => {
        selectedLocation = location;
        input(locationForm, "id").value = location.id;
        input(locationForm, "name").value = location.name;
        input(locationForm, "category").value = location.category;
        select(locationForm, "discoverability").value = location.discoverability;
        input(locationForm, "x").value = String(location.position.x);
        input(locationForm, "y").value = String(location.position.y);
    };

    const newLocation = (): void => {
        selectedLocation = null;
        locationForm.reset();
        input(locationForm, "id").value = "";
        input(locationForm, "x").value = "0";
        input(locationForm, "y").value = "0";
    };

    const loadFeature = (feature: SpatialFeature): void => {
        selectedFeature = feature;
        input(featureForm, "id").value = feature.id;
        input(featureForm, "name").value = feature.name;
        input(featureForm, "category").value = feature.category;
        select(featureForm, "kind").value = feature.kind;
        if (feature.position) {
            input(featureForm, "x").value = String(feature.position.x);
            input(featureForm, "y").value = String(feature.position.y);
        }
        draft = feature.kind === "Line" ? [...(feature.path ?? [])] : feature.kind === "Region" ? [...(feature.boundary ?? [])] : [];
        updatePointFieldVisibility();
        updateDraftLabel();
    };

    const newFeature = (): void => {
        selectedFeature = null;
        featureForm.reset();
        input(featureForm, "id").value = "";
        draft = [];
        updatePointFieldVisibility();
        updateDraftLabel();
    };

    const renderSelectedCell = (): void => {
        const cell = selectedCell;
        selectedCellEmpty.hidden = cell !== null;
        selectedCellContent.hidden = cell === null;
        mapSurface.renderer.selectedHex = cell;
        if (!cell) {
            mapSurface.requestRender();
            return;
        }

        selectedCellTitle.textContent = `Hex ${cell.q},${cell.r}`;

        const terrainFacts = terrainFactsForCell(worldEnvironment, cell);
        const terrainTags = terrainFacts
            .map(item => item.fact.valueKind === "Tag" ? item.fact.tag?.trim() ?? "" : "")
            .filter(Boolean);
        cellTerrain.value = terrainFacts.length === 1 && terrainTags.length === 1 ? terrainTags[0] : "";
        cellTerrainStatus.textContent = terrainFacts.length === 0
            ? "No explicit terrain is defined for this cell."
            : terrainFacts.length === 1
                ? `Explicit Hex-scope terrain: ${formatEnvironmentFact(terrainFacts[0].fact)}.`
                : "Multiple explicit terrain facts exist for this cell. Saving Terrain / biome will deliberately replace them with one Hex-scope terrain fact.";

        const cellLocations = locationsInCell(world, cell);
        const locationHost = required<HTMLElement>(root, "[data-cell-location-list]");
        locationHost.replaceChildren();
        if (cellLocations.length === 0) {
            locationHost.append(emptyState("No locations in this cell.", "Add a location here without entering map coordinates."));
        } else {
            for (const location of cellLocations) {
                const row = document.createElement("div");
                row.className = "hc-discovery-row";
                const label = document.createElement("span");
                label.textContent = `${location.name} · ${location.category} · ${location.discoverability}`;
                const edit = document.createElement("button");
                edit.type = "button";
                edit.textContent = "Edit";
                edit.addEventListener("click", () => {
                    loadLocation(location);
                    advancedWorldObjects.open = true;
                    const details = locationForm.closest("details");
                    if (details instanceof HTMLDetailsElement) details.open = true;
                    input(locationForm, "name").focus();
                });
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "Delete";
                remove.className = "hc-danger-action";
                remove.addEventListener("click", () => void run(null, async () => {
                    applyWorld(await api.deleteLocation(world.id, location.id, world.version));
                }));
                row.append(label, edit, remove);
                locationHost.append(row);
            }
        }

        const cellFeatures = featuresIntersectingCell(world, cell);
        const featureHost = required<HTMLElement>(root, "[data-cell-feature-list]");
        featureHost.replaceChildren();
        if (cellFeatures.length === 0) {
            featureHost.append(emptyState(
                "No spatial features intersect this cell.",
                "Draw a road, trail, river, border, or other line feature below."));
        } else {
            for (const feature of cellFeatures) {
                const row = document.createElement("div");
                row.className = "hc-discovery-row";
                const label = document.createElement("span");
                label.textContent = `${feature.name} · ${feature.category} · ${feature.kind}`;
                const edit = document.createElement("button");
                edit.type = "button";
                edit.textContent = "Edit";
                edit.addEventListener("click", () => {
                    loadFeature(feature);
                    advancedWorldObjects.open = true;
                    const details = featureForm.closest("details");
                    if (details instanceof HTMLDetailsElement) details.open = true;
                    input(featureForm, "name").focus();
                });
                const mechanics = document.createElement("button");
                mechanics.type = "button";
                mechanics.textContent = "Set mechanics";
                mechanics.addEventListener("click", () => {
                    select(cellFeatureEnvironmentForm, "featureId").value = feature.id;
                    input(cellFeatureEnvironmentForm, "value").focus();
                });
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "Delete";
                remove.className = "hc-danger-action";
                remove.addEventListener("click", () => void run(null, async () => {
                    if (featureHasEnvironmentRules(worldEnvironment, feature.id)) {
                        throw new Error("This feature has environment rules attached. Remove or reassign them before deleting the feature.");
                    }
                    applyWorld(await api.deleteFeature(world.id, feature.id, world.version));
                }));
                row.append(label, edit, mechanics, remove);
                featureHost.append(row);
            }
        }

        const featureChoices = [
            select(cellFeatureEnvironmentForm, "featureId"),
            select(cellAdvancedEnvironmentForm, "featureId")
        ];
        for (const choice of featureChoices) {
            const previous = choice.value;
            choice.replaceChildren();
            const empty = document.createElement("option");
            empty.value = "";
            empty.textContent = "Select intersecting feature";
            choice.append(empty);
            for (const feature of cellFeatures) {
                const option = document.createElement("option");
                option.value = feature.id;
                option.textContent = `${feature.name} · ${feature.category}`;
                choice.append(option);
            }
            if ([...choice.options].some(option => option.value === previous)) choice.value = previous;
        }

        const cellFacts = hexEnvironmentFacts(worldEnvironment, cell);
        const featureFacts = applicableFeatureEnvironmentFacts(worldEnvironment, cellFeatures);
        const environmentHost = required<HTMLElement>(root, "[data-cell-environment-list]");
        environmentHost.replaceChildren();
        const authoredFacts = [...cellFacts, ...featureFacts];
        if (authoredFacts.length === 0) {
            environmentHost.append(emptyState(
                "No explicit environment rules apply from this cell or its intersecting features.",
                "Add terrain, route behavior, visibility, hazards, or another campaign-defined fact."));
        } else {
            for (const item of authoredFacts) {
                const row = document.createElement("div");
                row.className = "hc-discovery-row";
                const label = document.createElement("span");
                label.textContent = `${item.sourceLabel} · ${humanizeEnvironmentDimension(item.fact.dimension)}: ${formatEnvironmentFact(item.fact)}`;
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "Remove";
                remove.addEventListener("click", () => void run(null, async () => {
                    await saveWorldEnvironment(removeEnvironmentFact(
                        worldEnvironment.annotations,
                        item.annotation.id,
                        item.fact.id));
                }));
                row.append(label, remove);
                environmentHost.append(row);
            }
        }

        const advancedSummary = required<HTMLElement>(root, "[data-cell-advanced-summary]");
        advancedSummary.replaceChildren();
        advancedSummary.append(
            technicalLine(`Cell coordinate: q ${cell.q}, r ${cell.r}`),
            technicalLine(`Cell center: ${formatPoint(hexToWorld(world.grid, cell))}`));
        for (const location of cellLocations) {
            advancedSummary.append(technicalLine(
                `Location ${location.id}: ${location.name} · position ${formatPoint(location.position)}`));
        }
        for (const feature of cellFeatures) {
            advancedSummary.append(technicalLine(
                `Feature ${feature.id}: ${feature.kind} · ${feature.category}`));
        }
        for (const item of authoredFacts) {
            advancedSummary.append(technicalLine(
                `Annotation ${item.annotation.id} · fact ${item.fact.id} · scope ${item.annotation.scope.kind} · ${item.fact.dimension} · ${item.fact.valueKind} · provenance ${item.fact.provenance ?? "none"} · note ${item.fact.note ?? "none"}`));
        }
    };

    const updatePointFieldVisibility = (): void => {
        required<HTMLElement>(featureForm, "[data-point-fields]").hidden = select(featureForm, "kind").value !== "Point";
    };

    const updateDraftLabel = (): void => {
        const kind = select(featureForm, "kind").value;
        draftLabel.textContent = kind === "Point"
            ? `Point: ${input(featureForm, "x").value || "?"}, ${input(featureForm, "y").value || "?"}`
            : `Geometry vertices (${draft.length}): ${draft.map(point => `${point.x.toFixed(2)},${point.y.toFixed(2)}`).join(" · ") || "none"}`;
    };

    const onMapClick = (point: WorldPoint): void => {
        if (placement === "cell-location") {
            if (!selectedCell) return;
            const clickedCell = worldToHex(world.grid, point);
            if (!sameHex(clickedCell, selectedCell)) {
                mapHint.textContent = `Choose an exact position inside Hex ${selectedCell.q},${selectedCell.r}.`;
                return;
            }
            cellLocationPoint = point;
            placement = null;
            cellLocationPosition.textContent = `Position: exact map point ${formatPoint(point)}.`;
            mapHint.textContent = "Exact location position captured. Complete the location details and save.";
            input(cellLocationForm, "name").focus();
            return;
        }
        if (placement === "cell-route") {
            cellRouteDraft.push(point);
            cellRouteDraftLabel.textContent = `Route vertices (${cellRouteDraft.length}): ${cellRouteDraft.map(formatPoint).join(" · ")}`;
            mapHint.textContent = `Route authoring: ${cellRouteDraft.length} vertices captured. Continue clicking anywhere the route travels, then save the line feature.`;
            return;
        }
        if (placement === "location") {
            input(locationForm, "x").value = String(point.x);
            input(locationForm, "y").value = String(point.y);
            placement = null;
            mapHint.textContent = "Location position captured. Save the location to persist it.";
            return;
        }
        if (placement === "point") {
            input(featureForm, "x").value = String(point.x);
            input(featureForm, "y").value = String(point.y);
            placement = null;
            updateDraftLabel();
            mapHint.textContent = "Point position captured. Save the feature to persist it.";
            return;
        }
        if (placement === "line" || placement === "region") {
            draft.push(point);
            updateDraftLabel();
            mapHint.textContent = `${placement === "line" ? "Polyline" : "Polygon"} authoring: ${draft.length} vertices captured. Continue clicking or save the feature.`;
        }
    };

    mapSurface.setHexSelectionHandler(hex => {
        if (placement !== null) {
            mapSurface.renderer.selectedHex = selectedCell;
            mapSurface.requestRender();
            return;
        }
        selectedCell = hex;
        cellLocationPoint = null;
        cellRouteDraft = [];
        cellLocationPosition.textContent = "Position: cell center.";
        cellRouteDraftLabel.textContent = "Draw a continuous line on the map; it may cross any number of cells.";
        persistSelectedCell(worldId, selectedCell);
        renderSelectedCell();
        if (hex) mapHint.textContent = `Selected Hex ${hex.q},${hex.r} for world authoring.`;
        else mapHint.textContent = "Select a map cell to author its contents.";
    });
    if (selectedCell) mapSurface.renderer.selectedHex = selectedCell;

    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    required<HTMLButtonElement>(root, "[data-reset-view]").addEventListener("click", () => mapSurface.resetView());

    required<HTMLButtonElement>(root, "[data-save-cell-terrain]").addEventListener("click", () => void run(null, async () => {
        if (!selectedCell) throw new Error("Select a map cell before setting terrain.");
        await saveWorldEnvironment(replaceHexTerrain(
            worldEnvironment.annotations,
            selectedCell,
            cellTerrain.value));
    }));
    required<HTMLButtonElement>(root, "[data-clear-cell-terrain]").addEventListener("click", () => void run(null, async () => {
        if (!selectedCell) throw new Error("Select a map cell before clearing terrain.");
        await saveWorldEnvironment(replaceHexTerrain(
            worldEnvironment.annotations,
            selectedCell,
            null));
    }));

    required<HTMLButtonElement>(root, "[data-cell-location-exact]").addEventListener("click", () => {
        if (!selectedCell) {
            showUiError(error, new Error("Select a map cell before choosing a location position."));
            return;
        }
        placement = "cell-location";
        cellLocationPoint = null;
        cellLocationPosition.textContent = `Position: choose an exact point inside Hex ${selectedCell.q},${selectedCell.r}.`;
        mapHint.textContent = `Click an exact position inside Hex ${selectedCell.q},${selectedCell.r}.`;
        mapSurface.canvas.focus();
    });
    cellLocationForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(cellLocationForm, async () => {
            if (!selectedCell) throw new Error("Select a map cell before adding a location.");
            const position = cellLocationPoint ?? hexToWorld(world.grid, selectedCell);
            applyWorld(await api.createLocation(world.id, {
                name: input(cellLocationForm, "name").value.trim(),
                category: input(cellLocationForm, "category").value.trim(),
                position,
                discoverability: select(cellLocationForm, "discoverability").value as "Obvious" | "Hidden" | "Conditional",
                expectedVersion: world.version
            }));
            cellLocationForm.reset();
            cellLocationPoint = null;
            cellLocationPosition.textContent = "Position: cell center.";
        });
    });

    const setCellRouteCategory = (category: string): void => {
        input(cellRouteForm, "category").value = category;
        input(cellRouteForm, "name").focus();
    };
    required<HTMLButtonElement>(root, "[data-cell-route-road]").addEventListener("click", () => setCellRouteCategory("road"));
    required<HTMLButtonElement>(root, "[data-cell-route-trail]").addEventListener("click", () => setCellRouteCategory("trail"));
    required<HTMLButtonElement>(root, "[data-cell-route-river]").addEventListener("click", () => setCellRouteCategory("river"));
    required<HTMLButtonElement>(root, "[data-cell-route-draw]").addEventListener("click", () => {
        if (!selectedCell) {
            showUiError(error, new Error("Select a map cell before drawing a route."));
            return;
        }
        cellRouteDraft = [];
        placement = "cell-route";
        cellRouteDraftLabel.textContent = "Route vertices (0). Click the map to draw the continuous route.";
        mapHint.textContent = "Click the map to add continuous route vertices. The route may cross any number of cells.";
        mapSurface.canvas.focus();
    });
    cellRouteForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(cellRouteForm, async () => {
            if (!selectedCell) throw new Error("Select a map cell before adding a route.");
            if (cellRouteDraft.length < 2) throw new Error("Draw at least two route points before saving the line feature.");
            const feature: SpatialFeature = {
                id: crypto.randomUUID(),
                name: input(cellRouteForm, "name").value.trim(),
                category: input(cellRouteForm, "category").value.trim(),
                kind: "Line",
                position: null,
                path: [...cellRouteDraft],
                boundary: null
            };
            if (!featureIntersectsCell(world, selectedCell, feature)) {
                throw new Error(`The route does not intersect selected Hex ${selectedCell.q},${selectedCell.r}. Draw the route through the selected cell or select the cell it belongs to.`);
            }
            applyWorld(await api.createFeature(world.id, {
                name: feature.name,
                category: feature.category,
                kind: feature.kind,
                position: feature.position,
                path: feature.path,
                boundary: feature.boundary,
                expectedVersion: world.version
            }));
            cellRouteForm.reset();
            cellRouteDraft = [];
            placement = null;
            cellRouteDraftLabel.textContent = "Draw a continuous line on the map; it may cross any number of cells.";
            mapHint.textContent = `Route saved. Hex ${selectedCell.q},${selectedCell.r} remains selected for authoring.`;
        });
    });

    cellEnvironmentForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(cellEnvironmentForm, async () => {
            if (!selectedCell) throw new Error("Select a map cell before adding environment details.");
            const dimension = input(cellEnvironmentForm, "dimension").value.trim();
            const value = input(cellEnvironmentForm, "value").value.trim();
            const next = dimension.toLowerCase() === "terrain"
                ? replaceHexTerrain(worldEnvironment.annotations, selectedCell, value)
                : addHexTagFact(worldEnvironment.annotations, selectedCell, dimension, value);
            await saveWorldEnvironment(next);
            cellEnvironmentForm.reset();
        });
    });

    cellFeatureEnvironmentForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(cellFeatureEnvironmentForm, async () => {
            const featureId = select(cellFeatureEnvironmentForm, "featureId").value;
            if (!featureId) throw new Error("Select an intersecting feature before setting feature behavior.");
            const dimension = input(cellFeatureEnvironmentForm, "dimension").value.trim();
            const value = input(cellFeatureEnvironmentForm, "value").value.trim();
            await saveWorldEnvironment(replaceFeatureTagFact(
                worldEnvironment.annotations,
                featureId,
                dimension,
                value));
        });
    });

    const advancedScope = select(cellAdvancedEnvironmentForm, "scope");
    const advancedFeatureRow = required<HTMLElement>(cellAdvancedEnvironmentForm, "[data-advanced-feature-row]");
    const advancedValueKind = select(cellAdvancedEnvironmentForm, "valueKind");
    const advancedTagRow = required<HTMLElement>(cellAdvancedEnvironmentForm, "[data-advanced-tag-row]");
    const advancedMeasurementRow = required<HTMLElement>(cellAdvancedEnvironmentForm, "[data-advanced-measurement-row]");
    const syncAdvancedEnvironmentForm = (): void => {
        advancedFeatureRow.hidden = advancedScope.value !== "SpatialFeature";
        const measurement = advancedValueKind.value === "Measurement";
        advancedTagRow.hidden = measurement;
        advancedMeasurementRow.hidden = !measurement;
        input(cellAdvancedEnvironmentForm, "tag").required = !measurement;
        input(cellAdvancedEnvironmentForm, "measurement").required = measurement;
        input(cellAdvancedEnvironmentForm, "unit").required = measurement;
    };
    advancedScope.addEventListener("change", syncAdvancedEnvironmentForm);
    advancedValueKind.addEventListener("change", syncAdvancedEnvironmentForm);
    syncAdvancedEnvironmentForm();

    cellAdvancedEnvironmentForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(cellAdvancedEnvironmentForm, async () => {
            if (!selectedCell) throw new Error("Select a map cell before adding an environment fact.");
            const scopeKind = advancedScope.value as "World" | "Hex" | "SpatialFeature";
            const featureId = select(cellAdvancedEnvironmentForm, "featureId").value;
            if (scopeKind === "SpatialFeature" && !featureId) {
                throw new Error("Select an intersecting feature for SpatialFeature scope.");
            }
            const provenance = input(cellAdvancedEnvironmentForm, "provenance").value.trim() || null;
            const note = input(cellAdvancedEnvironmentForm, "note").value.trim() || null;
            const dimension = input(cellAdvancedEnvironmentForm, "dimension").value.trim();
            const fact: EnvironmentFact = advancedValueKind.value === "Measurement"
                ? measurementFact(
                    dimension,
                    numeric(input(cellAdvancedEnvironmentForm, "measurement")),
                    input(cellAdvancedEnvironmentForm, "unit").value,
                    provenance,
                    note)
                : tagFact(
                    dimension,
                    input(cellAdvancedEnvironmentForm, "tag").value,
                    provenance,
                    note);
            const scope: EnvironmentAnnotation["scope"] = scopeKind === "World"
                ? { kind: "World", hex: null, featureId: null }
                : scopeKind === "Hex"
                    ? { kind: "Hex", hex: selectedCell, featureId: null }
                    : { kind: "SpatialFeature", hex: null, featureId };
            const next = scopeKind === "Hex" && dimension.toLowerCase() === "terrain" && fact.valueKind === "Tag"
                ? replaceHexTerrain(
                    worldEnvironment.annotations,
                    selectedCell,
                    fact.tag,
                    fact.provenance,
                    fact.note)
                : addEnvironmentFact(worldEnvironment.annotations, scope, fact);
            await saveWorldEnvironment(next);
        });
    });

    required<HTMLButtonElement>(root, "[data-place-location]").addEventListener("click", () => {
        placement = "location";
        mapHint.textContent = "Click the map to position the location.";
    });
    required<HTMLButtonElement>(root, "[data-new-location]").addEventListener("click", newLocation);
    required<HTMLButtonElement>(root, "[data-new-feature]").addEventListener("click", newFeature);
    required<HTMLButtonElement>(root, "[data-author-geometry]").addEventListener("click", () => {
        const kind = select(featureForm, "kind").value;
        placement = kind === "Point" ? "point" : kind === "Line" ? "line" : "region";
        if (kind !== "Point") draft = [];
        updateDraftLabel();
        mapHint.textContent = kind === "Point" ? "Click the map for the point." : `Click the map to add ${kind === "Line" ? "polyline" : "polygon"} vertices.`;
    });
    required<HTMLButtonElement>(root, "[data-clear-geometry]").addEventListener("click", () => {
        draft = [];
        updateDraftLabel();
    });
    select(featureForm, "kind").addEventListener("change", () => {
        draft = [];
        updatePointFieldVisibility();
        updateDraftLabel();
    });

    gridForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(gridForm, async () => {
            const kind = unitKindSelect.value as DistanceUnitKind;
            const centerDistance = numeric(input(gridForm, "scale"));
            let nextGrid = gridWithSelectedUnit(
                world.grid,
                kind,
                centerDistance,
                unitSymbolInput.value,
                kind === "Custom" ? numeric(metersPerUnitInput) : null);
            nextGrid = {
                ...nextGrid,
                orientation: select(gridForm, "orientation").value === "FlatTop" ? "FlatTop" : "PointyTop",
                origin: { x: numeric(input(gridForm, "originX")), y: numeric(input(gridForm, "originY")) },
                rotationDegrees: numeric(input(gridForm, "rotation")),
                hexRadiusWorldUnits: numeric(input(gridForm, "radius"))
            };
            applyWorld(await api.updateOverworld(world.id, input(gridForm, "name").value.trim(), nextGrid, world.version));
        });
    });

    locationForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(locationForm, async () => {
            const payload = {
                name: input(locationForm, "name").value,
                category: input(locationForm, "category").value,
                position: { x: numeric(input(locationForm, "x")), y: numeric(input(locationForm, "y")) },
                discoverability: select(locationForm, "discoverability").value as "Obvious" | "Hidden" | "Conditional",
                expectedVersion: world.version
            };
            applyWorld(selectedLocation
                ? await api.updateLocation(world.id, selectedLocation.id, payload)
                : await api.createLocation(world.id, payload));
            newLocation();
        });
    });

    required<HTMLButtonElement>(root, "[data-delete-location]").addEventListener("click", () => void run(null, async () => {
        if (!selectedLocation) throw new Error("Select a location to delete.");
        applyWorld(await api.deleteLocation(world.id, selectedLocation.id, world.version));
        newLocation();
    }));

    featureForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(featureForm, async () => {
            const kind = select(featureForm, "kind").value as "Point" | "Line" | "Region";
            const payload = {
                name: input(featureForm, "name").value,
                category: input(featureForm, "category").value,
                kind,
                position: kind === "Point" ? { x: numeric(input(featureForm, "x")), y: numeric(input(featureForm, "y")) } : null,
                path: kind === "Line" ? [...draft] : null,
                boundary: kind === "Region" ? [...draft] : null,
                expectedVersion: world.version
            };
            applyWorld(selectedFeature
                ? await api.updateFeature(world.id, selectedFeature.id, payload)
                : await api.createFeature(world.id, payload));
            newFeature();
        });
    });

    required<HTMLButtonElement>(root, "[data-delete-feature]").addEventListener("click", () => void run(null, async () => {
        if (!selectedFeature) throw new Error("Select a feature to delete.");
        if (featureHasEnvironmentRules(worldEnvironment, selectedFeature.id)) {
            throw new Error("This feature has environment rules attached. Remove or reassign them before deleting the feature.");
        }
        applyWorld(await api.deleteFeature(world.id, selectedFeature.id, world.version));
        newFeature();
    }));

    const procedure = select(expeditionForm, "procedure");
    for (const preset of procedurePresets) {
        const option = document.createElement("option");
        option.value = preset.presetKey;
        option.textContent = preset.displayName;
        procedure.append(option);
    }

    const expeditionHost = required<HTMLElement>(root, "[data-expedition-list]");
    const renderExpeditions = (expeditions = initialExpeditions): void => {
        expeditionHost.replaceChildren();
        if (expeditions.length === 0) {
            expeditionHost.append(emptyState(
                "No expeditions yet.",
                "Choose a procedure and starting hex below to begin the first expedition in this world."));
            return;
        }
        for (const expedition of expeditions) {
            expeditionHost.append(resourceButton(`${expedition.name} · ${expedition.procedureName}`, () =>
                navigate(`/expeditions/${expedition.id}`)));
        }
    };
    renderExpeditions();

    expeditionForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(expeditionForm, async () => {
            const expedition = await api.startExpedition(
                world.id,
                input(expeditionForm, "name").value,
                procedure.value,
                { q: integer(input(expeditionForm, "q")), r: integer(input(expeditionForm, "r")) });
            navigate(`/expeditions/${expedition.id}`);
        });
    });

    const sourceMapPlaceholder = required<HTMLElement>(root, "[data-source-maps]");
    const sourceMapDetails = sourceMapPlaceholder.closest("details");
    if (!(sourceMapDetails instanceof HTMLDetailsElement)) throw new Error("Source-map workspace requires a details container.");
    sourceMapWorkspace = new SourceMapWorkspace(
        sourceMapDetails,
        api,
        mapSurface,
        () => world,
        applyWorld,
        mapHint,
        error);
    await sourceMapWorkspace.initialize();

    updatePointFieldVisibility();
    applyWorld(world);
    return () => {
        disposed = true;
        sourceMapWorkspace?.dispose();
        mapSurface.dispose();
    };
}

function emptyState(title: string, nextStep: string): HTMLElement {
    const empty = document.createElement("div");
    empty.className = "hc-empty-state";
    const heading = document.createElement("strong");
    heading.textContent = title;
    const detail = document.createElement("span");
    detail.textContent = nextStep;
    empty.append(heading, detail);
    return empty;
}

function resourceButton(text: string, action: () => void): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "hc-resource-button";
    button.textContent = text;
    button.addEventListener("click", action);
    return button;
}

function setFormPending(form: HTMLFormElement, pending: boolean): void {
    form.dataset.pending = String(pending);
    for (const button of form.querySelectorAll<HTMLButtonElement>("button")) button.disabled = pending;
    const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]');
    if (!submit) return;
    if (pending) {
        submit.dataset.idleText = submit.textContent ?? "Save";
        submit.textContent = "Saving…";
    } else if (submit.dataset.idleText) {
        submit.textContent = submit.dataset.idleText;
        delete submit.dataset.idleText;
    }
}

function formatEnvironmentFact(fact: EnvironmentFact): string {
    if (fact.valueKind === "Measurement" && fact.measurement) {
        return `${formatNumber(fact.measurement.value)} ${fact.measurement.unit}`.trim();
    }
    return fact.tag?.trim() || "Unspecified";
}

function humanizeEnvironmentDimension(value: string): string {
    const text = value.replace(/[-_]+/g, " ").trim();
    return text ? text[0].toUpperCase() + text.slice(1) : value;
}

function formatNumber(value: number): string {
    return Number.isInteger(value) ? String(value) : value.toFixed(3).replace(/0+$/, "").replace(/\.$/, "");
}

function formatPoint(point: WorldPoint): string {
    return `${formatNumber(point.x)}, ${formatNumber(point.y)}`;
}

function technicalLine(text: string): HTMLElement {
    const line = document.createElement("p");
    line.className = "hc-hint";
    line.textContent = text;
    return line;
}

function selectedCellStorageKey(worldId: string): string {
    return `hex-crawl.world-editor.selected-cell.${worldId}`;
}

function restoreSelectedCell(worldId: string): HexCoordinate | null {
    try {
        const raw = sessionStorage.getItem(selectedCellStorageKey(worldId));
        if (!raw) return null;
        const parsed = JSON.parse(raw) as Partial<HexCoordinate>;
        return Number.isInteger(parsed.q) && Number.isInteger(parsed.r)
            ? { q: parsed.q!, r: parsed.r! }
            : null;
    } catch {
        return null;
    }
}

function persistSelectedCell(worldId: string, cell: HexCoordinate | null): void {
    try {
        if (cell) sessionStorage.setItem(selectedCellStorageKey(worldId), JSON.stringify(cell));
        else sessionStorage.removeItem(selectedCellStorageKey(worldId));
    } catch {
        // Selection persistence is presentation-only; authoring remains usable without web storage.
    }
}
