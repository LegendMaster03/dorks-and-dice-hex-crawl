import type { HexCrawlApi } from "../../api";
import { MapSurface } from "../../map-surface";
import { SourceMapWorkspace } from "./source-map-workspace";
import type { Location, Overworld, SpatialFeature, WorldPoint } from "../../types";
import { clearUiError, showUiError } from "../../ui-error";
import { customUnitFieldsVisible, gridWithSelectedUnit } from "./world-form";
import type { DistanceUnitKind } from "./world-form";
import { input, integer, numeric, required, select } from "../../ui/dom";

export async function renderWorldEditor(
    root: HTMLElement,
    api: HexCrawlApi,
    worldId: string,
    navigate: (route: string, replace?: boolean) => void): Promise<() => void> {
    let world = await api.getOverworld(worldId);
    const [profiles, initialExpeditions] = await Promise.all([
        api.getRuntimeProfiles(),
        api.listExpeditions(worldId)
    ]);
    let selectedLocation: Location | null = null;
    let selectedFeature: SpatialFeature | null = null;
    let placement: "location" | "point" | "line" | "region" | null = null;
    let draft: WorldPoint[] = [];
    let disposed = false;
    let sourceMapWorkspace: SourceMapWorkspace | null = null;

    root.innerHTML = `
        <section class="hc-page hc-workspace hc-world-editor">
            <header class="hc-page-header">
                <div><h1 data-title></h1><p>World map editor</p></div>
                <nav><button type="button" data-worlds>Overworlds</button><button type="button" data-reset-view>Reset map view</button></nav>
            </header>
            <div class="hc-error" data-error hidden role="alert"></div>
            <div class="hc-workspace-grid">
                <section class="hc-map-panel" aria-label="Overworld map">
                    <div class="hc-map-host" data-map></div>
                    <p class="hc-hint" data-map-hint>Use the controls to add locations and map features. Shift-drag or middle-drag pans; wheel zooms; the focused map also supports keyboard pan, zoom, and center-point selection.</p>
                </section>
                <aside class="hc-sidebar" aria-label="Overworld authoring controls">
                    <details open><summary>World and grid</summary><form class="hc-form" data-grid-form>
                        <label>Name <input name="name" required></label>
                        <label>Orientation <select name="orientation"><option value="PointyTop">Pointy top</option><option value="FlatTop">Flat top</option></select></label>
                        <label>Hex center distance <input name="scale" type="number" min="0.001" step="any" required><span class="hc-hint">Center-to-center distance between adjacent hexes.</span></label>
                        <label>Unit <select name="unitKind"><option value="Mile">Miles</option><option value="Kilometer">Kilometers</option><option value="Custom">Custom</option></select></label>
                        <div class="hc-custom-unit-fields" data-custom-unit hidden>
                            <label>Custom symbol <input name="unitSymbol" value="u"></label>
                            <label>Custom meters per unit <input name="metersPerUnit" type="number" min="0.001" step="any" value="1"></label>
                        </div>
                        <details><summary>Advanced grid alignment</summary><div class="hc-form"><p class="hc-hint">These values define the internal world-coordinate frame. Most maps can keep the existing values.</p>
                            <div class="hc-inline"><label>Origin X <input name="originX" type="number" step="any"></label><label>Origin Y <input name="originY" type="number" step="any"></label></div>
                            <label>Rotation degrees <input name="rotation" type="number" step="any"></label>
                            <label>Hex radius (world units) <input name="radius" type="number" min="0.001" step="any"></label>
                        </div></details>
                        <button type="submit" class="hc-primary-action">Save world and grid</button>
                    </form></details>

                    <details open><summary>Locations</summary>
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

                    <details><summary>Expeditions</summary><div data-expedition-list></div>
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

    const run = async (form: HTMLFormElement | null, action: () => Promise<void>): Promise<void> => {
        if (form?.dataset.pending === "true") return;
        clearUiError(error);
        if (form) setFormPending(form, true);
        try {
            await action();
        } catch (value) {
            if (!disposed) showUiError(error, value);
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
        title.textContent = next.name;
        fillGrid();
        renderLocations();
        renderFeatures();
        mapSurface.requestRender();
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

    required<HTMLButtonElement>(root, "[data-worlds]").addEventListener("click", () => navigate("/worlds"));
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
        applyWorld(await api.deleteFeature(world.id, selectedFeature.id, world.version));
        newFeature();
    }));

    const procedure = select(expeditionForm, "procedure");
    for (const profile of profiles) {
        const option = document.createElement("option");
        option.value = profile.key;
        option.textContent = profile.name;
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
                navigate(`/worlds/${world.id}/expeditions/${expedition.id}`)));
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
            navigate(`/worlds/${world.id}/expeditions/${expedition.id}`);
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

