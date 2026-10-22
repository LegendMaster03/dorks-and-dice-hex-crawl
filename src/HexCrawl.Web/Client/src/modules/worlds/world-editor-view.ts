import { HexCrawlApiError, type HexCrawlApi } from "../../api";
import type { EnvironmentAnnotation, EnvironmentFact, WorldEnvironment } from "../../environment-types";
import { MapSurface } from "../../map-surface";
import { defaultTerrainColors, defaultFeatureColors, mapCategoryColor, type MapAppearance } from "../../map-appearance";
import { enhanceExpeditionSetup } from "../expeditions/expedition-setup";
import { SourceMapWorkspace } from "./source-map-workspace";
import type { Location, Overworld, SpatialFeature, WorldPoint, WorldCellGeometry, WorldCellId, PlayerMapDisclosure } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { input, numeric, required, select } from "../../ui/dom";
import { applyGuidedExperience, guidancePreferenceButton } from "../../ui/guidance";
import {
    addEnvironmentFact,
    featureHasEnvironmentRules,
    featureIntersectsPolygon,
    pointInPolygon,
    measurementFact,
    removeEnvironmentFact,
    tagFact
} from "./selected-cell-authoring";

export async function renderWorldEditor(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    const [initialWorld, initialWorldEnvironment, initialExpeditions] = await Promise.all([
        api.getOverworld(worldId),
        api.getWorldEnvironment(worldId),
        api.listExpeditions(worldId)
    ]);
    let world = initialWorld;
    let worldEnvironment = initialWorldEnvironment;
    let selectedLocation: Location | null = null;
    let selectedPeriodicCell: WorldCellGeometry | null = null;
    let selectedFeature: SpatialFeature | null = null;
    let placement: "location" | "point" | "line" | "region" | null = null;
    let draft: WorldPoint[] = [];
    let disposed = false;
    let sourceMapWorkspace: SourceMapWorkspace | null = null;

    applyGuidedExperience(root);
    root.innerHTML = `
        <section class="hc-page hc-workspace hc-world-editor">
            <header class="hc-page-header">
                <div><span class="hc-sheet-kicker">Map preparation</span><h1 data-title></h1><p>World map editor · source maps, locations, and features.</p></div>
                <nav class="hc-button-row"><button type="button" data-worlds>All overworlds</button><button type="button" data-reset-view>Reset map view</button><span data-guidance-controls></span></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-workspace-grid">
                <section class="hc-map-panel" aria-label="Overworld map">
                    <div class="hc-map-host" data-map></div>
                    <p class="hc-hint" data-map-hint>Click to select or author map content; drag to pan; wheel to zoom. The focused map also supports keyboard pan, zoom, and center-point selection.</p>
                    <details><summary>Export map as PNG</summary>
                        <form class="hc-form" data-export-form>
                            <label>Audience <select name="audience"><option value="gm">GM (all authored details)</option><option value="player">Players (only released details)</option></select></label>
                            <label>Export area <select name="scope">
                                <option value="visible">Export visible area (PNG)</option>
                                <option value="full">Full bounded map</option>
                            </select></label>
                            <label>Background <select name="background"><option value="light">Light</option><option value="dark">Dark</option><option value="transparent">Transparent</option></select></label>
                            <label data-full-export-size>Maximum image dimension <select name="resolution"><option value="1600">1600 px</option><option value="2400" selected>2400 px</option><option value="3200">3200 px</option></select></label>
                            <label><input name="grid" type="checkbox" checked> Draw tile grid (full-map export)</label>
                            <label><input name="labels" type="checkbox" checked> Draw labels (full-map export)</label>
                            <button type="submit" class="hc-primary-action" data-export-map>Export PNG</button>
                            <p class="hc-hint">Player exports contain only objects explicitly released below, but selected source images may already contain baked secrets. Review the PNG before sharing. Full-map exports require finite boundaries.</p>
                        </form>
                    </details>
                </section>
                <aside class="hc-sidebar" aria-label="Overworld authoring controls">
                    <details open class="hc-selected-cell-panel" data-selected-cell-panel><summary>Selected cell</summary>
                        <div class="hc-form" data-selected-cell-empty>
                            <p class="hc-hint">Select a map cell to edit terrain, locations, routes, features, and explicit environment rules in tabletop terms.</p>
                        </div>
                    </details>

                    <details><summary>World name</summary><form class="hc-form" data-world-form>
                        <label>Name <input name="name" required></label>
                        <button type="submit" class="hc-primary-action">Save world name</button>
                    </form></details>

                    <details><summary>Map appearance</summary>
                        <p class="hc-hint">Category colors are visual preferences only. Terrain and feature rules are unchanged.</p>
                        <div data-map-palette></div>
                        <form class="hc-form" data-palette-form>
                            <label>Layer <select name="layer"><option value="terrain">Tile terrain</option><option value="feature">Feature lines, points, and regions</option><option value="label">Text labels</option></select></label>
                            <label>Category <input name="category" placeholder="forest, river, urban..." required maxlength="80"></label>
                            <label>Color <input name="color" type="color" value="#538a55" required></label>
                            <button type="submit">Set category color</button>
                        </form>
                    </details>
                    <details><summary>Player map visibility</summary>
                        <p class="hc-hint">All features, locations, labels, terrain colors, and source images are excluded by default. Release only the details players may see. These controls affect static player map exports, not expedition discovery mechanics.</p>
                        <div data-player-map-disclosure></div>
                    </details>
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
                        <form class="hc-form" data-expedition-form></form>
                    </details>
                    <details><summary>Source-map metadata</summary><p data-source-maps></p></details>
                </aside>
            </div>
        </section>`;

    const error = required<HTMLElement>(root, "[data-error]");
    const title = required<HTMLElement>(root, "[data-title]");
    const mapHint = required<HTMLElement>(root, "[data-map-hint]");
    const mapSurface = new MapSurface(required(root, "[data-map]"), () => world, point => onMapClick(point), api);
    const gridForm = required<HTMLFormElement>(root, "[data-world-form]");
    const locationForm = required<HTMLFormElement>(root, "[data-location-form]");
    const featureForm = required<HTMLFormElement>(root, "[data-feature-form]");
    const paletteForm = required<HTMLFormElement>(root, "[data-palette-form]");
    const exportForm = required<HTMLFormElement>(root, "[data-export-form]");
    const disclosureHost = required<HTMLElement>(root, "[data-player-map-disclosure]");
     const draftLabel = required<HTMLElement>(root, "[data-draft]");
    required<HTMLElement>(root, "[data-guidance-controls]").append(guidancePreferenceButton(root));
    const selectedCellEmpty = required<HTMLElement>(root, "[data-selected-cell-empty]");
    const advancedWorldObjects = required<HTMLDetailsElement>(root, "[data-advanced-world-objects]");
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

    const applyWorld = (next: Overworld): void => {
        world = next;
        if (worldEnvironment.version !== next.version) {
            worldEnvironment = { ...worldEnvironment, version: next.version };
        }
        title.textContent = next.name;
        fillGrid();
        renderLocations();
        renderFeatures();
        renderPalette();
        renderDisclosure();
        mapSurface.setTerrainAnnotations(worldEnvironment.annotations);
        renderPeriodicCell();
        mapSurface.requestRender();
    };

    const applyWorldEnvironment = (next: WorldEnvironment): void => {
        worldEnvironment = next;
        if (world.version !== next.version) world = { ...world, version: next.version };
        mapSurface.setTerrainAnnotations(next.annotations);
        renderPeriodicCell();
        mapSurface.requestRender();
    };

    const saveWorldEnvironment = async (annotations: EnvironmentAnnotation[]): Promise<void> => {
        applyWorldEnvironment(await api.replaceWorldEnvironment(world.id, {
            expectedVersion: worldEnvironment.version,
            annotations
        }));
    };

    const fillGrid = (): void => {
        input(gridForm, "name").value = world.name;
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

    const renderPalette = (): void => {
        const host = required<HTMLElement>(root, "[data-map-palette]");
        host.replaceChildren();
        for (const layer of ["terrain", "feature", "label"] as const) {
            const palette = layer === "terrain" ? defaultTerrainColors : layer === "feature" ? defaultFeatureColors : {};
            const overrides = layer === "terrain" ? world.mapAppearance?.terrainColors
                : layer === "feature" ? world.mapAppearance?.featureColors : world.mapAppearance?.labelColors;
            const categories = new Set([...Object.keys(palette), ...Object.keys(overrides ?? {})]);
            if (layer === "terrain") {
                for (const annotation of worldEnvironment.annotations) {
                    for (const fact of annotation.facts) {
                        if (fact.dimension.trim().toLowerCase() === "terrain" && fact.valueKind === "Tag" && fact.tag)
                            categories.add(fact.tag.toLowerCase());
                    }
                }
            } else {
                for (const feature of world.features) categories.add(feature.category.toLowerCase());
                if (layer === "label")
                    for (const location of world.locations) categories.add(location.category.toLowerCase());
            }
            const title = document.createElement("strong");
            title.textContent = layer === "terrain" ? "Tile terrain"
                : layer === "feature" ? "Feature categories" : "Feature and location labels";
            host.append(title);
            for (const category of [...categories].sort()) {
                const row = document.createElement("div");
                row.className = "hc-discovery-row";
                const swatch = document.createElement("span");
                swatch.style.display = "inline-block";
                swatch.style.width = "20px";
                swatch.style.height = "20px";
                swatch.style.border = "1px solid currentColor";
                const color = mapCategoryColor(world.mapAppearance, category, layer);
                swatch.style.backgroundColor = color ?? "transparent";
                const label = document.createElement("span");
                label.textContent = category + (overrides?.[category] ? " (custom)" : "");
                row.append(swatch, label);
                const edit = document.createElement("button");
                edit.type = "button";
                edit.textContent = "Edit";
                edit.addEventListener("click", () => {
                    select(paletteForm, "layer").value = layer;
                    input(paletteForm, "category").value = category;
                    input(paletteForm, "color").value = color ?? "#20251f";
                    input(paletteForm, "category").focus();
                });
                row.append(edit);
                if (overrides && Object.keys(overrides).some(key => key.toLowerCase() === category)) {
                    const reset = document.createElement("button");
                    reset.type = "button";
                    reset.textContent = "Reset";
                    reset.addEventListener("click", () => void run(null, async () => {
                        const appearance = currentAppearance();
                        const key = layer === "terrain" ? "terrainColors"
                            : layer === "feature" ? "featureColors" : "labelColors";
                        const existing = { ...appearance[key] };
                        for (const name of Object.keys(existing))
                            if (name.toLowerCase() === category) delete existing[name];
                        applyWorld(await api.updateMapAppearance(world.id, {
                            ...appearance, [key]: existing
                        }, world.version));
                    }));
                    row.append(reset);
                }
                host.append(row);
            }
        }
    };

    const currentAppearance = (): MapAppearance => ({
        terrainColors: { ...world.mapAppearance?.terrainColors },
        featureColors: { ...world.mapAppearance?.featureColors },
        labelColors: { ...world.mapAppearance?.labelColors }
    });

    const currentDisclosure = (): PlayerMapDisclosure => ({
        visibleFeatureIds: [...world.playerMapDisclosure?.visibleFeatureIds ?? []],
        visibleLocationIds: [...world.playerMapDisclosure?.visibleLocationIds ?? []],
        visibleFeatureLabelIds: [...world.playerMapDisclosure?.visibleFeatureLabelIds ?? []],
        visibleLocationLabelIds: [...world.playerMapDisclosure?.visibleLocationLabelIds ?? []],
        visibleSourceMapIds: [...world.playerMapDisclosure?.visibleSourceMapIds ?? []],
        showTerrain: world.playerMapDisclosure?.showTerrain ?? false,
        showGrid: world.playerMapDisclosure?.showGrid ?? true
    });

    const updateDisclosure = async (next: PlayerMapDisclosure): Promise<void> => {
        applyWorld(await api.updatePlayerMapDisclosure(world.id, next, world.version));
    };

    const renderDisclosure = (): void => {
        disclosureHost.replaceChildren();
        const state = currentDisclosure();
        const toggleFlag = (label: string, key: "showTerrain" | "showGrid"): void => {
            const line = document.createElement("label");
            const choice = document.createElement("input");
            choice.type = "checkbox";
            choice.checked = state[key];
            choice.addEventListener("change", () => void run(null, () =>
                updateDisclosure({ ...currentDisclosure(), [key]: choice.checked })));
            line.append(choice, document.createTextNode(" " + label));
            disclosureHost.append(line);
        };
        const toggleSubject = (name: string, id: string, key: "visibleFeatureIds" | "visibleLocationIds"
            | "visibleFeatureLabelIds" | "visibleLocationLabelIds" | "visibleSourceMapIds",
            extra?: string): void => {
            const line = document.createElement("label");
            line.className = "hc-discovery-row";
            const choice = document.createElement("input");
            choice.type = "checkbox";
            choice.checked = state[key].includes(id);
            if (key === "visibleFeatureLabelIds" && !state.visibleFeatureIds.includes(id)
                || key === "visibleLocationLabelIds" && !state.visibleLocationIds.includes(id))
                choice.disabled = true;
            choice.addEventListener("change", () => void run(null, async () => {
                const next = currentDisclosure();
                const selection = new Set(next[key]);
                if (choice.checked) selection.add(id);
                else selection.delete(id);
                const updated: PlayerMapDisclosure = { ...next, [key]: [...selection] };
                if (key === "visibleFeatureIds" && !choice.checked)
                    updated.visibleFeatureLabelIds = updated.visibleFeatureLabelIds.filter(value => value !== id);
                if (key === "visibleLocationIds" && !choice.checked)
                    updated.visibleLocationLabelIds = updated.visibleLocationLabelIds.filter(value => value !== id);
                await updateDisclosure(updated);
            }));
            line.append(choice, document.createTextNode(" " + name + (extra ?? "")));
            disclosureHost.append(line);
        };
        toggleFlag("Show terrain colors", "showTerrain");
        toggleFlag("Show grid", "showGrid");
        for (const [title, values, key, labelKey] of [
            ["Features", world.features, "visibleFeatureIds", "visibleFeatureLabelIds"],
            ["Locations", world.locations, "visibleLocationIds", "visibleLocationLabelIds"]
        ] as const) {
            const heading = document.createElement("strong");
            heading.textContent = title;
            disclosureHost.append(heading);
            for (const item of values) {
                toggleSubject(item.name, item.id, key, " · " + item.category);
                toggleSubject("Show label: " + item.name, item.id, labelKey);
            }
        }
        const heading = document.createElement("strong");
        heading.textContent = "Player source images";
        disclosureHost.append(heading);
        const playerMaps = world.sourceMaps.filter(map => map.role === "Player");
        for (const map of playerMaps)
            toggleSubject(map.name, map.id, "visibleSourceMapIds", " · may contain baked information");
        if (!playerMaps.length) {
            const message = document.createElement("p");
            message.className = "hc-hint";
            message.textContent = "No source images have the Player role.";
            disclosureHost.append(message);
        }
        const reset = document.createElement("button");
        reset.type = "button";
        reset.textContent = "Hide all player details";
        reset.addEventListener("click", () => void run(null, () => updateDisclosure({
            visibleFeatureIds: [],
            visibleLocationIds: [],
            visibleFeatureLabelIds: [],
            visibleLocationLabelIds: [],
            visibleSourceMapIds: [],
            showTerrain: false,
            showGrid: true
        })));
        disclosureHost.append(reset);
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
        placement = null;
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
        placement = null;
        selectedFeature = null;
        featureForm.reset();
        input(featureForm, "id").value = "";
        draft = [];
        updatePointFieldVisibility();
        updateDraftLabel();
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

    const sameQualifiedCell = (left: WorldCellId | null | undefined, right: WorldCellId): boolean =>
        !!left && left.tilingId === right.tilingId
        && left.address.motifCellId === right.address.motifCellId
        && left.address.translation.u === right.address.translation.u
        && left.address.translation.v === right.address.translation.v;

    function renderPeriodicCell(): void {
        if (!world.tiling) return;
        const panel = selectedCellEmpty;
        panel.hidden = false;
        panel.replaceChildren();
        const cell = selectedPeriodicCell;
        if (!cell) {
            const empty = document.createElement("p");
            empty.className = "hc-hint";
            empty.textContent = "Select a polygonal cell to author its contents.";
            panel.append(empty);
            return;
        }
        const intro = document.createElement("p");
        intro.className = "hc-hint";
        intro.textContent =
            "Selected world cell. Editing local facts does not move the party or reveal hidden content.";
        const summary = document.createElement("p");
        summary.textContent = `Center: ${formatPoint(cell.center)} · ${cell.polygon.length} sides`;
        panel.append(intro, summary);

        const locations = world.locations.filter(location =>
            pointInPolygon(location.position, cell.polygon));
        const features = world.features.filter(feature =>
            featureIntersectsPolygon(feature, cell.polygon));
        const objects = document.createElement("div");
        objects.className = "hc-stack";
        const label = document.createElement("h4");
        label.textContent = "Locations and features intersecting this cell";
        objects.append(label);
        if (locations.length + features.length === 0) {
            const empty = document.createElement("p");
            empty.className = "hc-hint";
            empty.textContent = "No authored map objects touch this cell.";
            objects.append(empty);
        }
        for (const location of locations) {
            const row = document.createElement("div");
            row.className = "hc-discovery-row";
            const description = document.createElement("span");
            description.textContent = `${location.name} · ${location.category}`;
            const edit = document.createElement("button");
            edit.type = "button";
            edit.textContent = "Edit location";
            edit.addEventListener("click", () => {
                loadLocation(location);
                advancedWorldObjects.open = true;
                const section = locationForm.closest("details");
                if (section) section.open = true;
                input(locationForm, "name").focus();
            });
            row.append(description, edit);
            objects.append(row);
        }
        for (const feature of features) {
            const row = document.createElement("div");
            row.className = "hc-discovery-row";
            const description = document.createElement("span");
            description.textContent = `${feature.name} · ${feature.category}`;
            const edit = document.createElement("button");
            edit.type = "button";
            edit.textContent = "Edit feature";
            edit.addEventListener("click", () => {
                loadFeature(feature);
                advancedWorldObjects.open = true;
                const section = featureForm.closest("details");
                if (section) section.open = true;
                input(featureForm, "name").focus();
            });
            row.append(description, edit);
            objects.append(row);
        }
        panel.append(objects);

        const annotations = worldEnvironment.annotations.filter(annotation =>
            annotation.scope.kind === "Cell" && sameQualifiedCell(annotation.scope.cell, cell.id));
        const terrainFacts = annotations.flatMap(annotation =>
            annotation.facts.filter(fact => fact.dimension.trim().toLowerCase() === "terrain"));
        const terrainForm = document.createElement("form");
        terrainForm.className = "hc-form";
        terrainForm.innerHTML = `
            <label>Terrain / biome <input name="terrain" list="hc-periodic-terrain" placeholder="Forest, plains, custom" required></label>
            <datalist id="hc-periodic-terrain"><option value="forest"><option value="swamp"><option value="grassland"><option value="desert"><option value="mountain"></datalist>
            <button type="submit" class="hc-primary-action">Save cell terrain</button>
            <button type="button" data-clear-terrain>Clear terrain</button>`;
        const terrainInput = input(terrainForm, "terrain");
        if (terrainFacts.length === 1 && terrainFacts[0].valueKind === "Tag")
            terrainInput.value = terrainFacts[0].tag ?? "";
        const replaceTerrain = async (value: string | null): Promise<void> => {
                let next = worldEnvironment.annotations.map(annotation =>
                    annotation.scope.kind === "Cell" && sameQualifiedCell(annotation.scope.cell, cell.id)
                        ? { ...annotation, facts: annotation.facts.filter(fact =>
                            fact.dimension.trim().toLowerCase() !== "terrain") }
                        : annotation).filter(annotation => annotation.facts.length > 0);
                if (value?.trim()) next = addEnvironmentFact(next,
                    { kind: "Cell", cell: cell.id, featureId: null },
                    tagFact("terrain", value.trim(), "world-editor:selected-cell", null));
                await saveWorldEnvironment(next);
        };
        terrainForm.addEventListener("submit", event => {
            event.preventDefault();
            void run(terrainForm, () => replaceTerrain(terrainInput.value));
        });
        required<HTMLButtonElement>(terrainForm, "[data-clear-terrain]")
            .addEventListener("click", () => void run(terrainForm, () => replaceTerrain(null)));
        panel.append(terrainForm);

        const facts = document.createElement("div");
        facts.className = "hc-stack";
        for (const annotation of annotations) {
            for (const fact of annotation.facts) {
                const line = document.createElement("div");
                line.className = "hc-discovery-row";
                const description = document.createElement("span");
                description.textContent = `${fact.dimension}: ${formatEnvironmentFact(fact)}`;
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "Remove";
                remove.addEventListener("click", () => void run(null, async () =>
                    saveWorldEnvironment(removeEnvironmentFact(
                        worldEnvironment.annotations, annotation.id, fact.id))));
                line.append(description, remove);
                facts.append(line);
            }
        }
        if (facts.childElementCount === 0) {
            const empty = document.createElement("p");
            empty.className = "hc-hint";
            empty.textContent = "No explicit local environment facts are defined.";
            facts.append(empty);
        }
        panel.append(facts);

        const localForm = document.createElement("form");
        localForm.className = "hc-form";
        localForm.innerHTML = `
            <label>Environmental detail <input name="dimension" list="hc-cell-environment-dimensions" required></label>
            <label>Value <input name="value" required></label>
            <button type="submit">Add local environment fact</button>`;
        localForm.addEventListener("submit", event => {
            event.preventDefault();
            void run(localForm, async () => {
                const dimension = input(localForm, "dimension").value.trim();
                const value = input(localForm, "value").value.trim();
                if (!dimension || !value) throw new Error("An environment dimension and value are required.");
                if (dimension.toLowerCase() === "terrain") {
                    await replaceTerrain(value);
                    return;
                }
                await saveWorldEnvironment(addEnvironmentFact(
                    worldEnvironment.annotations,
                    { kind: "Cell", cell: cell.id, featureId: null },
                    tagFact(dimension, value, "world-editor:selected-cell", null)));
            });
        });
        panel.append(localForm);

        // Scoped rules use the same authoritative cell, world, and feature
        // identities regardless of the polygon's geometry.
        const scopedDetails = document.createElement("details");
        scopedDetails.className = "hc-stack";
        const scopedSummary = document.createElement("summary");
        scopedSummary.textContent = "Advanced environment and feature mechanics";
        scopedDetails.append(scopedSummary);
        const scopedForm = document.createElement("form");
        scopedForm.className = "hc-form";
        scopedForm.innerHTML = `
            <label>Exact scope <select name="scope"><option value="Cell">Selected cell</option><option value="SpatialFeature">Intersecting feature</option><option value="World">Entire world</option></select></label>
            <label data-scope-feature hidden>Feature <select name="featureId"></select></label>
            <label>Dimension <input name="dimension" required></label>
            <label>Value type <select name="valueKind"><option value="Tag">Tag/value</option><option value="Measurement">Measurement</option></select></label>
            <label data-scope-tag>Value <input name="tag"></label>
            <div data-scope-measurement hidden class="hc-inline"><label>Numeric value <input name="measurement" type="number" step="any"></label><label>Unit <input name="unit"></label></div>
            <label>Provenance <input name="provenance"></label>
            <label>Note <input name="note"></label>
            <button type="submit">Add environment fact</button>`;
        const featureChoice = select(scopedForm, "featureId");
        for (const feature of features) {
            const option = document.createElement("option");
            option.value = feature.id;
            option.textContent = `${feature.name} · ${feature.category}`;
            featureChoice.append(option);
        }
        const scopeChoice = select(scopedForm, "scope");
        const valueKindChoice = select(scopedForm, "valueKind");
        const syncScopeFields = (): void => {
            const feature = scopeChoice.value === "SpatialFeature";
            required<HTMLElement>(scopedForm, "[data-scope-feature]").hidden = !feature;
            featureChoice.required = feature;
            const measurement = valueKindChoice.value === "Measurement";
            required<HTMLElement>(scopedForm, "[data-scope-tag]").hidden = measurement;
            required<HTMLElement>(scopedForm, "[data-scope-measurement]").hidden = !measurement;
            input(scopedForm, "tag").required = !measurement;
            input(scopedForm, "measurement").required = measurement;
            input(scopedForm, "unit").required = measurement;
        };
        scopeChoice.addEventListener("change", syncScopeFields);
        valueKindChoice.addEventListener("change", syncScopeFields);
        syncScopeFields();
        scopedForm.addEventListener("submit", event => {
            event.preventDefault();
            void run(scopedForm, async () => {
                const dimension = input(scopedForm, "dimension").value.trim();
                const provenance = input(scopedForm, "provenance").value.trim() || null;
                const note = input(scopedForm, "note").value.trim() || null;
                const fact = valueKindChoice.value === "Measurement"
                    ? measurementFact(dimension, numeric(input(scopedForm, "measurement")),
                        input(scopedForm, "unit").value, provenance, note)
                    : tagFact(dimension, input(scopedForm, "tag").value, provenance, note);
                const scopeKind = scopeChoice.value;
                const featureId = featureChoice.value;
                if (scopeKind === "SpatialFeature"
                    && !features.some(item => item.id === featureId))
                    throw new Error("Select an intersecting feature.");
                const scope: EnvironmentAnnotation["scope"] = scopeKind === "World"
                    ? { kind: "World", cell: null, featureId: null }
                    : scopeKind === "SpatialFeature"
                        ? { kind: "SpatialFeature", cell: null, featureId }
                        : { kind: "Cell", cell: cell.id, featureId: null };
                if (scopeKind === "Cell" && dimension.toLowerCase() === "terrain"
                    && fact.valueKind === "Tag") {
                    await replaceTerrain(fact.tag ?? "");
                    return;
                }
                await saveWorldEnvironment(addEnvironmentFact(worldEnvironment.annotations, scope, fact));
            });
        });
        scopedDetails.append(scopedForm);

        const applicable = worldEnvironment.annotations.filter(annotation =>
            annotation.scope.kind === "World"
            || annotation.scope.kind === "SpatialFeature"
                && features.some(feature => feature.id === annotation.scope.featureId));
        for (const annotation of applicable) {
            for (const fact of annotation.facts) {
                const line = document.createElement("div");
                line.className = "hc-discovery-row";
                const description = document.createElement("span");
                description.textContent = `${annotation.scope.kind}: ${fact.dimension} · ${formatEnvironmentFact(fact)}`;
                const remove = document.createElement("button");
                remove.type = "button";
                remove.textContent = "Remove";
                remove.addEventListener("click", () => void run(null, () =>
                    saveWorldEnvironment(removeEnvironmentFact(
                        worldEnvironment.annotations, annotation.id, fact.id))));
                line.append(description, remove);
                scopedDetails.append(line);
            }
        }
        panel.append(scopedDetails);

        const addLocation = document.createElement("button");
        addLocation.type = "button";
        addLocation.textContent = "Add location at this cell's center";
        addLocation.addEventListener("click", () => {
            newLocation();
            input(locationForm, "x").value = String(cell.center.x);
            input(locationForm, "y").value = String(cell.center.y);
            advancedWorldObjects.open = true;
            const section = locationForm.closest("details");
            if (section) section.open = true;
            input(locationForm, "name").focus();
        });
        panel.append(addLocation);
    }

    // A generalized cell is selected by server-qualified identity, not a fake
    // q/r address. Shared-boundary ambiguities stay in MapSurface.
    mapSurface.setCellSelectionHandler(cell => {
        selectedPeriodicCell = cell;
        renderPeriodicCell();
        if (cell) root.dispatchEvent(
            new CustomEvent("hex-crawl:world-cell-selected", { detail: cell.id }));
    });

    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
    exportForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(exportForm, async () => {
            const scope = select(exportForm, "scope").value;
            const background = select(exportForm, "background").value as
                "light" | "dark" | "transparent";
            const preview = select(exportForm, "audience").value === "player"
                ? await api.getPlayerMapPreview(world.id) : undefined;
            if (preview && preview.world.version !== world.version)
                throw new Error("The map changed before export. Reload and try again.");
            if (scope === "visible") {
                await mapSurface.exportViewportPng(world.name, background, preview);
            } else {
                const dimension = Number(select(exportForm, "resolution").value);
                if (![1600, 2400, 3200].includes(dimension))
                    throw new Error("Select an available image resolution.");
                await mapSurface.exportFullMapPng(world.name, background,
                    dimension as 1600 | 2400 | 3200,
                    input(exportForm, "grid").checked, input(exportForm, "labels").checked, preview);
            }
        });
    });
    select(exportForm, "scope").addEventListener("change", () => {
        required<HTMLElement>(exportForm, "[data-full-export-size]").hidden =
            select(exportForm, "scope").value !== "full";
    });
    required<HTMLElement>(exportForm, "[data-full-export-size]").hidden = true;
    select(exportForm, "scope").querySelector<HTMLOptionElement>('option[value="full"]')!.disabled =
        !world.tiling.playableBounds;
    paletteForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(paletteForm, async () => {
            const layer = select(paletteForm, "layer").value;
            const category = input(paletteForm, "category").value.trim().toLowerCase();
            const color = input(paletteForm, "color").value.toUpperCase();
            if (!/^[\p{L}\p{N}_ .:\/-]{1,80}$/u.test(category))
                throw new Error("Category names must contain 1–80 letters, numbers, spaces, or _ . : / -.");
            if (!/^#[0-9A-F]{6}$/.test(color)) throw new Error("Choose a six-digit hex color.");
            const key = layer === "terrain" ? "terrainColors"
                            : layer === "feature" ? "featureColors" : "labelColors";
            const appearance = currentAppearance();
            applyWorld(await api.updateMapAppearance(world.id, {
                ...appearance,
                [key]: { ...appearance[key], [category]: color }
            }, world.version));
        });
    });
    required<HTMLButtonElement>(root, "[data-reset-view]").addEventListener("click", () => mapSurface.resetView());

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
        placement = null;
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
            applyWorld(await api.renameOverworld(
                world.id, input(gridForm, "name").value.trim(), world.version));
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

    required<HTMLButtonElement>(root, "[data-delete-location]").addEventListener("click", () => {
        if (!selectedLocation) {
            void run(null, async () => { throw new Error("Select a location to delete."); });
            return;
        }
        if (!confirmDeletion(`Delete location “${selectedLocation.name}”? This can not be undone.`)) return;
        void run(null, async () => {
            applyWorld(await api.deleteLocation(world.id, selectedLocation!.id, world.version));
            newLocation();
        });
    });

    featureForm.addEventListener("submit", event => {
        event.preventDefault();
        void run(featureForm, async () => {
            const kind = select(featureForm, "kind").value as "Point" | "Line" | "Region";
            if (kind === "Line" && draft.length < 2)
                throw new Error("A line requires at least two points. Select Author geometry on map and click two or more positions before saving.");
            if (kind === "Region") {
                if (draft.length < 3)
                    throw new Error("A region requires at least three points. Select Author geometry on map and click three or more positions before saving.");
                const twiceArea = draft.reduce((area, point, index) => {
                    const next = draft[(index + 1) % draft.length];
                    return area + point.x * next.y - next.x * point.y;
                }, 0);
                if (Math.abs(twiceArea) < 1e-9)
                    throw new Error("A region requires three or more points enclosing an area. Reauthor its boundary before saving.");
            }
            if (kind === "Point" && (!Number.isFinite(Number(input(featureForm, "x").value))
                || !Number.isFinite(Number(input(featureForm, "y").value))
                || !input(featureForm, "x").value.trim() || !input(featureForm, "y").value.trim()))
                throw new Error("A point requires a valid position. Select Author geometry on map or enter its X and Y coordinates.");
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

    required<HTMLButtonElement>(root, "[data-delete-feature]").addEventListener("click", () => {
        if (!selectedFeature) {
            void run(null, async () => { throw new Error("Select a feature to delete."); });
            return;
        }
        if (!confirmDeletion(`Delete feature “${selectedFeature.name}”? This can not be undone.`)) return;
        void run(null, async () => {
            if (featureHasEnvironmentRules(worldEnvironment, selectedFeature!.id)) {
                throw new Error("This feature has environment rules attached. Remove or reassign them before deleting the feature.");
            }
            applyWorld(await api.deleteFeature(world.id, selectedFeature!.id, world.version));
            newFeature();
        });
    });

     const expeditionHost = required<HTMLElement>(root, "[data-expedition-list]");
    const renderExpeditions = (expeditions = initialExpeditions): void => {
        expeditionHost.replaceChildren();
        if (expeditions.length === 0) {
            expeditionHost.append(emptyState(
                "No expeditions yet.",
                "Choose an exploration ruleset and starting cell below to begin the first expedition in this world."));
            return;
        }
        for (const expedition of expeditions) {
            expeditionHost.append(resourceButton(`${expedition.name} · ${expedition.procedureName}`, () =>
                navigate(`/expeditions/${expedition.id}`)));
        }
    };
    renderExpeditions();

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
    // Existing hex worlds and new periodic worlds share a cell-authoritative
    // ruleset selection and start form; the retired start route is unused.
    const disposeExpeditionSetup = await enhanceExpeditionSetup(root, api, worldId, navigate);

    updatePointFieldVisibility();
    applyWorld(world);
    return () => {
        disposed = true;
        sourceMapWorkspace?.dispose();
        disposeExpeditionSetup();
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

function confirmDeletion(message: string): boolean {
    return window.confirm(message);
}
